using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Lighting;

internal sealed partial class SceneView
{
    private List<RoomPoint>? _outline;
    private bool _drawing;
    private int _vertex = -1;

    public void BeginOutline(bool drawing)
    {
        CancelInterior();
        _drawing = drawing;
        _outline = drawing ? [] : editor.Room.Vertices.ToList();
        InvalidateVisual();
    }

    public void CancelOutline()
    {
        _outline = null;
        _vertex = -1;
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
                vertices.Add(OutlinePoint(point));
        }
        else if (!_drawing && index >= 0 && properties.IsRightButtonPressed)
        {
            var candidate = vertices.ToList();
            candidate.RemoveAt(index);
            UpdateOutline(candidate);
        }
        else if (!_drawing && index >= 0 && properties.IsLeftButtonPressed)
        {
            _vertex = index;
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
                candidate.Insert(edge + 1, OutlinePoint(insertion));
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
        _outline[_vertex] = OutlinePoint(point);
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
        InvalidateVisual();
    }

    private void CancelVertexDrag()
    {
        if (_vertex < 0)
            return;
        _vertex = -1;
        _outline = editor.Room.Vertices.ToList();
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
