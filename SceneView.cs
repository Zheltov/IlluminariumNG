using Vector2 = System.Numerics.Vector2;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lighting;

internal sealed partial class SceneView(EditorWindow editor, bool orbitView) : Control
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
        var corners = room.Vertices;
        if (orbitView)
        {
            for (var i = 0; i < corners.Count; i++)
                DrawRoomWall(context, i);
        }

        var floor = Shape(corners.Select(p => Project(p.X, p.Y)).ToArray());
        context.DrawGeometry(Floor, Edge, floor);
        using (context.PushGeometryClip(floor))
        {
            for (var i = 1; i < Math.Min(room.Width, 200); i++)
                context.DrawLine(GridLine, Project(room.MinX + i, room.MinY),
                    Project(room.MinX + i, room.MinY + room.Depth));
            for (var i = 1; i < Math.Min(room.Depth, 200); i++)
                context.DrawLine(GridLine, Project(room.MinX, room.MinY + i),
                    Project(room.MinX + room.Width, room.MinY + i));
        }

        if (editor.Result is { } result)
        {
            var height = orbitView ? Room.WorkHeight : 0;
            using var mapClip = context.PushGeometryClip(Shape(corners.Select(p => Project(p.X, p.Y, height)).ToArray()));
            var dx = room.Width / Illuminance.Columns;
            var dy = room.Depth / Illuminance.Rows;
            for (var y = 0; y < Illuminance.Rows; y++)
            for (var x = 0; x < Illuminance.Columns; x++)
            {
                if (double.IsNaN(result[x, y]))
                    continue;
                var band = (int)Math.Clamp(result[x, y] / 50, 0, Heat.Length - 1);
                var left = room.MinX + x * dx;
                var top = room.MinY + y * dy;
                Polygon(context, Heat[band], null, Project(left, top, height),
                    Project(left + dx, top, height), Project(left + dx, top + dy, height),
                    Project(left, top + dy, height));
            }
        }

        DrawInterior(context);
        DrawOutlineEditor(context);

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

    private static void Polygon(DrawingContext context, IBrush? fill, Pen? pen, params Point[] points) =>
        context.DrawGeometry(fill, pen, Shape(points));

    private static StreamGeometry Shape(Point[] points)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], true);
            foreach (var point in points.Skip(1))
                path.LineTo(point);
            path.EndFigure(true);
        }

        return geometry;
    }

    private double Scale => Math.Max(1, Math.Min(
        (Bounds.Width - 46) / (orbitView ? Math.Sqrt(editor.Room.Width * editor.Room.Width +
            editor.Room.Depth * editor.Room.Depth) : editor.Room.Width),
        (Bounds.Height - 46) / (orbitView ? 0.52 * Math.Sqrt(editor.Room.Width * editor.Room.Width +
            editor.Room.Depth * editor.Room.Depth) + editor.Room.Height * 0.8 : editor.Room.Depth))) * _zoom;

    private double MidX => editor.Room.MinX + editor.Room.Width / 2;
    private double MidY => editor.Room.MinY + editor.Room.Depth / 2;

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2 + (orbitView ? Scale * editor.Room.Height * 0.4 : 0));

    private double Depth(double x, double y) =>
        Math.Sin(_angle) * (x - MidX) + Math.Cos(_angle) * (y - MidY);

    private Point Project(double x, double y, double z = 0)
    {
        if (!orbitView)
            return Center + new Vector(x - MidX, y - MidY) * Scale;

        var across = Math.Cos(_angle) * (x - MidX) -
            Math.Sin(_angle) * (y - MidY);
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

        return new Vector2((float)(x + MidX), (float)(y + MidY));
    }

    private Point Marker(Luminaire light) => Project(light.Position.X, light.Position.Y, editor.Room.Height);

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_pointer != null)
            return;

        var point = e.GetPosition(this);
        if (!orbitView && HandleOutlinePress(e, point))
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (!orbitView && _interiorTool is not null)
        {
            e.Handled = true;
            await PlaceInterior(point);
            return;
        }

        var hit = LightDrawOrder().Reverse()
            .FirstOrDefault(index => WithinMarker(Marker(editor.Lights[index]), point), -1);
        if (!orbitView)
        {
            var element = hit < 0 ? InteriorHit(point) : null;
            editor.SelectInterior(element);
            if (element is not null)
            {
                editor.Select(-1);
                e.Handled = true;
                return;
            }
        }
        if (hit < 0 && !orbitView)
        {
            editor.Select(-1);
            return;
        }

        _dragLight = hit >= 0;
        if (_dragLight)
        {
            editor.Select(hit);
            _dragOffset = editor.Lights[hit].Position - Unproject(point, editor.Room.Height);
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
        if (MoveOutlineVertex(point))
            return;
        if (_dragLight)
            editor.MoveSelected(Unproject(point, editor.Room.Height) + _dragOffset);
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
        {
            CommitOutlineVertex();
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer == _pointer)
        {
            _pointer = null;
            CancelVertexDrag();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _zoom = Math.Clamp(_zoom * Math.Pow(1.15, e.Delta.Y), 0.55, 2.5);
        InvalidateVisual();
        e.Handled = true;
    }
}
