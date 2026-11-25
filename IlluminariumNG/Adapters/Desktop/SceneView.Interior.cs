using Avalonia;
using Avalonia.Media;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Desktop;

internal sealed partial class SceneView
{
    private static readonly IBrush ObjectFill = Brush.Parse("#486274");
    private static readonly IBrush ObjectTop = Brush.Parse("#688393");
    private static readonly IBrush OpeningMark = Brush.Parse("#EDC375");
    private InteriorTool? _interiorTool;
    private RoomPoint? _firstInteriorPoint;

    public void BeginInterior(InteriorTool tool)
    {
        CancelOutline();
        _interiorTool = tool;
        _firstInteriorPoint = null;
        editor.RefreshUndo();
        InvalidateVisual();
    }

    public void CancelInterior()
    {
        ClearSnapPreview();
        _interiorTool = null;
        _firstInteriorPoint = null;
        editor.RefreshUndo();
        InvalidateVisual();
    }

    private async Task PlaceInterior(Point position)
    {
        var room = editor.Room;
        if (_interiorTool == InteriorTool.Opening)
        {
            var (host, distance, fraction) = OpeningHostAt(position);

            if (distance > 12)
            {
                editor.ShowInteriorHint("Щёлкните ближе к стене или перегородке.");
                return;
            }

            var width = Math.Min(host.Span.Length, 0.9);
            var cut = new OpeningCut(SnapOffset(host.Span, fraction * host.Span.Length, host.Span.Length - width),
                width, 0, Math.Min(2, host.Height));
            CancelInterior();
            if (host.IsPartition)
                await editor.EditPartitionOpening(host.Index, cut);
            else
                await editor.EditInterior(new WallOpening(host.Index, cut.Offset, cut.Width, cut.Bottom, cut.Height));
        }
        else if (_firstInteriorPoint is not { } start)
        {
            _firstInteriorPoint = SnapPoint(OutlinePoint(position));
            InvalidateVisual();
            editor.ShowInteriorHint("Поставьте вторую точку на плане.");
        }
        else
        {
            var end = SnapPoint(OutlinePoint(position));
            InteriorElement element = _interiorTool == InteriorTool.Partition
                ? new Partition(start, end, room.Height)
                : new BoxObstacle(start, end, Math.Min(room.Height, 1));
            CancelInterior();
            await editor.EditInterior(element);
        }
    }

    private ((int Index, bool IsPartition, WallSpan Span, double Height) Host, double Distance, double Fraction)
        OpeningHostAt(Point position)
    {
        var room = editor.Room;
        var closest = Enumerable.Range(0, room.Vertices.Count)
            .Select(i => (Index: i, IsPartition: false, Span: room.Wall(i), Height: room.Height))
            .Concat(editor.InteriorElements.Select((element, i) => (element, i))
                .Where(item => item.element is Partition)
                .Select(item =>
                {
                    var partition = (Partition)item.element;
                    return (Index: item.i, IsPartition: true, Span: partition.Span, Height: partition.Height);
                }))
            .Select(host => (Host: host, Hit: SegmentHit(At(host.Span.Start), At(host.Span.End), position)))
            .MinBy(item => item.Hit.Distance);

        return (closest.Host, closest.Hit.Distance, closest.Hit.Fraction);
    }

    private Point At(RoomPoint point, double height = 0) => Project(point.X, point.Y, height);

    private static (double Distance, double Fraction) SegmentHit(Point a, Point b, Point p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        var fraction = lengthSquared == 0 ? 0 :
            Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return (double.Hypot(p.X - (a.X + dx * fraction), p.Y - (a.Y + dy * fraction)), fraction);
    }

    private InteriorSelection? InteriorHit(Point point)
    {
        for (var index = editor.InteriorElements.Count - 1; index >= 0; index--)
        {
            var element = editor.InteriorElements[index];
            if (element is BoxObstacle box)
            {
                var corners = box.Footprint();
                if (new Rect(At(corners[0]), At(corners[2])).Contains(point))
                    return new(index);
                continue;
            }

            if (element is Partition partition)
            {
                for (var opening = partition.Openings.Length - 1; opening >= 0; opening--)
                {
                    var cut = partition.Openings[opening].Along(partition.Span);
                    if (SegmentHit(At(cut.Start), At(cut.End), point).Distance <= 8)
                        return new(index, opening);
                }
            }

            var (start, end) = FootprintLine(element);
            if (SegmentHit(At(start), At(end), point).Distance <= 10)
                return new(index);
        }
        return null;
    }

    private WallSpan FootprintLine(InteriorElement element)
    {
        if (element is Partition partition)
            return new(partition.Start, partition.End);
        var opening = (WallOpening)element;
        return opening.Cut.Along(editor.Room.Wall(opening.Wall));
    }

    private void DrawInterior(DrawingContext context)
    {
        if (orbitView)
        {
            foreach (var (element, index) in editor.InteriorElements
                .Select((element, index) => (element, index))
                .Where(item => item.element is not WallOpening)
                .OrderBy(item => InteriorDepth(item.element)))
                DrawInteriorVolume(context, element, index);
            return;
        }

        for (var index = 0; index < editor.InteriorElements.Count; index++)
        {
            var element = editor.InteriorElements[index];
            var pen = new Pen(editor.SelectedInterior == new InteriorSelection(index) ? Accent : element is WallOpening ? OpeningMark : White, 3);
            if (element is BoxObstacle box)
                Polygon(context, ObjectFill, pen, box.Footprint().Select(p => At(p)).ToArray());
            else
            {
                var (start, end) = FootprintLine(element);
                context.DrawLine(pen, At(start), At(end));
                if (element is Partition partition)
                {
                    for (var opening = 0; opening < partition.Openings.Length; opening++)
                    {
                        var cut = partition.Openings[opening].Along(partition.Span);
                        var selected = editor.SelectedInterior == new InteriorSelection(index, opening);
                        context.DrawLine(new Pen(selected ? Accent : OpeningMark, 4), At(cut.Start), At(cut.End));
                    }
                }
            }
        }

        if (_firstInteriorPoint is { } first)
            context.DrawEllipse(Accent, null, At(first), 5, 5);
    }

    private double InteriorDepth(InteriorElement element)
    {
        var (start, end) = element is BoxObstacle box ? new WallSpan(box.First, box.Opposite) : FootprintLine(element);
        return (Depth(start.X, start.Y) + Depth(end.X, end.Y)) / 2;
    }

    private void DrawInteriorVolume(DrawingContext context, InteriorElement element, int index)
    {
        var pen = editor.SelectedInterior == new InteriorSelection(index) ? new Pen(Accent, 2) : Edge;
        if (element is Partition partition)
        {
            if (partition.Openings.IsEmpty)
                DrawVerticalFace(context, partition.Start, partition.End, 0, partition.Height, ObjectFill, pen);
            else
                DrawCutSurface(context, partition.Span, partition.Height, ObjectFill,
                    partition.Openings.Select((cut, opening) =>
                        (cut, editor.SelectedInterior == new InteriorSelection(index, opening) ? new Pen(Accent, 2) : pen)));
            return;
        }

        var box = (BoxObstacle)element;
        var corners = box.Footprint();
        for (var i = 0; i < corners.Length; i++)
        {
            var start = corners[i];
            var end = corners[(i + 1) % corners.Length];
            if (Across(start, end) < 0)
                DrawVerticalFace(context, start, end, 0, box.Height, ObjectFill, pen);
        }

        Polygon(context, ObjectTop, pen, corners.Select(p => At(p, box.Height)).ToArray());
    }

    private double Across(RoomPoint start, RoomPoint end) =>
        Math.Cos(_angle) * (end.X - start.X) - Math.Sin(_angle) * (end.Y - start.Y);

    private void DrawVerticalFace(DrawingContext context, RoomPoint start, RoomPoint end,
        double bottom, double top, IBrush? brush, Pen? pen) =>
        Polygon(context, brush, pen, At(start, bottom), At(end, bottom), At(end, top), At(start, top));

    private void DrawRoomWall(DrawingContext context, int wallIndex)
    {
        var room = editor.Room;
        var wall = room.Wall(wallIndex);
        if (Across(wall.Start, wall.End) * room.Winding <= 0)
            return;

        var openings = editor.InteriorElements.Select((element, index) => (element, index))
            .Where(item => item.element is WallOpening opening && opening.Wall == wallIndex)
            .Select(item => (((WallOpening)item.element).Cut,
                editor.SelectedInterior == new InteriorSelection(item.index) ? new Pen(Accent, 2) : Edge))
            .ToArray();
        if (openings.Length == 0)
            DrawVerticalFace(context, wall.Start, wall.End, 0, room.Height, Wall, Edge);
        else
            DrawCutSurface(context, wall, room.Height, Wall, openings);
    }

    private void DrawCutSurface(DrawingContext context, WallSpan span, double height, IBrush fill,
        IEnumerable<(OpeningCut Cut, Pen Pen)> openings)
    {
        var cuts = openings.ToArray();
        foreach (var panel in InteriorGeometry.SurfacePanels(span.Length, height, cuts.Select(item => item.Cut)))
            DrawVerticalFace(context, span.At(panel.Left), span.At(panel.Right), panel.Bottom, panel.Top, fill, null);

        foreach (var (opening, pen) in cuts)
        {
            var (start, end) = opening.Along(span);
            var top = opening.Bottom + opening.Height;
            context.DrawLine(pen, At(start, opening.Bottom), At(start, top));
            context.DrawLine(pen, At(end, opening.Bottom), At(end, top));
            context.DrawLine(pen, At(start, top), At(end, top));
            if (opening.Bottom > 0)
                context.DrawLine(pen, At(start, opening.Bottom), At(end, opening.Bottom));
        }
    }
}
