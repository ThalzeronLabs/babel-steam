using System.Runtime.InteropServices;
using Babel.Core;

namespace Babel;

public static class Program
{
    /// <summary>
    ///   Babel.exe              opens the window (the guided setup the first time)
    ///   Babel.exe --tray       starts quietly in the tray (used when Windows starts)
    ///   Babel.exe --eco        uses the test engine: "[PT] text", no key and no cost
    ///   Babel.exe render DIR   draws the README screenshots from the real windows
    /// </summary>
    [DllImport("kernel32.dll")]
    static extern bool AttachConsole(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] is "help" or "--help" or "-h" or "/?" or "--version")
        {
            AttachConsole(-1);
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var version = typeof(Program).Assembly.GetName().Version;
            Console.WriteLine($"""

                Babel para Steam {version?.ToString(3)}: traduz a Steam para o português do Brasil.

                  Babel.exe              abre a janela (a configuração guiada na primeira vez)
                  Babel.exe --tray       liga quieto na bandeja (o que o Windows usa ao iniciar)
                  Babel.exe --eco        usa o motor de teste "[PT] texto", sem chave e sem custo
                  Babel.exe render DIR   desenha as imagens do README a partir das janelas de verdade
                """);
            return 0;
        }
        if (args.Length > 0 && args[0] == "render")
            return UI.Screenshots.Render(args.Length > 1 ? args[1] : "docs/images");
        try
        {
            return UI.TrayApp.Run(tray: args.Contains("--tray"), echo: args.Contains("--eco"));
        }
        catch (Exception e)
        {
            Log.Write($"fatal: {e}");
            return 1;
        }
    }
}
