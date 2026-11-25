using System.Numerics;

namespace IlluminariumNG.Domain;

internal readonly record struct RoomPoint(double X, double Y);

internal sealed class Room
{
    public const double MountHeight = 2.8;
    public const double WorkHeight = 0.8;
    private const double DistanceTolerance = 1e-9;

    public Room(double width, double depth)
        : this([new(0, 0), new(width, 0), new(width, depth), new(0, depth)], MountHeight)
    {
    }

    public Room(IEnumerable<RoomPoint> vertices, double height)
    {
        var points = vertices.ToArray();
        if (!double.IsFinite(height) || height <= WorkHeight)
            throw new ArgumentException("Высота должна быть больше 0,8 м.");
        if (points.Length < 3 || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
            throw new ArgumentException("Укажите хотя бы три вершины с конечными координатами.");

        MinX = points.Min(p => p.X);
        MinY = points.Min(p => p.Y);
        Width = points.Max(p => p.X) - MinX;
        Depth = points.Max(p => p.Y) - MinY;
        if (!double.IsFinite(Width) || !double.IsFinite(Depth))
            throw new ArgumentException("Размеры контура слишком велики.");

        double doubleArea = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            var previous = points[(i + points.Length - 1) % points.Length];
            doubleArea += Cross(points[0], a, b);
            for (var j = i + 1; j < points.Length; j++)
            {
                var c = points[j];
                if (Math.Abs(a.X - c.X) <= DistanceTolerance && Math.Abs(a.Y - c.Y) <= DistanceTolerance)
                    throw new ArgumentException("В контуре есть совпадающие вершины.");
                if (j == i + 1 || (i == 0 && j == points.Length - 1))
                    continue;
                if (Intersects(a, b, c, points[(j + 1) % points.Length]))
                    throw new ArgumentException("Стены контура пересекаются или касаются.");
            }

            if (OnSegment(previous, a, b) || OnSegment(a, b, previous))
                throw new ArgumentException("Смежные стены накладываются друг на друга.");
        }

        if (!double.IsFinite(doubleArea) || Math.Abs(doubleArea) <= DistanceTolerance * Math.Max(Width, Depth))
            throw new ArgumentException("Площадь контура должна быть больше нуля.");

        Vertices = Array.AsReadOnly(points);
        Height = height;
        Winding = Math.Sign(doubleArea);
        Area = Math.Abs(doubleArea) / 2;
    }

    public IReadOnlyList<RoomPoint> Vertices { get; }
    public double MinX { get; }
    public double MinY { get; }
    public double Width { get; }
    public double Depth { get; }
    public double Height { get; }
    public int Winding { get; }
    public double Area { get; }

    public bool Contains(Vector2 point) => Contains(new RoomPoint(point.X, point.Y));

    public bool Contains(RoomPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            return false;

        var inside = false;
        for (var i = 0; i < Vertices.Count; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % Vertices.Count];
            if (OnSegment(a, b, point))
                return true;
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < a.X + (b.X - a.X) * ((point.Y - a.Y) / (b.Y - a.Y)))
                inside = !inside;
        }

        return inside;
    }

    public WallSpan Wall(int index) => new(Vertices[index], Vertices[(index + 1) % Vertices.Count]);

    public bool ContainsSegment(RoomPoint start, RoomPoint end, bool requireInterior = false)
    {
        if (start == end || !Contains(start) || !Contains(end))
            return false;

        // Between successive intersections with the boundary, the segment has constant membership.
        var vertical = Math.Abs(end.Y - start.Y) > Math.Abs(end.X - start.X);
        double Parameter(RoomPoint point) => vertical
            ? (point.Y - start.Y) / (end.Y - start.Y)
            : (point.X - start.X) / (end.X - start.X);

        var parameters = new List<double> { 0, 1 };
        for (var i = 0; i < Vertices.Count; i++)
        {
            var (a, b) = Wall(i);
            if (OnSegment(start, end, a))
                parameters.Add(Math.Clamp(Parameter(a), 0, 1));

            if (Side(start, end, a) * Side(start, end, b) < 0 &&
                Side(a, b, start) * Side(a, b, end) < 0)
            {
                var from = Cross(a, b, start);
                var to = Cross(a, b, end);
                parameters.Add(from / (from - to));
            }
        }

        parameters.Sort();
        var hasInterior = false;
        for (var i = 1; i < parameters.Count; i++)
        {
            if (parameters[i] == parameters[i - 1])
                continue;
            var t = (parameters[i - 1] + parameters[i]) / 2;
            var point = new RoomPoint(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            if (!Contains(point))
                return false;
            hasInterior |= !Enumerable.Range(0, Vertices.Count).Any(j =>
                OnSegment(Vertices[j], Vertices[(j + 1) % Vertices.Count], point));
        }

        return !requireInterior || hasInterior;
    }

    public RoomPoint InteriorPoint()
    {
        var center = new RoomPoint(MinX + Width / 2, MinY + Depth / 2);
        if (Contains(center))
            return center;

        var levels = Vertices.Select(p => p.Y).Distinct().Order().ToArray();
        var best = center;
        var widest = 0d;
        for (var level = 1; level < levels.Length; level++)
        {
            var y = levels[level - 1] + (levels[level] - levels[level - 1]) / 2;
            var crossings = new List<double>();
            for (var i = 0; i < Vertices.Count; i++)
            {
                var a = Vertices[i];
                var b = Vertices[(i + 1) % Vertices.Count];
                if ((a.Y > y) != (b.Y > y))
                    crossings.Add(a.X + (b.X - a.X) * ((y - a.Y) / (b.Y - a.Y)));
            }

            crossings.Sort();
            for (var i = 1; i < crossings.Count; i += 2)
            {
                var width = crossings[i] - crossings[i - 1];
                if (width > widest)
                {
                    widest = width;
                    best = new RoomPoint(crossings[i - 1] + width / 2, y);
                }
            }
        }

        return best;
    }

    private static double Cross(RoomPoint a, RoomPoint b, RoomPoint p) =>
        (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static int Side(RoomPoint a, RoomPoint b, RoomPoint p)
    {
        var cross = Cross(a, b, p);
        var tolerance = DistanceTolerance * Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        return cross > tolerance ? 1 : cross < -tolerance ? -1 : 0;
    }

    private static bool OnSegment(RoomPoint a, RoomPoint b, RoomPoint p) =>
        p.X >= Math.Min(a.X, b.X) - DistanceTolerance && p.X <= Math.Max(a.X, b.X) + DistanceTolerance &&
        p.Y >= Math.Min(a.Y, b.Y) - DistanceTolerance && p.Y <= Math.Max(a.Y, b.Y) + DistanceTolerance &&
        Side(a, b, p) == 0;

    private static bool Intersects(RoomPoint a, RoomPoint b, RoomPoint c, RoomPoint d) =>
        OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b) ||
        (Side(a, b, c) * Side(a, b, d) < 0 && Side(c, d, a) * Side(c, d, b) < 0);
}
