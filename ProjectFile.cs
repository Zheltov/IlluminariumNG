using System.Numerics;
using System.Text.Json;

namespace Lighting;

internal sealed record LightingProject(Room Room, Luminaire[] Lights);

internal static class ProjectFile
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed record ProjectData(int FormatVersion, double Width, double Depth, LightData?[]? Lights);
    private sealed record LightData(float? X, float? Y, double[]? Vertical, double[]? Horizontal, double[]?[]? Candela);

    public static async Task<LightingProject> ReadAsync(Stream stream)
    {
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var number))
            throw new FormatException("В файле отсутствует корректная версия формата проекта.");
        if (number != Version)
            throw new FormatException($"Версия формата проекта {number} не поддерживается.");

        var data = root.Deserialize<ProjectData>(JsonOptions)!;
        if (!(data.Width is >= 2 and <= 30 && data.Depth is >= 2 and <= 30) || data.Lights is null)
            throw new FormatException("Некорректные размеры помещения или список светильников.");

        var room = new Room(data.Width, data.Depth);
        var lights = new Luminaire[data.Lights.Length];
        for (var i = 0; i < lights.Length; i++)
            lights[i] = ReadLight(room, data.Lights[i], i + 1);
        return new LightingProject(room, lights);
    }

    private static Luminaire ReadLight(Room room, LightData? data, int number)
    {
        if (data?.X is not float x || data.Y is not float y ||
            !(x >= 0.25f && x <= (float)room.Width - 0.25f && y >= 0.25f && y <= (float)room.Depth - 0.25f))
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
        return JsonSerializer.SerializeAsync(stream,
            new ProjectData(Version, project.Room.Width, project.Room.Depth, lights), JsonOptions);
    }

    public static async Task SaveAsync(string path, LightingProject project)
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
