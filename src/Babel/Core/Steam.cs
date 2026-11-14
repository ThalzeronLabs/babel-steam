using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace Babel.Core;

public record Target(string Id, string Type, string Title, string Url, string? WebSocketUrl);

/// <summary>Steam seen from outside: where it is, whether it runs, whether a game is open, and how to open it with
/// the debugging port. The port is a documented Steam launch option and only accepts connections from this PC.</summary>
public static class Steam
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static string[] LaunchArgs(int port) => ["-cef-enable-debugging", $"-devtools-port={port}"];

    static object? Registry(string name)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return k?.GetValue(name);
    }

    public static string? Executable() => (Registry("SteamExe") as string)?.Replace('/', '\\');

    public static bool Installed() => Executable() is { } exe && System.IO.File.Exists(exe);

    public static bool GameRunning() => Registry("RunningAppID") is int id && id != 0;

    public static bool Running() => Process.GetProcessesByName("steam").Length > 0;

    /// <summary>The pages Steam exposes on the debugging port; null when the port is closed.</summary>
    public static async Task<List<Target>?> TargetsAsync(int port, CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json/list", ct);
            return JsonNode.Parse(json)!.AsArray().Select(t => new Target(
                (string?)t!["id"] ?? "", (string?)t["type"] ?? "", (string?)t["title"] ?? "", (string?)t["url"] ?? "",
                (string?)t["webSocketDebuggerUrl"])).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                      or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Steam always has the hidden page SharedJSContext, served from its internal steamloopback.host. Any other
    /// program on the port (a browser or an app being debugged) is never touched.</summary>
    public static bool IsSteam(IEnumerable<Target> targets) =>
        targets.Any(t => t.Title == "SharedJSContext" && t.Url.Contains("steamloopback.host"));

    public static void Open(int port, bool silent = true)
    {
        var exe = Executable();
        if (exe == null) return;
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
        if (silent) psi.ArgumentList.Add("-silent");
        foreach (var a in LaunchArgs(port)) psi.ArgumentList.Add(a);
        Process.Start(psi);
    }

    /// <summary>Closes Steam calmly and opens it the normal way, without the debugging port.</summary>
    public static async Task RestartNormallyAsync()
    {
        var exe = Executable();
        if (exe == null) return;
        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false, CreateNoWindow = true });
        for (int i = 0; i < 90 && Running(); i++) await Task.Delay(1000);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
    }

    /// <summary>Closes Steam calmly (-shutdown, like its own Exit menu) and opens it again with the port.</summary>
    public static async Task ReopenAsync(int port)
    {
        var exe = Executable();
        if (exe == null) return;
        Process.Start(new ProcessStartInfo(exe, "-shutdown") { UseShellExecute = false, CreateNoWindow = true });
        for (int i = 0; i < 90 && Running(); i++) await Task.Delay(1000);
        Open(port, silent: false);
    }
}
