using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;

namespace Babel.Core;

public enum Verdict
{
    Translate,
    NotAPage,
    NotLocal,
    HiddenContext,
    ClientMenu,
    Sensitive,
    Chat,
    UnknownWindow,
    UnknownSite,
}

/// <summary>
/// Which Steam pages Babel may touch. It is an allow list, not a block list: Babel only enters Steam's main window,
/// the store and the community, and inside those it skips every page with personal or payment data. Every other
/// window of the Steam client (chat, friends, login, settings, menus, and anything new Valve adds) is left alone.
/// </summary>
public static class Pages
{
    /// <summary>The only DevTools protocol commands Babel ever sends. Nothing about cookies, storage, network or input.</summary>
    public static readonly IReadOnlySet<string> AllowedCommands = new HashSet<string>
    {
        "Runtime.enable", "Runtime.addBinding", "Page.enable", "Page.addScriptToEvaluateOnNewDocument", "Runtime.evaluate",
    };

    public static readonly string[] Hosts = ["store.steampowered.com", "steamcommunity.com"];

    // Account, wallet, payment, purchases, login, trading and profile editing: never sent to Gemini.
    public static readonly string[] SensitivePaths =
    [
        "/account", "/checkout", "/cart", "/login", "/join", "/steamaccount", "/wallet", "/digitalgiftcards",
        "/redeemwalletcode", "/email", "/twofactor", "/phone", "/gift", "/purchase", "/subscriptions",
        "/chat", "/tradeoffer", "/trade", "/market", "/inventory", "/edit", "/settings", "/notifications",
        "/familymanagement", "/parental",
    ];

    // Small client windows that hold only buttons and menus.
    static readonly string[] MenuTitles = ["Menu", "Supernav", "Root Menu", "notificationtoasts", "SharedJSContext"];

    // Client windows with personal data, never translated even when the chat is on.
    static readonly string[] PrivateWindowTitles =
    [
        "Sign in", "Entrar", "Login", "Settings", "Configurações", "Steam Guard", "Account", "Conta", "Wallet", "Carteira",
        "Purchase", "Compra", "Friends List", "Lista de amigos",
    ];

    /// <summary>The WebSocket address must be this PC, on the configured port. A page list that points anywhere else
    /// is not Steam's and is ignored.</summary>
    public static bool IsLocal(string? ws, int port) =>
        Uri.TryCreate(ws, UriKind.Absolute, out var u) && u.Scheme == "ws" && u.Port == port
        && u.Host == "127.0.0.1"; // Steam listens on IPv4 loopback; "localhost" could resolve elsewhere

    public static Verdict Judge(Target t, int port, bool chatAllowed)
    {
        if (t.Type != "page" || t.WebSocketUrl == null) return Verdict.NotAPage;
        if (!IsLocal(t.WebSocketUrl, port)) return Verdict.NotLocal;
        if (t.Title == "SharedJSContext" || t.Url.StartsWith("devtools://") || t.Url.StartsWith("chrome")) return Verdict.HiddenContext;

        if (Uri.TryCreate(t.Url, UriKind.Absolute, out var u) && u.Scheme == "https")
        {
            if (!Hosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase)) return Verdict.UnknownSite;
            var path = u.AbsolutePath.ToLowerInvariant();
            if (SensitivePaths.Any(p => path.StartsWith(p) || path.Contains(p + "/") || path.EndsWith(p))) return Verdict.Sensitive;
            return Verdict.Translate;
        }

        if (t.Url.StartsWith("about:blank"))
        {
            // The client's own windows. The main one is called "Steam" and holds the library and the patch notes.
            if (t.Title == "Steam") return Verdict.Translate;
            if (MenuTitles.Any(m => t.Title.Contains(m, StringComparison.OrdinalIgnoreCase))) return Verdict.ClientMenu;
            if (PrivateWindowTitles.Any(m => t.Title.Contains(m, StringComparison.OrdinalIgnoreCase))) return Verdict.Sensitive;
            // What is left are the chat windows (each one is named after the friend or the group).
            return chatAllowed ? Verdict.Translate : Verdict.Chat;
        }
        return Verdict.UnknownWindow;
    }
}

/// <summary>Who is listening on the port. Only Steam's own web helper, from Steam's own folder, may receive the script:
/// a page title can be imitated by any program, the owner of the port cannot.</summary>
public static class PortOwner
{
    [StructLayout(LayoutKind.Sequential)]
    struct MIB_TCPROW_OWNER_PID
    {
        public uint state, localAddr, localPort, remoteAddr, remotePort, owningPid;
    }

    const int AF_INET = 2;
    const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);

    /// <summary>The first process listening on this TCP port, or null.</summary>
    public static int? ListeningPid(int port) => ListeningPids(port).Cast<int?>().FirstOrDefault();

    /// <summary>Every process listening on this TCP port (IPv4).</summary>
    public static List<int> ListeningPids(int port)
    {
        var pids = new List<int>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return pids;
            int count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buffer + 4 + i * rowSize);
                int rowPort = IPAddress.NetworkToHostOrder((short)(row.localPort & 0xFFFF)) & 0xFFFF;
                if (rowPort == port && !pids.Contains((int)row.owningPid)) pids.Add((int)row.owningPid);
            }
            return pids;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>True when EVERY process listening on the port is steamwebhelper.exe inside the installed Steam's folder.</summary>
    public static bool IsSteamWebHelper(int port)
    {
        var steamExe = Steam.Executable();
        var pids = ListeningPids(port);
        if (steamExe == null || pids.Count == 0) return false;
        var folder = Path.GetDirectoryName(steamExe)!;
        foreach (var pid in pids)
        {
            try
            {
                if (!IsHelperOf(Process.GetProcessById(pid).MainModule?.FileName, folder)) return false;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsHelperOf(string? helperPath, string steamFolder) =>
        helperPath != null
        && string.Equals(Path.GetFileName(helperPath), "steamwebhelper.exe", StringComparison.OrdinalIgnoreCase)
        && Path.GetFullPath(helperPath).StartsWith(Path.GetFullPath(steamFolder).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A budget of characters sent to Gemini, per minute and per day, for the whole app.</summary>
public sealed class Budget(int perMinute, int perDay, Func<DateTime>? clock = null)
{
    readonly Func<DateTime> now = clock ?? (() => DateTime.Now);
    readonly object gate = new();
    DateTime minuteStart = DateTime.MinValue, day = DateTime.MinValue;
    long minuteChars, dayChars;

    public bool LimitReached { get; private set; }

    public bool TryTake(int chars)
    {
        lock (gate)
        {
            var t = now();
            if (t - minuteStart >= TimeSpan.FromMinutes(1)) { minuteStart = t; minuteChars = 0; }
            if (t.Date != day) { day = t.Date; dayChars = 0; }
            if (minuteChars + chars > perMinute || dayChars + chars > perDay)
            {
                LimitReached = true;
                return false;
            }
            minuteChars += chars;
            dayChars += chars;
            LimitReached = false;
            return true;
        }
    }
}
