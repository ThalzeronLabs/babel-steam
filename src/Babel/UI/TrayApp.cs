using System.Windows;
using Babel.Core;
using Forms = System.Windows.Forms;

namespace Babel.UI;

/// <summary>The globe in the tray, the window, and the service running in the background.</summary>
public sealed class TrayApp
{
    readonly Application wpf;
    readonly Forms.NotifyIcon icon;
    readonly Runtime runtime;
    MainWindow? window;
    Forms.ToolStripItem? pauseItem;

    TrayApp(Application wpf, Runtime runtime)
    {
        this.wpf = wpf;
        this.runtime = runtime;
        using var s = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/babel.ico")).Stream;
        icon = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(s, Forms.SystemInformation.SmallIconSize), Visible = true, Text = "Babel para Steam" };
        icon.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowWindow(); };
        icon.BalloonTipClicked += (_, _) => ShowWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir o Babel", null, (_, _) => ShowWindow());
        pauseItem = menu.Items.Add("Pausar a tradução", null, async (_, _) =>
        {
            if (runtime.Injector is { } i) await i.PauseAsync(!i.Paused);
        });
        menu.Items.Add("Abrir a Steam", null, (_, _) => { if (!Steam.Running()) Steam.Open(runtime.Settings.Port, silent: false); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => Quit());
        menu.Opening += (_, _) => pauseItem.Text = runtime.Injector?.Paused == true ? "Retomar a tradução" : "Pausar a tradução";
        icon.ContextMenuStrip = menu;
        runtime.Changed += () => wpf.Dispatcher.BeginInvoke(() =>
        {
            UpdateTooltip();
            // A problem that shows up while translating (the credit ran out, the quota was reached) is told once,
            // so the person is not left wondering why Steam went back to English.
            var problem = runtime.Translator?.EngineProblem;
            if (problem != null && problem != lastEngineProblem)
                ShowBalloon("O Babel parou de traduzir", problem == "http-429"
                    ? "O Google atingiu o limite de uso da sua chave. Volto a tentar sozinho."
                    : "O Google recusou a sua chave. Clique aqui para ver o que fazer.");
            lastEngineProblem = problem;
        });
    }

    string? lastEngineProblem;

    void UpdateTooltip()
    {
        string state = runtime.Problem != null ? "precisa de atenção (clique)" : runtime.Injector?.State switch
        {
            ServiceState.Translating => "traduzindo",
            ServiceState.Paused => "pausado",
            ServiceState.SteamWithoutPort => "a Steam abriu sem a porta",
            ServiceState.NoSteam => "esperando a Steam",
            _ => "ligando",
        };
        var tip = $"Babel: {state}";
        icon.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    public void ShowWindow()
    {
        window ??= new MainWindow(this, runtime);
        window.Closed += (_, _) => window = null;
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    public void ShowBalloon(string title, string text) => icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.Info);

    public void Quit()
    {
        runtime.Stop();
        icon.Visible = false;
        icon.Dispose();
        wpf.Shutdown();
    }

    /// <summary>Runs Babel. With --tray it starts quietly; the first time (or without a key) it opens the setup.</summary>
    public static int Run(bool tray, bool echo)
    {
        using var single = new Mutex(true, @"Local\BabelParaSteam", out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BabelParaSteam.Show");
        if (!first)
        {
            if (!tray) showSignal.Set(); // the copy already running shows its window
            return 0;
        }
        var wpf = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        wpf.DispatcherUnhandledException += (_, e) =>
        {
            Log.Write($"unhandled: {e.Exception}");
            e.Handled = true;
        };
        var runtime = new Runtime(Settings.Load());
        var app = new TrayApp(wpf, runtime);
        wpf.Startup += async (_, _) =>
        {
            bool needsSetup = !runtime.Settings.SetupDone || (KeyStore.Read() == null && !echo);
            if (needsSetup || !tray) app.ShowWindow();
            if (!needsSetup) await runtime.StartAsync(echo);
            if (runtime.Problem != null && tray) app.ShowBalloon("O Babel precisa de atenção", "Clique aqui para ver o que falta.");
            app.UpdateTooltip();
            // a second Babel.exe opened by the person asks this one to show the window
            _ = Task.Run(() =>
            {
                while (showSignal.WaitOne())
                    wpf.Dispatcher.BeginInvoke(app.ShowWindow);
            });
        };
        return wpf.Run();
    }
}
