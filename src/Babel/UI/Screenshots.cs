using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Babel.Core;

namespace Babel.UI;

/// <summary>Draws the README images from the real windows ("Babel.exe render docs/images"), without touching Steam
/// and without a key: the dashboard uses example numbers.</summary>
public static class Screenshots
{
    const double Dpi = 192;

    public static int Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // a throwaway settings folder, so drawing never reads or changes the person's real setup
        Settings.Folder = Path.Combine(Path.GetTempPath(), "babel-render-" + Environment.ProcessId);
        var runtime = new Runtime(new Settings());
        void Shot(string name, Func<MainWindow, FrameworkElement> draw)
        {
            var w = new MainWindow(null, runtime);
            Save(draw(w), Path.Combine(folder, name));
        }
        Shot("setup-1.png", w => w.RenderPage(true, 0));
        Shot("setup-2.png", w => w.RenderPage(true, 1));
        Shot("setup-3.png", w => w.RenderPage(true, 2, "Chave funcionando (modelo gemini-3.5-flash-lite)."));
        Shot("dashboard.png", w =>
        {
            w.preview = new MainWindow.Preview(1284, 3912, 18406, "gemini-3.5-flash-lite", 4);
            return w.RenderPage(false, 0);
        });
        try { Directory.Delete(Settings.Folder, true); } catch (IOException) { }
        app.Shutdown();
        Console.WriteLine("rendered");
        return 0;
    }

    static void Save(FrameworkElement e, string path)
    {
        e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        e.Arrange(new Rect(e.DesiredSize));
        e.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(e.ActualWidth * Dpi / 96), (int)(e.ActualHeight * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
        bmp.Render(e);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var f = File.Create(path);
        enc.Save(f);
    }
}
