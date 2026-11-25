using System.Collections.Immutable;

namespace IlluminariumNG.Domain;

internal abstract record InteriorElement(double Height);
internal sealed record WallOpening(int Wall, double Offset, double Width, double Bottom, double Height)
    : InteriorElement(Height)
{
    public OpeningCut Cut => new(Offset, Width, Bottom, Height);
}

internal sealed record Partition(RoomPoint Start, RoomPoint End, double Height) : InteriorElement(Height)
{
    public WallSpan Span => new(Start, End);
    public ImmutableArray<OpeningCut> Openings { get; init; } = [];
}

internal readonly record struct OpeningCut(double Offset, double Width, double Bottom, double Height)
{
    public WallPanel Bounds => new(Offset, Offset + Width, Bottom, Bottom + Height);
    public WallSpan Along(WallSpan host) => new(host.At(Offset), host.At(Offset + Width));
}
internal sealed record BoxObstacle(RoomPoint First, RoomPoint Opposite, double Height) : InteriorElement(Height)
{
    public RoomPoint[] Footprint()
    {
        var left = Math.Min(First.X, Opposite.X);
        var right = Math.Max(First.X, Opposite.X);
        var top = Math.Min(First.Y, Opposite.Y);
        var bottom = Math.Max(First.Y, Opposite.Y);
        return [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];
    }
}

internal readonly record struct WallSpan(RoomPoint Start, RoomPoint End)
{
    public double Length => double.Hypot(End.X - Start.X, End.Y - Start.Y);

    public RoomPoint At(double offset)
    {
        var fraction = offset / Length;
        return new(Start.X + (End.X - Start.X) * fraction, Start.Y + (End.Y - Start.Y) * fraction);
    }
}

internal readonly record struct WallPanel(double Left, double Right, double Bottom, double Top)
{
    public double Area => (Right - Left) * (Top - Bottom);

    public bool Overlaps(WallPanel other) =>
        Left < other.Right && other.Left < Right && Bottom < other.Top && other.Bottom < Top;

    public IEnumerable<WallPanel> Subtract(WallPanel opening)
    {
        if (!Overlaps(opening))
        {
            yield return this;
            yield break;
        }

        var left = Math.Max(Left, opening.Left);
        var right = Math.Min(Right, opening.Right);
        if (Left < left)
            yield return this with { Right = left };
        if (right < Right)
            yield return this with { Left = right };
        if (Bottom < opening.Bottom)
            yield return new(left, right, Bottom, opening.Bottom);
        if (opening.Top < Top)
            yield return new(left, right, opening.Top, Top);
    }
}

internal static class InteriorGeometry
{
    public static IEnumerable<WallPanel> WallPanels(Room room, int wall, IEnumerable<InteriorElement> elements) =>
        SurfacePanels(room.Wall(wall).Length, room.Height,
            elements.OfType<WallOpening>().Where(opening => opening.Wall == wall).Select(opening => opening.Cut));

    public static IEnumerable<WallPanel> SurfacePanels(double length, double height, IEnumerable<OpeningCut> openings)
    {
        WallPanel[] panels = [new(0, length, 0, height)];
        foreach (var opening in openings)
            panels = panels.SelectMany(panel => panel.Subtract(opening.Bounds)).ToArray();
        return panels;
    }

    private static bool Fits(WallPanel bounds, double length, double height) =>
        bounds.Left >= 0 && bounds.Right > bounds.Left && bounds.Right <= length &&
        bounds.Bottom >= 0 && bounds.Top > bounds.Bottom && bounds.Top <= height;

    public static string? Validate(Room room, IReadOnlyList<InteriorElement> elements)
    {
        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            if (!double.IsFinite(element.Height) || element.Height <= 0 || element.Height > room.Height)
                return "Высота элемента должна быть больше нуля и не выше потолка.";

            switch (element)
            {
                case WallOpening opening:
                    var bounds = opening.Cut.Bounds;
                    if (opening.Wall < 0 || opening.Wall >= room.Vertices.Count ||
                        !Fits(bounds, room.Wall(opening.Wall).Length, room.Height))
                        return "Проём должен целиком помещаться в выбранной стене.";

                    for (var previous = 0; previous < index; previous++)
                        if (elements[previous] is WallOpening other && other.Wall == opening.Wall && bounds.Overlaps(other.Cut.Bounds))
                            return "Проёмы на одной стене пересекаются.";
                    break;
                case Partition partition:
                    if (!room.ContainsSegment(partition.Start, partition.End, requireInterior: true))
                        return "Перегородка должна проходить внутри помещения.";
                    for (var i = 0; i < partition.Openings.Length; i++)
                    {
                        var cut = partition.Openings[i].Bounds;
                        if (!Fits(cut, partition.Span.Length, partition.Height))
                            return "Проём должен целиком помещаться в перегородке.";
                        for (var j = 0; j < i; j++)
                            if (cut.Overlaps(partition.Openings[j].Bounds))
                                return "Проёмы в одной перегородке пересекаются.";
                    }
                    break;
                case BoxObstacle obstacle:
                    var corners = obstacle.Footprint();
                    if (!(corners[0].X < corners[2].X && corners[0].Y < corners[2].Y) ||
                        !Enumerable.Range(0, corners.Length).All(i =>
                            room.ContainsSegment(corners[i], corners[(i + 1) % corners.Length])))
                        return "Прямоугольное основание препятствия должно целиком помещаться в помещении.";
                    break;
                default:
                    return "Неизвестный тип элемента помещения.";
            }
        }
        return null;
    }
}
