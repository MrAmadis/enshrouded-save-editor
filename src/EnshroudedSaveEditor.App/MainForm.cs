using EnshroudedSaveEditor.Core.Files;
using EnshroudedSaveEditor.GameData;
using EnshroudedSaveEditor.GameData.Install;
using EnshroudedSaveEditor.UI.Services;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;

namespace EnshroudedSaveEditor.App;

/// <summary>The editor window: the Blazor UI in a WebView2, plus the services it uses.</summary>
internal sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "Enshrouded Save Editor";
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1100, 720);
        BackColor = Color.FromArgb(0x0b, 0x0f, 0x14);
        StartPosition = FormStartPosition.CenterScreen;

        var options = new EditorOptions(SaveLocator.DefaultRoots(), EditorOptions.DefaultAppDataRoot, new ProcessProbe(), PickFolder);
        var icons = new IconService(options.IconCacheRoot);
        var game = new GameDataProvider(() => GameDataProvider.Load(
            GameInstall.Find(Settings.Load(options.SettingsPath).GameDir), options.GameDataCacheRoot));

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(game);
        services.AddSingleton(icons);
        services.AddSingleton<IAssetUrls>(new IconAssets(icons, game));
        services.AddSingleton(new EditorService(options, game, icons));

        var view = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = Path.Combine("wwwroot", "index.html"),
            Services = services.BuildServiceProvider(),
        };
        view.RootComponents.Add<UI.App>("#app");
        view.BlazorWebViewInitializing += (_, e) => e.UserDataFolder = Path.Combine(options.AppDataRoot, "webview2");
        view.BlazorWebViewInitialized += (_, e) =>
        {
            // Item and skill icons are plain files in the cache; WebView2 serves them under icons.ese.
            Directory.CreateDirectory(options.IconCacheRoot);
            e.WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                IconAssets.IconsHost, options.IconCacheRoot, CoreWebView2HostResourceAccessKind.Allow);
        };
        Controls.Add(view);
    }

    private string? PickFolder() => (string?)Invoke(() =>
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the folder with your Enshrouded characters (character0.ksd, ...)",
            UseDescriptionForTitle = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    });
}
