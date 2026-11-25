using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Files;

internal sealed class JsonProjectFile : IProjectFile
{
    private const int Version = 4;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record LegacyProjectData(double Width, double Depth, LightData?[]? Lights);
    private sealed record ProjectData(int FormatVersion, double Height, VertexData?[]? Vertices, LightData?[]? Lights,
        ElementData?[]? Elements);
    private sealed record ElementData(string? Type, double? Height, int? Wall = null,
        double? Offset = null, double? Width = null, double? Bottom = null,
        VertexData? Start = null, VertexData? End = null, OpeningData?[]? Openings = null);
    private sealed record OpeningData(double? Offset, double? Width, double? Bottom, double? Height);
    private sealed record VertexData(double? X, double? Y);
    private sealed record LightData(float? X, float? Y, double[]? Vertical, double[]? Horizontal, double[]?[]? Candela);

    public async Task<LightingProject> ReadAsync(Stream stream)
    {
        try
        {
            return await ReadProjectAsync(stream).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Повреждён файл проекта: некорректный JSON или структура данных.", ex);
        }
    }

    private static async Task<LightingProject> ReadProjectAsync(Stream stream)
    {
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number))
            throw new FormatException("В файле отсутствует корректная версия формата проекта.");
        Room room;
        LightData?[]? lightData;
        InteriorElement[] elements = [];
        switch (number)
        {
            case 1:
                var legacy = root.Deserialize<LegacyProjectData>(JsonOptions)!;
                if (!(legacy.Width is >= 2 and <= 30 && legacy.Depth is >= 2 and <= 30))
                    throw new FormatException("Некорректные размеры помещения.");
                room = new Room(legacy.Width, legacy.Depth);
                lightData = legacy.Lights;
                break;
            case 2:
            case 3:
            case Version:
                var data = root.Deserialize<ProjectData>(JsonOptions)!;
                if (data.Vertices is null || data.Vertices.Any(p => p?.X is null || p.Y is null))
                    throw new FormatException("Некорректные вершины помещения.");
                try
                {
                    room = new Room(data.Vertices.Select(p => new RoomPoint(p!.X!.Value, p.Y!.Value)), data.Height);
                }
                catch (ArgumentException error)
                {
                    throw new FormatException(error.Message, error);
                }
                if (number >= 3)
                {
                    if (data.Elements is null)
                        throw new FormatException("В файле отсутствует список элементов помещения.");
                    elements = data.Elements.Select(element => ReadElement(element, number == Version)).ToArray();
                    var error = InteriorGeometry.Validate(room, elements);
                    if (error is not null)
                        throw new FormatException(error);
                }
                lightData = data.Lights;
                break;
            default:
                throw new FormatException($"Версия формата проекта {number} не поддерживается.");
        }

        if (lightData is null)
            throw new FormatException("Некорректный список светильников.");

        var lights = new Luminaire[lightData.Length];
        for (var i = 0; i < lights.Length; i++)
            lights[i] = ReadLight(room, lightData[i], i + 1, number == 1);
        return new LightingProject(room, lights) { Elements = Array.AsReadOnly(elements) };
    }

    private static InteriorElement ReadElement(ElementData? data, bool readPartitionOpenings) => data switch
    {
        { Type: "opening", Height: double height, Wall: int wall, Offset: double offset,
          Width: double width, Bottom: double bottom } => new WallOpening(wall, offset, width, bottom, height),
        { Type: "partition", Height: double height, Start: { X: double x1, Y: double y1 },
          End: { X: double x2, Y: double y2 } } => new Partition(new(x1, y1), new(x2, y2), height)
          {
              Openings = readPartitionOpenings ? ReadOpenings(data.Openings) : []
          },
        { Type: "obstacle", Height: double height, Start: { X: double x1, Y: double y1 },
          End: { X: double x2, Y: double y2 } } => new BoxObstacle(new(x1, y1), new(x2, y2), height),
        _ => throw new FormatException("Некорректное описание элемента помещения.")
    };

    private static ImmutableArray<OpeningCut> ReadOpenings(OpeningData?[]? openings)
    {
        if (openings is null)
            throw new FormatException("В файле отсутствует список проёмов перегородки.");
        return [.. openings.Select(opening => opening is
            { Offset: double offset, Width: double width, Bottom: double bottom, Height: double height }
            ? new OpeningCut(offset, width, bottom, height)
            : throw new FormatException("Некорректные параметры проёма в перегородке."))];
    }

    private static ElementData WriteElement(InteriorElement element) => element switch
    {
        WallOpening opening => new("opening", opening.Height, opening.Wall, opening.Offset, opening.Width, opening.Bottom),
        Partition partition => new("partition", partition.Height,
            Start: new(partition.Start.X, partition.Start.Y), End: new(partition.End.X, partition.End.Y),
            Openings: partition.Openings.Select(cut => new OpeningData(cut.Offset, cut.Width, cut.Bottom, cut.Height)).ToArray()),
        BoxObstacle obstacle => new("obstacle", obstacle.Height,
            Start: new(obstacle.First.X, obstacle.First.Y), End: new(obstacle.Opposite.X, obstacle.Opposite.Y)),
        _ => throw new ArgumentException("Неизвестный тип элемента помещения.", nameof(element))
    };

    private static Luminaire ReadLight(Room room, LightData? data, int number, bool legacy)
    {
        if (data?.X is not float x || data.Y is not float y ||
            !(legacy
                ? x >= 0.25f && x <= (float)room.Width - 0.25f && y >= 0.25f && y <= (float)room.Depth - 0.25f
                : room.Contains(new Vector2(x, y))))
            throw new FormatException($"Некорректные координаты светильника {number}.");

        if (data.Vertical is not { Length: >= 2 } vertical || data.Horizontal is not { Length: >= 1 } horizontal ||
            !AnglesIncrease(vertical) || !AnglesIncrease(horizontal) || vertical[0] < 0 || vertical[^1] > 180 ||
            horizontal[0] != 0 || horizontal[^1] is not (0 or 90 or 180 or 360) ||
            data.Candela is not { } rows || rows.Length != horizontal.Length)
            throw new FormatException($"Некорректная фотометрия светильника {number}.");

        var candela = new double[rows.Length][];
        for (var i = 0; i < rows.Length; i++)
        {
            if (rows[i] is not { } row || row.Length != vertical.Length ||
                row.Any(value => !double.IsFinite(value) || value < 0))
                throw new FormatException($"Некорректная таблица силы света светильника {number}.");
            candela[i] = row;
        }

        return new Luminaire(new Vector2(x, y), new Photometry(vertical, horizontal, candela));
    }

    private static bool AnglesIncrease(double[] angles) =>
        angles.All(double.IsFinite) && angles.Zip(angles.Skip(1)).All(pair => pair.First < pair.Second);

    public static Task WriteAsync(Stream stream, LightingProject project)
    {
        var lights = project.Lights.Select(light => new LightData(light.Position.X, light.Position.Y,
            light.Distribution.Vertical, light.Distribution.Horizontal, light.Distribution.Candela)).ToArray();
        var vertices = project.Room.Vertices.Select(p => new VertexData(p.X, p.Y)).ToArray();
        return JsonSerializer.SerializeAsync(stream,
            new ProjectData(Version, project.Room.Height, vertices, lights, project.Elements.Select(WriteElement).ToArray()), JsonOptions);
    }

    public async Task SaveAsync(string path, LightingProject project)
    {
        path = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".lighting-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, useAsync: true))
            {
                created = true;
                await WriteAsync(stream, project).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
