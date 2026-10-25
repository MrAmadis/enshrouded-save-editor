using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace EnshroudedSaveEditor.App;

internal static class Program
{
    private const string WebView2Download = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Enshrouded Save Editor needs the Microsoft Edge WebView2 Runtime, which is missing on this PC.\n\nThe download page will open now.",
                "Enshrouded Save Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Process.Start(new ProcessStartInfo(WebView2Download) { UseShellExecute = true });
            return;
        }
        Application.Run(new MainForm());
    }
}
