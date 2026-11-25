using System.Numerics;

namespace Lighting;

internal sealed class EditorScene(Room room, IEnumerable<Luminaire> lights)
{
    private readonly List<Luminaire> _lights = lights.ToList();

    public Room Room { get; private set; } = room;
    public IReadOnlyList<Luminaire> Lights => _lights;
    public double[,]? Result { get; private set; }
    public int Revision { get; private set; }

    public bool TryApplyOutline(IEnumerable<RoomPoint> vertices, double height, out string? error)
    {
        Room candidate;
        try
        {
            candidate = new Room(vertices, height);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        if (_lights.Any(light => !candidate.Contains(light.Position)))
        {
            error = "Новый контур оставляет светильник за пределами помещения.";
            return false;
        }

        if (candidate.Height != Room.Height || !candidate.Vertices.SequenceEqual(Room.Vertices))
        {
            Room = candidate;
            InvalidateResult();
        }

        error = null;
        return true;
    }

    public bool TryAddLight(Photometry distribution)
    {
        var point = Room.InteriorPoint();
        var position = new Vector2((float)point.X, (float)point.Y);
        if (!Room.Contains(position))
            return false;

        _lights.Add(new Luminaire(position, distribution));
        InvalidateResult();
        return true;
    }

    public bool TryMoveLight(int index, Vector2 position)
    {
        var snapped = new Vector2(MathF.Round(position.X * 4) / 4, MathF.Round(position.Y * 4) / 4);
        if (!Room.Contains(snapped) || snapped == _lights[index].Position)
            return false;

        _lights[index] = _lights[index] with { Position = snapped };
        InvalidateResult();
        return true;
    }

    public void RemoveLight(int index)
    {
        _lights.RemoveAt(index);
        InvalidateResult();
    }

    public void Replace(Room replacement, IEnumerable<Luminaire> lights)
    {
        var snapshot = lights.ToArray();
        Room = replacement;
        _lights.Clear();
        _lights.AddRange(snapshot);
        InvalidateResult();
    }

    public bool TryPublishResult(int revision, double[,] result)
    {
        if (revision != Revision)
            return false;

        Result = result;
        return true;
    }

    private void InvalidateResult()
    {
        Revision++;
        Result = null;
    }
}
