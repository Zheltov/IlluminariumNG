using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Desktop;

internal sealed partial class SceneView
{
    private List<RoomPoint>? _outline;
    private bool _drawing;
    private int _vertex = -1;
    private RoomPoint _originalVertex;

    public bool HasDraft => (_drawing && _outline is not null) || _vertex >= 0 || _interiorTool is not null;

    public void BeginOutline(bool drawing)
    {
        CancelInterior();
        _drawing = drawing;
        _outline = drawing ? [] : editor.Room.Vertices.ToList();
        editor.RefreshUndo();
        InvalidateVisual();
    }

    public void CancelOutline()
    {
        ClearSnapPreview();
        _outline = null;
        _vertex = -1;
        editor.RefreshUndo();
        InvalidateVisual();
    }

    public void AcceptOutline()
    {
        if (_outline is { } points && editor.ApplyOutline(points))
            CancelOutline();
    }

    private RoomPoint OutlinePoint(Point point) =>
        new(MidX + (point.X - Center.X) / Scale, MidY + (point.Y - Center.Y) / Scale);

    private bool HandleOutlinePress(PointerPressedEventArgs e, Point point)
    {
        if (_outline is not { } vertices)
            return false;

        var index = vertices.FindIndex(p => WithinMarker(Project(p.X, p.Y), point));
        var properties = e.GetCurrentPoint(this).Properties;
        if (_drawing && properties.IsLeftButtonPressed)
        {
            if (index == 0 && vertices.Count >= 3)
                AcceptOutline();
            else
                vertices.Add(SnapPoint(OutlinePoint(point)));
        }
        else if (!_drawing && index >= 0 && properties.IsRightButtonPressed)
        {
            var candidate = vertices.ToList();
            candidate.RemoveAt(index);
            UpdateOutline(candidate);
        }
        else if (!_drawing && index >= 0 && properties.IsLeftButtonPressed)
        {
            _originalVertex = vertices[index];
            _vertex = index;
            editor.RefreshUndo();
            _pointer = e.Pointer;
            e.Pointer.Capture(this);
        }
        else if (!_drawing && e.ClickCount == 2 && properties.IsLeftButtonPressed)
        {
            var best = 10d;
            var edge = -1;
            var insertion = point;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = Project(vertices[i].X, vertices[i].Y);
                var next = vertices[(i + 1) % vertices.Count];
                var direction = Project(next.X, next.Y) - a;
                var delta = point - a;
                var fraction = Math.Clamp((delta.X * direction.X + delta.Y * direction.Y) /
                    (direction.X * direction.X + direction.Y * direction.Y), 0, 1);
                var projected = a + direction * fraction;
                var dx = projected.X - point.X;
                var dy = projected.Y - point.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < best)
                {
                    best = distance;
                    edge = i;
                    insertion = projected;
                }
            }

            if (edge >= 0)
            {
                var candidate = vertices.ToList();
                var host = editor.Room.Wall(edge);
                var axisPoint = OutlinePoint(insertion);
                var offset = double.Hypot(axisPoint.X - host.Start.X, axisPoint.Y - host.Start.Y);
                candidate.Insert(edge + 1, host.At(SnapOffset(host, offset, host.Length)));
                UpdateOutline(candidate);
            }
        }

        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool MoveOutlineVertex(Point point)
    {
        if (_vertex < 0 || _outline is null)
            return false;
        _outline[_vertex] = SnapPoint(OutlinePoint(point), _originalVertex);
        if (SnappingEnabled)
            PreviewSnap(_outline[_vertex]);
        InvalidateVisual();
        return true;
    }

    private void CommitOutlineVertex()
    {
        if (_vertex >= 0 && _outline is { } candidate)
        {
            _vertex = -1;
            UpdateOutline(candidate);
        }
    }

    private void UpdateOutline(List<RoomPoint> candidate)
    {
        editor.ApplyOutline(candidate);
        _outline = editor.Room.Vertices.ToList();
        editor.RefreshUndo();
        InvalidateVisual();
    }

    private void CancelVertexDrag()
    {
        if (_vertex < 0)
            return;
        _vertex = -1;
        _outline = editor.Room.Vertices.ToList();
        editor.RefreshUndo();
        InvalidateVisual();
    }

    private void DrawOutlineEditor(DrawingContext context)
    {
        if (_outline is not { } vertices)
            return;

        var pen = new Pen(Accent, 2);
        for (var i = 0; i < vertices.Count; i++)
        {
            var point = Project(vertices[i].X, vertices[i].Y);
            if (i > 0 || !_drawing)
            {
                var previous = vertices[(i + vertices.Count - 1) % vertices.Count];
                context.DrawLine(pen, Project(previous.X, previous.Y), point);
            }
            context.DrawEllipse(Panel, pen, point, 5, 5);
        }
    }
}
