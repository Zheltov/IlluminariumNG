using Vector2 = System.Numerics.Vector2;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lighting;

internal sealed class SceneView(EditorWindow editor, bool orbitView) : Control
{
    private static readonly IBrush Panel = Brush.Parse("#101925");
    private static readonly IBrush Floor = Brush.Parse("#1B2938");
    private static readonly IBrush Wall = Brush.Parse("#233646");
    private static readonly IBrush Accent = Brush.Parse("#72E5CB");
    private static readonly IBrush White = Brush.Parse("#E6F1F5");
    private static readonly Pen Edge = new(Brush.Parse("#567385"), 1);
    private static readonly Pen GridLine = new(Brush.Parse("#364B5B"), 1);
    private static readonly Pen Stem = new(Brush.Parse("#8BAABB"), 1, DashStyle.Dash);
    private static readonly IBrush[] Heat =
    [
        Brush.Parse("#303F79"), Brush.Parse("#325B91"), Brush.Parse("#317A9B"),
        Brush.Parse("#2B969B"), Brush.Parse("#41AB8C"), Brush.Parse("#80BE78"),
        Brush.Parse("#CFCC72"), Brush.Parse("#F6DC82")
    ];

    private double _angle = -0.63;
    private double _zoom = 1;
    private IPointer? _pointer;
    private Point _last;
    private Vector2 _dragOffset;
    private bool _dragLight;

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        using var clip = context.PushClip(area);
        context.DrawRectangle(Panel, null, area);
        if (Bounds.Width < 1 || Bounds.Height < 1)
            return;

        var room = editor.Room;
        var corners = new[] { (0d, 0d), (room.Width, 0d), (room.Width, room.Depth), (0d, room.Depth) };
        if (orbitView)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                var (x, y) = corners[i];
                var (nextX, nextY) = corners[(i + 1) % corners.Length];
                if (Depth((x + nextX) / 2, (y + nextY) / 2) < 0)
                    Polygon(context, Wall, Edge, Project(x, y), Project(nextX, nextY),
                        Project(nextX, nextY, Room.MountHeight), Project(x, y, Room.MountHeight));
            }
        }

        Polygon(context, Floor, Edge, corners.Select(corner => Project(corner.Item1, corner.Item2)).ToArray());
        for (var x = 1; x < room.Width; x++)
            context.DrawLine(GridLine, Project(x, 0), Project(x, room.Depth));
        for (var y = 1; y < room.Depth; y++)
            context.DrawLine(GridLine, Project(0, y), Project(room.Width, y));

        if (editor.Result is { } result)
        {
            var height = orbitView ? Room.WorkHeight : 0;
            var dx = room.Width / Illuminance.Columns;
            var dy = room.Depth / Illuminance.Rows;
            for (var y = 0; y < Illuminance.Rows; y++)
            for (var x = 0; x < Illuminance.Columns; x++)
            {
                var band = (int)Math.Clamp(result[x, y] / 50, 0, Heat.Length - 1);
                Polygon(context, Heat[band], null, Project(x * dx, y * dy, height),
                    Project((x + 1) * dx, y * dy, height), Project((x + 1) * dx, (y + 1) * dy, height),
                    Project(x * dx, (y + 1) * dy, height));
            }
        }

        foreach (var index in LightDrawOrder())
        {
            var light = editor.Lights[index];
            var center = Marker(light);
            var active = index == editor.Selected;
            if (orbitView)
                context.DrawLine(Stem, center, Project(light.Position.X, light.Position.Y, Room.WorkHeight));
            context.DrawEllipse(Panel, new Pen(active ? Accent : White, active ? 2 : 1), center, 9, 9);
            context.DrawEllipse(active ? Accent : White, null, center, 3, 3);
        }
    }

    private IEnumerable<int> LightDrawOrder() =>
        Enumerable.Range(0, editor.Lights.Count)
            .OrderBy(index => index == editor.Selected)
            .ThenBy(index => Depth(editor.Lights[index].Position.X, editor.Lights[index].Position.Y));

    private static void Polygon(DrawingContext context, IBrush? fill, Pen? pen, params Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1))
                path.LineTo(point);
            path.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private double Scale => Math.Max(1, Math.Min(
        (Bounds.Width - 46) / (orbitView ? Math.Sqrt(editor.Room.Width * editor.Room.Width +
            editor.Room.Depth * editor.Room.Depth) : editor.Room.Width),
        (Bounds.Height - 46) / (orbitView ? 0.52 * Math.Sqrt(editor.Room.Width * editor.Room.Width +
            editor.Room.Depth * editor.Room.Depth) + Room.MountHeight * 0.8 : editor.Room.Depth))) * _zoom;

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2 + (orbitView ? Scale * Room.MountHeight * 0.4 : 0));

    private double Depth(double x, double y) =>
        Math.Sin(_angle) * (x - editor.Room.Width / 2) + Math.Cos(_angle) * (y - editor.Room.Depth / 2);

    private Point Project(double x, double y, double z = 0)
    {
        if (!orbitView)
            return Center + new Vector(x - editor.Room.Width / 2, y - editor.Room.Depth / 2) * Scale;

        var across = Math.Cos(_angle) * (x - editor.Room.Width / 2) -
            Math.Sin(_angle) * (y - editor.Room.Depth / 2);
        return Center + new Vector(across, Depth(x, y) * 0.52 - z * 0.8) * Scale;
    }

    private Vector2 Unproject(Point point, double z)
    {
        var displacement = (point - Center) / Scale;
        var x = displacement.X;
        var y = displacement.Y;
        if (orbitView)
        {
            var depth = (y + z * 0.8) / 0.52;
            x = Math.Cos(_angle) * displacement.X + Math.Sin(_angle) * depth;
            y = -Math.Sin(_angle) * displacement.X + Math.Cos(_angle) * depth;
        }

        return new Vector2((float)(x + editor.Room.Width / 2), (float)(y + editor.Room.Depth / 2));
    }

    private Point Marker(Luminaire light) => Project(light.Position.X, light.Position.Y, Room.MountHeight);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        var hit = LightDrawOrder().Reverse()
            .FirstOrDefault(index => WithinMarker(Marker(editor.Lights[index]), point), -1);
        if (hit < 0 && !orbitView)
        {
            editor.Select(-1);
            return;
        }

        _dragLight = hit >= 0;
        if (_dragLight)
        {
            editor.Select(hit);
            _dragOffset = editor.Lights[hit].Position - Unproject(point, Room.MountHeight);
        }

        _last = point;
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private static bool WithinMarker(Point marker, Point pointer)
    {
        var dx = marker.X - pointer.X;
        var dy = marker.Y - pointer.Y;
        return dx * dx + dy * dy <= 13 * 13;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer != _pointer)
            return;

        var point = e.GetPosition(this);
        if (_dragLight)
            editor.MoveSelected(Unproject(point, Room.MountHeight) + _dragOffset);
        else
        {
            _angle += (point.X - _last.X) * 0.013;
            InvalidateVisual();
        }

        _last = point;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer == _pointer &&
            e.InitialPressMouseButton == MouseButton.Left)
            e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer == _pointer)
            _pointer = null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.55, 2.5);
        InvalidateVisual();
        e.Handled = true;
    }
}
