using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace AssetViewer.Browser.Startup;

internal sealed partial class BrowserApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime browser) browser.MainView = new BrowserRoot();
        base.OnFrameworkInitializationCompleted();
    }
}
