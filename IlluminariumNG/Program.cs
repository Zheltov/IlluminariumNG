using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using IlluminariumNG.Adapters.Desktop;
using IlluminariumNG.Adapters.Files;

namespace IlluminariumNG;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new EditorWindow(new JsonProjectFile());

        base.OnFrameworkInitializationCompleted();
    }
}
