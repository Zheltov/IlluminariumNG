using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Adapters.Desktop;

internal sealed partial class EditorWindow
{
    private readonly ComboBox _elementPicker = new() { PlaceholderText = "Элементы помещения", MinWidth = 170 };
    private readonly Button _editElement = new() { Content = "Изменить…", IsEnabled = false };
    private readonly Button _removeElement = new() { Content = "Удалить элемент", IsEnabled = false };

    public IReadOnlyList<InteriorElement> InteriorElements => _scene.Elements;
    private readonly List<InteriorSelection> _interiorChoices = [];
    public InteriorSelection? SelectedInterior => _elementPicker.SelectedIndex is var index &&
        index >= 0 && index < _interiorChoices.Count ? _interiorChoices[index] : null;

    private WrapPanel InteriorToolbar()
    {
        var toolbar = new WrapPanel { ItemSpacing = 8, LineSpacing = 6 };
        foreach (var (label, tool) in new[]
        {
            ("Проём", InteriorTool.Opening), ("Перегородка", InteriorTool.Partition),
            ("Препятствие", InteriorTool.Obstacle)
        })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) =>
            {
                _plan.BeginInterior(tool);
                SelectInterior(null);
                _status.Text = tool == InteriorTool.Opening
                    ? "Щёлкните по стене или перегородке в начале проёма."
                    : "Поставьте две точки на плане.";
            };
            toolbar.Children.Add(button);
        }

        _elementPicker.SelectionChanged += (_, _) =>
        {
            _editElement.IsEnabled = _removeElement.IsEnabled = SelectedInterior is not null;
            _plan.InvalidateVisual();
            _orbit.InvalidateVisual();
        };
        _editElement.Click += async (_, _) =>
        {
            if (SelectedInterior is not { } selection)
                return;

            if (selection.Opening is { } opening)
                await EditPartitionOpening(selection.Element, ((Partition)InteriorElements[selection.Element]).Openings[opening], opening);
            else
                await EditInterior(InteriorElements[selection.Element], selection.Element);
        };
        _removeElement.Click += (_, _) =>
        {
            if (SelectedInterior is not { } selection)
                return;
            if (selection.Opening is { } opening)
                _scene.RemovePartitionOpening(selection.Element, opening);
            else
                _scene.RemoveElement(selection.Element);
            RefreshInteriorSelection(null);
            SceneChanged();
        };
        toolbar.Children.Add(_elementPicker);
        toolbar.Children.Add(_editElement);
        toolbar.Children.Add(_removeElement);
        return toolbar;
    }

    private void RefreshInteriorSelection(InteriorSelection? selection)
    {
        var labels = new List<string>();
        _interiorChoices.Clear();
        for (var index = 0; index < InteriorElements.Count; index++)
        {
            var element = InteriorElements[index];
            labels.Add($"{index + 1}. {ElementName(element)}");
            _interiorChoices.Add(new(index));
            if (element is Partition partition)
            {
                for (var opening = 0; opening < partition.Openings.Length; opening++)
                {
                    labels.Add($"{index + 1}.{opening + 1}. Проём в перегородке");
                    _interiorChoices.Add(new(index, opening));
                }
            }
        }

        _elementPicker.ItemsSource = labels;
        SelectInterior(selection);
    }

    public void SelectInterior(InteriorSelection? selection) =>
        _elementPicker.SelectedIndex = selection is { } item ? _interiorChoices.IndexOf(item) : -1;

    public void ShowInteriorHint(string message) => _status.Text = message;

    private static string ElementName(InteriorElement element) => element switch
    {
        WallOpening => "Проём",
        Partition => "Перегородка",
        _ => "Препятствие"
    };

    public Task EditInterior(InteriorElement element, int? index = null) =>
        EditInterior(element, index, null);

    public Task EditPartitionOpening(int partition, OpeningCut cut, int? index = null) =>
        EditInterior(null, index, (partition, cut));

    private async Task EditInterior(InteriorElement? element, int? index, (int Partition, OpeningCut Cut)? child)
    {
        var name = element is null ? "Проём в перегородке" : ElementName(element);
        var fields = new StackPanel { Margin = new Thickness(20), Spacing = 8 };
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 370 };
        var dialog = new Window
        {
            Title = name,
            Content = fields,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };

        TextBox Field(string label, double value)
        {
            var input = new TextBox { Text = value.ToString("G", CultureInfo.CurrentCulture) };
            fields.Children.Add(new TextBlock { Text = label });
            fields.Children.Add(input);
            return input;
        }

        double Read(TextBox input)
        {
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
                return value;
            throw new FormatException("Введите числовые значения для всех полей.");
        }

        string? ApplyElement(InteriorElement candidate)
        {
            if (!_scene.TrySetElement(index, candidate, out var error))
                return error;

            RefreshInteriorSelection(new(index ?? InteriorElements.Count - 1));
            return null;
        }

        Func<double, string?> apply;
        double initialHeight;
        if (element is WallOpening || child is not null)
        {
            var wall = element as WallOpening;
            var roomWall = wall is null ? null : Field("Номер стены", wall.Wall + 1);
            var cut = wall?.Cut ?? child!.Value.Cut;
            var offset = Field("Отступ от начала, м", cut.Offset);
            var width = Field("Ширина проёма, м", cut.Width);
            var bottom = Field("Отметка низа, м", cut.Bottom);
            initialHeight = cut.Height;

            if (child is { } target)
            {
                var partition = (Partition)InteriorElements[target.Partition];
                ShowSpan($"Перегородка {target.Partition + 1}", partition.Span);
                apply = height =>
                {
                    var opening = new OpeningCut(Read(offset), Read(width), Read(bottom), height);
                    if (!_scene.TrySetPartitionOpening(target.Partition, index, opening, out var error))
                        return error;

                    var saved = (Partition)InteriorElements[target.Partition];
                    RefreshInteriorSelection(new(target.Partition, index ?? saved.Openings.Length - 1));
                    return null;
                };
            }
            else
            {
                ShowSpan($"Стена {wall!.Wall + 1}", Room.Wall(wall.Wall));
                apply = height =>
                {
                    var number = Read(roomWall!);
                    if (number < 1 || number > Room.Vertices.Count || number != Math.Truncate(number) || !double.IsFinite(number))
                        throw new FormatException("Укажите номер существующей стены.");
                    return ApplyElement(new WallOpening((int)number - 1, Read(offset), Read(width), Read(bottom), height));
                };
            }
        }
        else
        {
            var (start, end) = element switch
            {
                Partition partition => (partition.Start, partition.End),
                BoxObstacle obstacle => (obstacle.First, obstacle.Opposite),
                _ => throw new InvalidOperationException("Неизвестный тип элемента помещения.")
            };
            var x1 = Field("X первой точки, м", start.X);
            var y1 = Field("Y первой точки, м", start.Y);
            var x2 = Field("X второй точки, м", end.X);
            var y2 = Field("Y второй точки, м", end.Y);
            initialHeight = element.Height;
            apply = height =>
            {
                var a = new RoomPoint(Read(x1), Read(y1));
                var b = new RoomPoint(Read(x2), Read(y2));
                return ApplyElement(element is Partition partition
                    ? partition with { Start = a, End = b, Height = height }
                    : new BoxObstacle(a, b, height));
            };
        }

        void ShowSpan(string label, WallSpan span) => fields.Children.Add(new TextBlock
        {
            Text = $"{label}: ({span.Start.X:F2}; {span.Start.Y:F2}) → ({span.End.X:F2}; {span.End.Y:F2})"
        });

        var height = Field("Высота, м", initialHeight);
        fields.Children.Add(feedback);
        var accept = new Button { Content = "Применить", IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        fields.Children.Add(buttons);
        cancel.Click += (_, _) => dialog.Close();
        accept.Click += (_, _) =>
        {
            try
            {
                var revision = _scene.Revision;
                if (apply(Read(height)) is { } error)
                {
                    feedback.Text = error;
                    return;
                }

                if (revision != _scene.Revision)
                    SceneChanged();
                dialog.Close();
            }
            catch (FormatException error)
            {
                feedback.Text = error.Message;
            }
        };
        await dialog.ShowDialog(this);
    }
}

internal readonly record struct InteriorSelection(int Element, int? Opening = null);

internal enum InteriorTool { Opening, Partition, Obstacle }
