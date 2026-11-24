using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Lighting;

internal sealed partial class EditorWindow
{
    private const string ProjectExtension = ".lighting.json";
    private static readonly FilePickerFileType ProjectFileType = new("Проект освещения")
    {
        Patterns = ["*.lighting.json"]
    };

    private string? _projectPath;
    private int _savedRevision;
    private bool _fileAction;
    private bool _allowClose;

    private bool IsDirty => _revision != _savedRevision;

    private StackPanel ProjectToolbar()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var open = new Button { Content = "Открыть…" };
        var save = new Button { Content = "Сохранить" };
        var saveAs = new Button { Content = "Сохранить как…" };
        open.Click += async (_, _) => await RunFileAction(OpenProject);
        save.Click += async (_, _) => await RunFileAction(async () => { await SaveProject(false); });
        saveAs.Click += async (_, _) => await RunFileAction(async () => { await SaveProject(true); });
        toolbar.Children.Add(open);
        toolbar.Children.Add(save);
        toolbar.Children.Add(saveAs);
        return toolbar;
    }

    private async Task RunFileAction(Func<Task> action)
    {
        if (_fileAction)
            return;

        _fileAction = true;
        var editor = (Control)Content!;
        editor.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or JsonException)
        {
            var message = ex is JsonException ? "Повреждён файл проекта: некорректный JSON или структура данных." : ex.Message;
            await ProjectDialog("Ошибка работы с файлом", message, ("Закрыть", ProjectChoice.Cancel));
        }
        finally
        {
            editor.IsEnabled = true;
            _fileAction = false;
        }
    }

    private async Task<bool> SaveProject(bool saveAs)
    {
        var path = _projectPath;
        if (path is null || saveAs)
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Сохранить проект освещения",
                SuggestedFileName = path is null ? "Проект" : Path.GetFileName(path),
                DefaultExtension = "lighting.json",
                FileTypeChoices = [ProjectFileType],
                ShowOverwritePrompt = true
            });
            if (file is null)
                return false;
            path = LocalPath(file);
            if (!path.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += ProjectExtension;
                if (File.Exists(path) && await ProjectDialog("Заменить файл?", Path.GetFileName(path),
                    ("Заменить", ProjectChoice.Discard), ("Отмена", ProjectChoice.Cancel)) != ProjectChoice.Discard)
                    return false;
            }
        }

        var snapshot = new LightingProject(Room, Lights.ToArray());
        var revision = _revision;
        await ProjectFile.SaveAsync(path, snapshot);
        _projectPath = path;
        _savedRevision = revision;
        UpdateProjectTitle();
        return revision == _revision;
    }

    private async Task OpenProject()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Открыть проект освещения",
            FileTypeFilter = [ProjectFileType]
        });
        if (files.Count == 0)
            return;

        var path = LocalPath(files[0]);
        LightingProject project;
        await using (var stream = await files[0].OpenReadAsync())
            project = await ProjectFile.ReadAsync(stream);

        var previouslySaved = _savedRevision;
        if (!await ConfirmReplacement())
            return;

        // The confirmation may have just saved the same file we loaded.
        if (previouslySaved != _savedRevision && string.Equals(path, _projectPath,
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            project = new LightingProject(Room, Lights.ToArray());

        _projectPath = path;
        Room = project.Room;
        _width.Value = (decimal)Room.Width;
        _depth.Value = (decimal)Room.Depth;
        Lights.Clear();
        Lights.AddRange(project.Lights);
        Selected = -1;
        _savedRevision = _revision + 1;
        SceneChanged();
    }

    private async Task<bool> ConfirmReplacement()
    {
        if (!IsDirty)
            return true;

        var choice = await ProjectDialog("Сохранить изменения?", "Текущий проект содержит несохранённые изменения.",
            ("Сохранить", ProjectChoice.Save), ("Отбросить", ProjectChoice.Discard), ("Отмена", ProjectChoice.Cancel));
        return choice == ProjectChoice.Discard || (choice == ProjectChoice.Save && await SaveProject(false));
    }

    private async void OnProjectClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_allowClose)
            return;
        if (_fileAction)
        {
            args.Cancel = true;
            return;
        }
        if (!IsDirty)
            return;

        args.Cancel = true;
        var confirmed = false;
        await RunFileAction(async () => { confirmed = await ConfirmReplacement(); });
        if (!confirmed)
            return;
        _allowClose = true;
        Close();
    }

    private void UpdateProjectTitle()
    {
        var name = _projectPath is null ? "Проект без имени" : Path.GetFileName(_projectPath);
        Title = $"ПУРЯ · {name}{(IsDirty ? " *" : "")}";
    }

    private static string LocalPath(IStorageItem file) =>
        file.TryGetLocalPath() ?? throw new IOException("Выберите файл на локальном диске.");

    private enum ProjectChoice { Cancel, Save, Discard }

    private Task<ProjectChoice> ProjectDialog(string title, string message, params (string Label, ProjectChoice Choice)[] commands)
    {
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var body = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 });
        body.Children.Add(buttons);
        var dialog = new Window
        {
            Title = title,
            Content = body,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };
        foreach (var command in commands)
        {
            var button = new Button { Content = command.Label, IsCancel = command.Choice == ProjectChoice.Cancel };
            button.Click += (_, _) => dialog.Close(command.Choice);
            buttons.Children.Add(button);
        }
        return dialog.ShowDialog<ProjectChoice>(this);
    }
}
