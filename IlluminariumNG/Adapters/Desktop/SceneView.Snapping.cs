using Avalonia;
using Avalonia.Media;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Desktop;

internal sealed partial class SceneView
{
    private const double SnapRadiusPixels = 10;
    private RoomPoint? _snapPreview;
    private double SnapRadius => SnapRadiusPixels / Scale;
    private bool SnappingEnabled => editor.SnapGrid || editor.SnapGeometry;

    private IEnumerable<RoomPoint> SnapCandidates()
    {
        for (var index = 0; index < editor.Room.Vertices.Count; index++)
            if (index != _vertex)
                yield return editor.Room.Vertices[index];

        foreach (var element in editor.InteriorElements)
        {
            switch (element)
            {
                case WallOpening opening:
                    var (left, right) = opening.Cut.Along(editor.Room.Wall(opening.Wall));
                    yield return left;
                    yield return right;
                    break;
                case Partition partition:
                    yield return partition.Start;
                    yield return partition.End;
                    foreach (var cut in partition.Openings)
                    {
                        var (start, end) = cut.Along(partition.Span);
                        yield return start;
                        yield return end;
                    }
                    break;
                case BoxObstacle box:
                    foreach (var corner in box.Footprint())
                        yield return corner;
                    break;
            }
        }
    }

    private RoomPoint SnapPoint(RoomPoint point, RoomPoint? excluded = null) =>
        PlanSnap.Point(point, editor.SnapGrid, editor.SnapGeometry, SnapCandidates(), SnapRadius, excluded);

    private double SnapOffset(WallSpan host, double offset, double maxOffset) =>
        PlanSnap.Along(host, offset, maxOffset, editor.SnapGrid, editor.SnapGeometry, SnapCandidates(), SnapRadius);

    public void ClearSnapPreview() => PreviewSnap(null);

    private void PreviewSnap(RoomPoint? point)
    {
        if (_snapPreview == point)
            return;

        _snapPreview = point;
        InvalidateVisual();
    }

    private void UpdateSnapPreview(Point pointer)
    {
        if (!SnappingEnabled)
        {
            ClearSnapPreview();
            return;
        }

        if (_interiorTool == InteriorTool.Opening)
        {
            var (host, distance, fraction) = OpeningHostAt(pointer);
            var width = Math.Min(host.Span.Length, 0.9);
            PreviewSnap(distance <= 12
                ? host.Span.At(SnapOffset(host.Span, fraction * host.Span.Length, host.Span.Length - width))
                : null);
        }
        else if ((_outline is not null && _drawing) || _interiorTool is not null)
            PreviewSnap(SnapPoint(OutlinePoint(pointer)));
        else
            ClearSnapPreview();
    }

    private void DrawSnapPreview(DrawingContext context)
    {
        if (_snapPreview is not { } point || !SnappingEnabled)
            return;

        var center = At(point);
        context.DrawRectangle(null, new Pen(OpeningMark, 2),
            new Rect(center.X - 6, center.Y - 6, 12, 12));
    }
}
