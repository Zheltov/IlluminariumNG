using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Lighting;

internal sealed partial class EditorWindow : Window
{
    private static readonly IBrush Muted = Brush.Parse("#9FB2C2");
    private readonly SceneView _plan;
    private readonly SceneView _orbit;
    private readonly TextBlock _status = Label("", 13, Muted);
    private readonly TextBlock _selection = Label("", 13, Muted);
    private readonly Button _calculate = new() { Content = "Рассчитать" };
    private readonly Button _remove = new() { Content = "Удалить" };
    private CancellationTokenSource? _pending;
    private string _resultStatus = "Расчётная плоскость · 0,8 м";
    private readonly TextBox _height = new() { Text = 2.8.ToString(), Width = 72 };

    private readonly EditorScene _scene = new(new Room(8, 6),
    [
        new(new Vector2(2, 2), Photometry.Sample),
        new(new Vector2(6, 2), Photometry.Sample),
        new(new Vector2(4, 4), Photometry.Sample)
    ]);

    public Room Room => _scene.Room;
    public IReadOnlyList<Luminaire> Lights => _scene.Lights;
    public int Selected { get; private set; } = -1;
    public double[,]? Result => _scene.Result;

    public EditorWindow()
    {
        UpdateProjectTitle();
        Width = 1200;
        Height = 760;
        MinWidth = 850;
        MinHeight = 560;
        Background = Brush.Parse("#0C131E");

        _plan = new SceneView(this, false);
        _orbit = new SceneView(this, true);
        var views = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        var plan = ViewPanel("План помещения", "Перетащите светильник · шаг 0,25 м", _plan);
        var orbit = ViewPanel("3D-вид", "Перетащите фон для поворота · прокрутите для зума", _orbit);
        Grid.SetColumn(orbit, 1);
        views.Children.Add(plan);
        views.Children.Add(orbit);

        var draw = new Button { Content = "Новый контур" };
        draw.Click += (_, _) =>
        {
            _plan.BeginOutline(true);
            _status.Text = "Поставьте вершины на плане. Нажмите первую вершину или «Замкнуть».";
        };
        var edit = new Button { Content = "Править вершины" };
        edit.Click += (_, _) =>
        {
            _plan.BeginOutline(false);
            _status.Text = "Перетащите вершину. Двойной щелчок по стене добавляет вершину, правый щелчок по вершине удаляет её.";
        };
        var accept = new Button { Content = "Замкнуть" };
        accept.Click += (_, _) => _plan.AcceptOutline();
        var cancel = new Button { Content = "Выйти из режима" };
        cancel.Click += (_, _) =>
        {
            _plan.CancelOutline();
            _plan.CancelInterior();
            Refresh();
        };
        var setHeight = new Button { Content = "Задать высоту" };
        setHeight.Click += (_, _) => ApplyOutline(Room.Vertices);
        var outlineToolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var control in new Control[] { draw, edit, accept, cancel, Label("Высота, м", 12, Muted), _height, setHeight })
            outlineToolbar.Children.Add(control);

        var add = new Button { Content = "Добавить" };
        add.Click += (_, _) => AddLight(Photometry.Sample);
        var import = new Button { Content = "Открыть IES…" };
        import.Click += async (_, _) => await RunFileAction(ImportPhotometry);
        _remove.Click += (_, _) =>
        {
            if (Selected < 0)
                return;
            _scene.RemoveLight(Selected);
            Selected = -1;
            SceneChanged();
        };
        _calculate.Click += async (_, _) => await Calculate();

        var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var control in new[] { add, import, _remove, _calculate })
        {
            control.VerticalAlignment = VerticalAlignment.Bottom;
            toolbar.Children.Add(control);
        }

        var header = new StackPanel { Spacing = 10 };
        header.Children.Add(Label("Проект освещения", 27, Brushes.White, FontWeight.SemiBold));
        header.Children.Add(ProjectToolbar());
        header.Children.Add(outlineToolbar);
        header.Children.Add(InteriorToolbar());
        header.Children.Add(toolbar);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(_selection, 1);
        footer.Children.Add(_status);
        footer.Children.Add(_selection);
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 15,
            Margin = new Thickness(20)
        };
        Grid.SetRow(views, 1);
        Grid.SetRow(footer, 2);
        layout.Children.Add(header);
        layout.Children.Add(views);
        layout.Children.Add(footer);
        Content = layout;
        Closing += OnProjectClosing;
        Closed += (_, _) => _pending?.Cancel();
        Refresh();
    }

    public void Select(int index)
    {
        Selected = index;
        Refresh();
    }

    public void MoveSelected(Vector2 position)
    {
        if (Selected >= 0 && _scene.TryMoveLight(Selected, position))
            SceneChanged();
    }

    public bool ApplyOutline(IEnumerable<RoomPoint> vertices)
    {
        if (!double.TryParse(_height.Text, out var height))
        {
            _status.Text = "Введите высоту в метрах.";
            return false;
        }

        var revision = _scene.Revision;
        if (!_scene.TryApplyOutline(vertices, height, out var error))
        {
            _status.Text = error;
            return false;
        }

        if (revision != _scene.Revision)
            SceneChanged();
        return true;
    }

    private void AddLight(Photometry distribution)
    {
        if (!_scene.TryAddLight(distribution))
        {
            _status.Text = "Координаты помещения слишком точны для размещения светильника.";
            return;
        }

        Selected = Lights.Count - 1;
        SceneChanged();
    }

    private async Task ImportPhotometry()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Выберите фотометрию",
                FileTypeFilter = [new FilePickerFileType("Фотометрия IES") { Patterns = ["*.ies", "*.IES"] }]
            });
            if (files.Count == 0)
                return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            AddLight(Photometry.Read(await reader.ReadToEndAsync()));
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
        }
    }

    private async Task Calculate()
    {
        if (_pending is not null)
        {
            _pending.Cancel();
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        _calculate.Content = "Отменить";
        _status.Text = "Расчёт освещённости…";
        var room = Room;
        var lights = Lights.ToArray();
        var revision = _scene.Revision;
        try
        {
            var result = await Task.Run(() => Illuminance.Calculate(room, lights, cancellation.Token));
            if (cancellation.IsCancellationRequested || !_scene.TryPublishResult(revision, result))
                return;

            var average = Illuminance.Mean(result);
            _resultStatus = double.IsNaN(average) ? "Внутри контура нет точек расчётной сетки." : $"Средняя: {average:F0} лк   ·   Шкала 0–350+ лк   ·   Плоскость 0,8 м";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (revision == _scene.Revision && cancellation.IsCancellationRequested)
                _resultStatus = "Расчёт отменён";
            _pending = null;
            _calculate.Content = "Рассчитать";
            Refresh();
        }
    }

    private void SceneChanged()
    {
        UpdateProjectTitle();
        _pending?.Cancel();
        _resultStatus = "Сцена изменена · запустите расчёт";
        Refresh();
    }

    private void Refresh()
    {
        _status.Text = _resultStatus;
        _remove.IsEnabled = Selected >= 0;
        _selection.Text = Selected < 0
            ? $"Светильников: {Lights.Count}"
            : $"Светильник {Selected + 1}  ·  X {Lights[Selected].Position.X:F2} м  ·  Y {Lights[Selected].Position.Y:F2} м";
        _plan.InvalidateVisual();
        _orbit.InvalidateVisual();
    }

    private static Border ViewPanel(string title, string hint, SceneView view)
    {
        var heading = new StackPanel { Margin = new Thickness(14, 10), Spacing = 4 };
        heading.Children.Add(Label(title, 16, Brushes.White, FontWeight.SemiBold));
        heading.Children.Add(Label(hint, 12, Muted));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        Grid.SetRow(view, 1);
        layout.Children.Add(heading);
        layout.Children.Add(view);
        return new Border
        {
            Background = Brush.Parse("#162230"),
            BorderBrush = Brush.Parse("#2A3B4B"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = layout
        };
    }

    private static TextBlock Label(string title, double size, IBrush color, FontWeight weight = FontWeight.Normal) =>
        new() { Text = title, FontSize = size, Foreground = color, FontWeight = weight };

}
