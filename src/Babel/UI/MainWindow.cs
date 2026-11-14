using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Babel.Core;

namespace Babel.UI;

/// <summary>
/// One window with two faces. The first time, a three-step guided setup (what Babel does and what it sends where;
/// the free Gemini key; finishing). After that, a small dashboard: the state, the numbers, the key and the options.
/// </summary>
public sealed class MainWindow : Window
{
    const string KeyPage = "https://aistudio.google.com/apikey";
    const string ProjectPage = "https://github.com/ThalzeronLabs/babel-steam";

    readonly TrayApp? app;
    readonly Runtime runtime;
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    int step;
    bool wizard;
    bool startWithWindows = true;
    bool? adjustSteam; // nothing chosen until the person chooses (informed consent)
    string keyMessage = "";
    Brush keyColor = Ui.Muted;
    bool busy;
    PasswordBox? keyBox;

    /// <summary>Numbers for the README picture of the dashboard, so drawing it never touches Steam.</summary>
    internal record Preview(long Translated, long FromCache, long Stored, string Model, int Pages);
    internal Preview? preview;

    public MainWindow(TrayApp? app, Runtime runtime)
    {
        this.app = app;
        this.runtime = runtime;
        Title = "Babel para Steam";
        Background = Ui.Background;
        Width = 640;
        Height = 740;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/babel.ico"));
        wizard = !runtime.Settings.SetupDone || KeyStore.Read() == null;
        refresh.Tick += (_, _) => { if (!wizard && !busy) Render(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) refresh.Start(); else refresh.Stop(); };
        Render();
    }

    public void ShowKeyStep()
    {
        wizard = true;
        step = 1;
        Render();
    }

    // ---------- the frame ----------

    void Render()
    {
        var body = new StackPanel();
        string title;
        UIElement? footer;
        if (wizard)
        {
            (title, footer) = step switch
            {
                0 => Welcome(body),
                1 => KeyStep(body),
                _ => Finish(body),
            };
        }
        else
        {
            (title, footer) = Dashboard(body);
        }

        var root = new DockPanel { Margin = new Thickness(30, 22, 30, 22) };
        var top = new StackPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Image
        {
            Source = Icon,
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 10, 0),
        });
        brand.Children.Add(Ui.Label("Babel para Steam", 15, Ui.Muted, FontWeights.SemiBold));
        if (wizard)
        {
            var dots = Ui.Label($"   ·   Passo {step + 1} de 3", 13, Ui.Muted);
            dots.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(dots);
        }
        top.Children.Add(brand);
        var t = Ui.Label(title, 24, Ui.Text, FontWeights.SemiBold);
        t.Margin = new Thickness(0, 12, 0, 6);
        top.Children.Add(t);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        if (footer != null)
        {
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
        }
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body, Margin = new Thickness(0, 4, 0, 0) });
        Content = root;
    }

    static Grid Nav(Border? back, Border next)
    {
        var g = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (back != null)
        {
            back.MinWidth = 110;
            g.Children.Add(back);
        }
        next.MinWidth = 180;
        Grid.SetColumn(next, 2);
        g.Children.Add(next);
        return g;
    }

    static TextBlock P(string text, Brush? color = null, double size = 14)
    {
        var t = Ui.Label(text, size, color);
        t.Margin = new Thickness(0, 0, 0, 10);
        return t;
    }

    // ---------- 1. welcome ----------

    (string, UIElement?) Welcome(StackPanel body)
    {
        body.Children.Add(P("O Babel traduz para o português do Brasil o que as pessoas e os estúdios escrevem na Steam: " +
            "notas de atualização, análises, discussões, guias, descrições da Oficina e textos da loja que não têm pt-BR. " +
            "Os menus da Steam continuam como estão; o Babel cuida do resto, na hora, na própria tela."));
        var how = new StackPanel();
        how.Children.Add(Ui.Label("Como funciona", 13, Ui.Muted, FontWeights.SemiBold));
        foreach (var line in new[]
                 {
                     "1.  A Steam abre com uma porta de depuração, uma opção oficial dela, que só aceita conexões deste PC.",
                     "2.  O Babel lê o texto que está na tela e manda só o que é estrangeiro para o Gemini, a IA do Google, com a SUA chave grátis.",
                     "3.  A tradução volta em segundos e entra no lugar do texto. Cada trecho é traduzido uma vez só: abrir de novo não gasta nada.",
                 })
        {
            var l = Ui.Label(line, 13.5);
            l.Margin = new Thickness(0, 6, 0, 0);
            how.Children.Add(l);
        }
        body.Children.Add(Ui.Box(how));

        var notice = new StackPanel();
        notice.Children.Add(Ui.Label("Antes de seguir, com franqueza", 13, Ui.Amber, FontWeights.SemiBold));
        foreach (var line in new[]
                 {
                     "•  O Babel é independente, sem ligação com a Valve. Ele só muda o texto na tela: não mexe nos arquivos da Steam, nos jogos nem na sua conta. Mesmo assim, o Acordo de Assinatura da Steam proíbe modificar o cliente sem autorização da Valve, e a Valve pode restringir contas. Use por sua conta e risco.",
                     "•  Enquanto a Steam estiver aberta com a opção de depuração, com ou sem o Babel, qualquer programa deste PC pode controlar a Steam logada. Use num computador em que você confia.",
                     "•  O texto estrangeiro da tela vai para o Google pela SUA chave. A API do Gemini exige 18 anos ou mais. No nível grátis, o Google usa o texto para melhorar os produtos dele, e pessoas podem lê-lo; na União Europeia, na Suíça e no Reino Unido, só a chave paga é permitida.",
                     "•  Por isso o Babel nunca traduz as páginas de conta, carteira, pagamento e login, e o chat dos amigos vem desligado. Nada passa por nós: não há servidor do Babel.",
                 })
        {
            var l = Ui.Label(line, 13);
            l.Margin = new Thickness(0, 6, 0, 0);
            notice.Children.Add(l);
        }
        body.Children.Add(Ui.Box(notice, Ui.Amber));
        var more = Ui.Link("Ler tudo isso em detalhe (abre no navegador)", () => Ui.OpenUrl(ProjectPage + "#privacidade"));
        body.Children.Add(more);
        return ("Bem-vindo ao Babel", Nav(null, Ui.Button("Entendi, continuar", Ui.AccentDark, () => { step = 1; Render(); })));
    }

    // ---------- 2. the key ----------

    (string, UIElement?) KeyStep(StackPanel body)
    {
        var saved = KeyStore.Read();
        body.Children.Add(P("O Babel usa o Gemini, a IA do Google. Você cria uma chave grátis, sem cartão, em uns 2 minutos:"));
        var steps = new StackPanel();
        foreach (var line in new[]
                 {
                     "1.  Clique no botão abaixo: abre o Google AI Studio no navegador.",
                     "2.  Entre com a sua conta do Google e aceite os termos, se ele pedir.",
                     "3.  Clique em \"Criar chave de API\" (Create API key). Se ele pedir um projeto, escolha o que ele sugerir.",
                     "4.  Copie a chave (ela começa com AIza) e cole aqui embaixo.",
                 })
        {
            var l = Ui.Label(line, 13.5);
            l.Margin = new Thickness(0, 3, 0, 3);
            steps.Children.Add(l);
        }
        var open = Ui.Button("Abrir o Google AI Studio", Ui.Line, () => Ui.OpenUrl(KeyPage), 13.5);
        open.Margin = new Thickness(0, 10, 0, 0);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        steps.Children.Add(open);
        body.Children.Add(Ui.Box(steps));

        body.Children.Add(Ui.Label(saved != null ? $"Sua chave guardada termina em {KeyStore.Hint(saved)}. Para trocar, cole outra:" : "Cole a sua chave aqui:", 13, Ui.Muted));
        var row = new Grid { Margin = new Thickness(0, 6, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keyBox = new PasswordBox { FontSize = 15, Padding = new Thickness(10, 7, 10, 7), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(keyBox, "Chave do Gemini");
        row.Children.Add(keyBox);
        var paste = Ui.Button("Colar", Ui.Line, () =>
        {
            if (Clipboard.ContainsText()) keyBox.Password = Clipboard.GetText().Trim();
        }, 13.5);
        paste.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(paste, 1);
        row.Children.Add(paste);
        body.Children.Add(row);
        var msg = Ui.Label(keyMessage, 13.5, keyColor);
        body.Children.Add(msg);
        body.Children.Add(P("A chave fica cifrada pelo Windows: só o seu usuário, neste PC, consegue abrir. Ela nunca aparece na tela nem no registro.", Ui.Muted, 12.5));

        var next = Ui.Button(busy ? "Testando…" : "Testar e continuar", Ui.AccentDark, busy ? null : async () => await TestKeyAsync(saved));
        return ("Sua chave grátis do Gemini", Nav(Ui.Button("Voltar", Ui.Line, () => { step = 0; Render(); }), next));
    }

    async Task TestKeyAsync(string? saved)
    {
        var typed = keyBox?.Password.Trim() ?? "";
        if (typed.Length == 0 && saved != null)
        {
            step = 2;
            Render();
            return;
        }
        if (!KeyStore.LooksValid(typed))
        {
            keyMessage = "Isso não parece uma chave do Gemini: ela começa com AIza e tem uns 39 caracteres, sem espaços.";
            keyColor = Ui.Red;
            Render();
            return;
        }
        busy = true;
        keyMessage = "Conferindo a chave com o Google…";
        keyColor = Ui.Muted;
        Render();
        var (ok, model, error) = await GeminiEngine.TestKeyAsync(typed);
        busy = false;
        if (ok)
        {
            KeyStore.Save(typed);
            keyMessage = $"Chave funcionando (modelo {model}).";
            keyColor = Ui.Green;
            step = 2;
        }
        else
        {
            keyMessage = error switch
            {
                "invalid-key" => "O Google recusou essa chave. Confira se copiou ela inteira, ou crie outra.",
                "network" => "Não consegui falar com o Google. Confira a internet e tente de novo.",
                "http-429" => "O Google disse que o limite de uso foi atingido. Espere um minuto e tente de novo.",
                _ => $"O Google respondeu com um erro ({error}). Tente de novo em instantes.",
            };
            keyColor = Ui.Red;
        }
        Render();
    }

    // ---------- 3. finish ----------

    (string, UIElement?) Finish(StackPanel body)
    {
        if (keyMessage.Length > 0) body.Children.Add(P("✓  " + keyMessage, Ui.Green));
        body.Children.Add(P("Falta só escolher como o Babel fica no seu PC:"));
        var opts = new StackPanel();
        opts.Children.Add(Ui.Check("Iniciar o Babel com o Windows (recomendado). Ele fica quietinho na bandeja, perto do relógio, e só trabalha quando a Steam está aberta.",
            startWithWindows, v => startWithWindows = v));
        var gap = new Border { Height = 8 };
        opts.Children.Add(gap);
        body.Children.Add(Ui.Box(opts));

        // Changing how Steam starts is the person's call: two choices, none selected.
        var steamChoice = new StackPanel();
        steamChoice.Children.Add(Ui.Label("Para traduzir, a Steam precisa abrir com a opção de depuração. Como você prefere?", 13.5, Ui.Text, FontWeights.SemiBold));
        var a = new RadioButton { GroupName = "steam", IsChecked = adjustSteam == true, Margin = new Thickness(0, 10, 0, 4),
            Content = Ui.Label("Ajustar a Steam: o Babel acrescenta a opção na inicialização da Steam e no atalho dela do menu Iniciar. Ele guarda como era, e a desinstalação desfaz.", 13.5) };
        var b = new RadioButton { GroupName = "steam", IsChecked = adjustSteam == false, Margin = new Thickness(0, 6, 0, 0),
            Content = Ui.Label("Não mexer na Steam: eu abro a Steam pelo botão \"Abrir a Steam\" do Babel quando quiser a tradução.", 13.5) };
        a.Checked += (_, _) => { adjustSteam = true; Render(); };
        b.Checked += (_, _) => { adjustSteam = false; Render(); };
        steamChoice.Children.Add(a);
        steamChoice.Children.Add(b);
        body.Children.Add(Ui.Box(steamChoice, adjustSteam == null ? Ui.Accent : null));
        body.Children.Add(P("Para usar: abra uma nota de atualização ou uma análise em inglês e espere um instante. Alt + clique num trecho mostra o original; Alt + Shift + T liga e desliga a página toda.", Ui.Muted, 13));
        var done = Ui.Button(busy ? "Preparando…" : adjustSteam == null ? "Escolha uma opção acima" : "Concluir e abrir a Steam", Ui.Green,
            busy || adjustSteam == null ? null : async () => await FinishAsync());
        return ("Tudo pronto", Nav(Ui.Button("Voltar", Ui.Line, () => { step = 1; Render(); }), done));
    }

    async Task FinishAsync()
    {
        busy = true;
        Render();
        try
        {
            Install.Apply(runtime.Settings, startWithWindows, adjustSteam == true);
            if (!runtime.Running) await runtime.StartAsync();
            wizard = false;
            if (Steam.Installed() && !Steam.Running()) Steam.Open(runtime.Settings.Port, silent: false);
        }
        catch (Exception e)
        {
            Log.Write($"setup failed: {e.Message}");
            keyMessage = "Algo deu errado ao preparar: " + e.Message;
            keyColor = Ui.Red;
        }
        busy = false;
        Render();
        if (!wizard) app?.ShowBalloon("O Babel está na bandeja", "Ele traduz a Steam sozinho. Clique no globo para ver o painel.");
    }

    // ---------- the dashboard ----------

    (string, UIElement?) Dashboard(StackPanel body)
    {
        var (text, color) = StatusText();
        var pill = new Border
        {
            Background = color, CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 5, 12, 6),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14),
            Child = Ui.Label(text, 14, Brushes.White, FontWeights.SemiBold),
        };
        body.Children.Add(pill);

        var stats = new StackPanel();
        var tr = runtime.Translator;
        long translated = preview?.Translated ?? tr?.Translated ?? 0, cached = preview?.FromCache ?? tr?.FromCache ?? 0,
            stored = preview?.Stored ?? runtime.Cache?.Count() ?? 0;
        string? model = preview?.Model ?? runtime.Model;
        stats.Children.Add(Ui.Label("Desde que o Babel abriu", 13, Ui.Muted, FontWeights.SemiBold));
        stats.Children.Add(Ui.Label($"{translated:N0} trechos traduzidos pelo Gemini · {cached:N0} vieram do cache, de graça", 14));
        stats.Children.Add(Ui.Label($"{stored:N0} traduções guardadas neste PC" + (model != null ? $" · modelo {model}" : ""), 13, Ui.Muted));
        body.Children.Add(Ui.Box(stats));

        var actions = new WrapPanel();
        Border Add(Border b)
        {
            b.Margin = new Thickness(0, 0, 8, 8);
            actions.Children.Add(b);
            return b;
        }
        if (preview == null && runtime.Injector?.State == ServiceState.SteamWithoutPort)
            // the person asks for it; never with a game open
            Add(Ui.Button(Steam.GameRunning() ? "Feche o jogo para reabrir a Steam" : "Reabrir a Steam com a tradução", Ui.AccentDark,
                Steam.GameRunning() ? null : () => _ = Steam.ReopenAsync(runtime.Settings.Port), 13.5));
        else
            Add(Ui.Button("Abrir a Steam", Ui.AccentDark, preview == null && Steam.Running() ? null : () => Steam.Open(runtime.Settings.Port, silent: false), 13.5));
        var injector = runtime.Injector;
        if (injector != null || preview != null)
            Add(Ui.Button(injector?.Paused == true ? "Retomar a tradução" : "Pausar a tradução", Ui.Line, async () => { if (injector != null) { await injector.PauseAsync(!injector.Paused); Render(); } }, 13.5));
        Add(Ui.Button("Trocar a chave", Ui.Line, ShowKeyStep, 13.5));
        Add(Ui.Button("Apagar as traduções guardadas", Ui.Line, ClearCache, 13.5));
        body.Children.Add(actions);

        var opts = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var s = runtime.Settings;
        opts.Children.Add(Ui.Check("Iniciar com o Windows", Install.StartsWithWindows, Install.SetStartWithWindows));
        opts.Children.Add(Ui.Check("Reabrir a Steam com a tradução quando ela abrir sem (nunca com um jogo aberto)", s.ReopenSteam, v =>
        {
            s.ReopenSteam = v;
            s.Save();
        }));
        opts.Children.Add(Ui.Check("Traduzir também o chat dos amigos", s.TranslateChat, v =>
        {
            if (v && MessageBox.Show(this,
                    "As mensagens dos seus amigos vão ao Google para tradução. No nível grátis, o Google pode usá-las e pessoas podem lê-las, e os termos do Google pedem que você não envie dados pessoais ao nível grátis.\n\nUse uma chave com faturamento ativado para traduzir o chat.\n\nLigar a tradução do chat?",
                    "Traduzir o chat", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                Render();
                return;
            }
            s.TranslateChat = v;
            s.Save();
        }));
        body.Children.Add(Ui.Box(opts));
        body.Children.Add(P("Alt + clique num trecho mostra o original · Alt + Shift + T liga e desliga a página toda.", Ui.Muted, 13));

        var footer = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        TextBlock FooterLink(string t, Action a)
        {
            var l = Ui.Link(t, a, 12.5);
            l.Margin = new Thickness(0, 0, 18, 0);
            footer.Children.Add(l);
            return l;
        }
        FooterLink("Ver o registro", () => Ui.OpenUrl(System.IO.Path.GetDirectoryName(Log.FilePath)!));
        FooterLink("Sobre e privacidade", () => Ui.OpenUrl(ProjectPage + "#privacidade"));
        FooterLink("Desinstalar", Uninstall);
        return ("O Babel está trabalhando", footer);
    }

    (string, Brush) StatusText()
    {
        if (preview != null) return ($"Traduzindo a Steam · {preview.Pages} página(s) ligadas", Ui.Green);
        if (runtime.Problem is { } p)
            return p switch
            {
                "no-key" => ("Falta a chave do Gemini", Ui.Red),
                "invalid-key" => ("O Google recusou a chave: troque por outra", Ui.Red),
                _ => ("Sem conexão com o Google: tento de novo sozinho", Ui.Amber),
            };
        return runtime.Injector?.State switch
        {
            ServiceState.Translating when runtime.Translator?.LimitReached == true =>
                ("Limite de uso do Babel atingido: volta sozinho no próximo minuto ou amanhã", Ui.Amber),
            ServiceState.Translating => ($"Traduzindo a Steam · {runtime.Injector.PagesInjected} página(s) ligadas", Ui.Green),
            ServiceState.Paused => ("Pausado: a Steam mostra os textos originais", Ui.Amber),
            ServiceState.SteamWithoutPort => (runtime.Settings.ReopenSteam
                ? "A Steam abriu sem a tradução: reabro sozinho quando você não estiver jogando"
                : "A Steam abriu sem a tradução: feche e use \"Abrir a Steam\" do Babel", Ui.Amber),
            ServiceState.PortNotSteam => ($"A porta {runtime.Settings.Port} está com outro programa", Ui.Amber),
            ServiceState.NoSteam => ("Esperando a Steam abrir", Ui.Line),
            _ => ("Ligando…", Ui.Line),
        };
    }

    async void ClearCache()
    {
        if (MessageBox.Show(this, "Apagar todas as traduções guardadas neste PC? Os textos voltam a ser traduzidos (e a gastar a sua cota) quando aparecerem de novo.",
                "Apagar as traduções", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        bool wasRunning = runtime.Running;
        runtime.Stop();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { "cache.sqlite", "cache.sqlite-wal", "cache.sqlite-shm" })
        {
            var path = System.IO.Path.Combine(Settings.Folder, f);
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch (System.IO.IOException) { }
        }
        Log.Write("the cache was cleared by the user");
        if (wasRunning) await runtime.StartAsync();
        Render();
    }

    void Uninstall()
    {
        var sure = MessageBox.Show(this,
            "Tirar o Babel do seu PC?\n\nA Steam volta a abrir como era antes, e o Babel sai do menu Iniciar e da inicialização do Windows.",
            "Desinstalar o Babel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (sure != MessageBoxResult.Yes) return;
        var data = MessageBox.Show(this,
            "Apagar também a sua chave e as traduções guardadas?\n\nSim: apaga tudo.  Não: guarda, caso você volte a usar.",
            "Desinstalar o Babel", MessageBoxButton.YesNo, MessageBoxImage.Question);
        runtime.Stop();
        Install.Uninstall(runtime.Settings, data == MessageBoxResult.Yes);
        // The port stays open until Steam restarts. Close it for real now, if no game is running.
        if (Steam.Running() && !Steam.GameRunning() && MessageBox.Show(this,
                "A Steam ainda está aberta com a opção de depuração. Reiniciar a Steam agora, do jeito normal, para fechar a porta?",
                "Desinstalar o Babel", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _ = Steam.RestartNormallyAsync();
        MessageBox.Show(this,
            $"Pronto. Para terminar, apague a pasta do programa:\n{Install.Folder}\n\nA Steam passa a abrir como antes na próxima vez.",
            "Desinstalar o Babel", MessageBoxButton.OK, MessageBoxImage.Information);
        app?.Quit();
    }

    /// <summary>One screen, drawn off-screen for the README.</summary>
    public FrameworkElement RenderPage(bool asWizard, int page, string? message = null)
    {
        wizard = asWizard;
        step = page;
        if (message != null)
        {
            keyMessage = message;
            keyColor = Ui.Green;
        }
        Render();
        var c = (FrameworkElement)Content;
        Content = null;
        return new Border { Background = Ui.Background, Width = 624, Height = 700, Child = c };
    }
}
