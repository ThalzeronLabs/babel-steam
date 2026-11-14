using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Babel.Core;

public enum ServiceState { Starting, NoSteam, SteamWithoutPort, PortNotSteam, Translating, Paused, Stopped }

/// <summary>
/// The service. It connects to Steam's debugging port (the same protocol as Chrome's DevTools, CDP), puts the page
/// script in every page and answers the translation requests the pages send through the "__babelSend" binding: a
/// direct channel between the page and this process, with no network and no page security rules in the way.
/// </summary>
public sealed class Injector(Settings settings, Translator translator)
{
    /// <summary>A guard against a page that would ask for translations without end.</summary>
    public const int MaxCharsPerMinute = 400_000;

    readonly ConcurrentDictionary<string, Task> pages = new();
    readonly ConcurrentDictionary<string, DateTime> failedAt = new();
    readonly ConcurrentDictionary<string, PageSession> sessions = new();
    DateTime? withoutPortSince;
    DateTime lastReopen = DateTime.MinValue;
    bool warnedIntruder;

    public string Script { get; } = BuildScript(settings.MinWords);
    public ServiceState State { get; private set; } = ServiceState.Starting;
    public int PagesInjected => sessions.Count;
    public bool Paused { get; private set; }
    public event Action? Changed;

    /// <summary>The page script with its settings filled in. Its version is a hash of the script itself, so a new
    /// Babel replaces an old script that is still running in an open page.</summary>
    public static string BuildScript(int minWords)
    {
        using var s = typeof(Injector).Assembly.GetManifestResourceStream("Babel.pagina.js")!;
        var script = new StreamReader(s, Encoding.UTF8).ReadToEnd().Replace("__PALAVRAS_MINIMAS__", minWords.ToString());
        var version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(script)))[..12];
        return script.Replace("__VERSAO__", version);
    }

    void Set(ServiceState s)
    {
        if (State == s) return;
        State = s;
        Changed?.Invoke();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (await Steam.TargetsAsync(settings.Port, ct) == null && !Steam.Running() && Steam.Installed())
        {
            Log.Write("opening Steam with the port");
            Steam.Open(settings.Port);
        }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log.Write($"watcher: {e.Message}");
            }
            await Task.Delay(1500, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        Set(ServiceState.Stopped);
    }

    async Task TickAsync(CancellationToken ct)
    {
        if (Paused)
        {
            Set(ServiceState.Paused);
            return;
        }
        var targets = await Steam.TargetsAsync(settings.Port, ct);
        if (targets == null)
        {
            await SteamWithoutPortAsync();
            return;
        }
        if (!Steam.IsSteam(targets))
        {
            if (!warnedIntruder) Log.Write($"port {settings.Port} is not Steam's; nothing is injected");
            warnedIntruder = true;
            Set(ServiceState.PortNotSteam);
            return;
        }
        warnedIntruder = false;
        withoutPortSince = null;
        Set(ServiceState.Translating);
        var now = DateTime.UtcNow;
        foreach (var t in targets.Where(Steam.ShouldInject))
        {
            if (pages.ContainsKey(t.Id)) continue;
            if (failedAt.TryGetValue(t.Id, out var f) && now - f < TimeSpan.FromSeconds(10)) continue;
            pages[t.Id] = Task.Run(() => PageAsync(t, ct), ct);
        }
    }

    /// <summary>Steam was opened without the port (a desktop shortcut, the taskbar)? Reopen it with the port, carefully:
    /// never with a game running, only after 45 s stable, and at most once every 10 minutes.</summary>
    async Task SteamWithoutPortAsync()
    {
        if (!Steam.Running())
        {
            withoutPortSince = null;
            Set(ServiceState.NoSteam);
            return;
        }
        Set(ServiceState.SteamWithoutPort);
        var now = DateTime.UtcNow;
        if (withoutPortSince == null || Steam.GameRunning())
        {
            withoutPortSince = now;
            return;
        }
        if (now - withoutPortSince > TimeSpan.FromSeconds(45) && now - lastReopen > TimeSpan.FromMinutes(10))
        {
            Log.Write("Steam is open without the debugging port; reopening it with the port");
            lastReopen = now;
            await Steam.ReopenAsync(settings.Port);
        }
    }

    async Task PageAsync(Target t, CancellationToken ct)
    {
        var session = new PageSession(t, this);
        try
        {
            sessions[t.Id] = session;
            await session.RunAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            failedAt[t.Id] = DateTime.UtcNow;
        }
        finally
        {
            sessions.TryRemove(t.Id, out _);
            pages.TryRemove(t.Id, out _);
        }
    }

    /// <summary>Stops translating: every open page shows its original text again, and new pages are left alone.</summary>
    public async Task PauseAsync(bool pause)
    {
        Paused = pause;
        foreach (var s in sessions.Values)
            await s.EvaluateQuietlyAsync(pause
                ? "window.__babel && window.__babel.parar && window.__babel.parar(); delete window.__babel;"
                : Script);
        Set(pause ? ServiceState.Paused : ServiceState.Starting);
        Changed?.Invoke();
    }

    internal Translator Translator => translator;

    /// <summary>One Steam page: injects the script and answers its requests.</summary>
    sealed class PageSession(Target target, Injector owner)
    {
        readonly ClientWebSocket ws = new();
        readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> waiting = new();
        readonly SemaphoreSlim sendLock = new(1, 1);
        int seq;
        DateTime windowStart = DateTime.UtcNow;
        long windowChars;
        string Title => target.Title.Length > 40 ? target.Title[..40] : target.Title;

        public async Task RunAsync(CancellationToken ct)
        {
            ws.Options.KeepAliveInterval = TimeSpan.Zero;
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(new Uri(target.WebSocketUrl!), connect.Token);
            var reader = ReadAsync(ct);
            await CommandAsync("Runtime.enable");
            await CommandAsync("Runtime.addBinding", new JsonObject { ["name"] = "__babelSend" });
            await CommandAsync("Page.enable");
            // Pages that navigate (store, community) get the script again on every new document...
            await CommandAsync("Page.addScriptToEvaluateOnNewDocument", new JsonObject { ["source"] = owner.Script });
            // ...and the page already open gets it now.
            await CommandAsync("Runtime.evaluate", new JsonObject { ["expression"] = owner.Script });
            Log.Write($"injected into a page ({target.Url.Split('?')[0]})");
            await reader;
        }

        public async Task EvaluateQuietlyAsync(string expression)
        {
            try
            {
                await CommandAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression });
            }
            catch
            {
                // the page may be gone
            }
        }

        async Task<JsonNode> CommandAsync(string method, JsonObject? parameters = null)
        {
            int id = Interlocked.Increment(ref seq);
            var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting[id] = tcs;
            var msg = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
            var bytes = Encoding.UTF8.GetBytes(msg.ToJsonString());
            await sendLock.WaitAsync();
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                sendLock.Release();
            }
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }

        async Task ReadAsync(CancellationToken ct)
        {
            var buffer = new byte[64 * 1024];
            using var message = new MemoryStream();
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var r = await ws.ReceiveAsync(buffer, ct);
                if (r.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, r.Count);
                if (!r.EndOfMessage) continue;
                var node = JsonNode.Parse(message.GetBuffer().AsSpan(0, (int)message.Length));
                message.SetLength(0);
                if (node?["id"] is JsonNode idNode)
                {
                    if (waiting.TryRemove((int)idNode, out var tcs)) tcs.TrySetResult(node);
                }
                else if ((string?)node?["method"] == "Runtime.bindingCalled" && (string?)node["params"]?["name"] == "__babelSend")
                {
                    _ = HandleAsync(node["params"]!);
                }
            }
        }

        bool WithinLimit(IEnumerable<string> texts)
        {
            var now = DateTime.UtcNow;
            if (now - windowStart > TimeSpan.FromMinutes(1))
            {
                windowStart = now;
                windowChars = 0;
            }
            windowChars += texts.Sum(t => (long)t.Length);
            return windowChars <= MaxCharsPerMinute;
        }

        async Task HandleAsync(JsonNode p)
        {
            try
            {
                var request = JsonNode.Parse((string)p["payload"]!)!;
                if ((string?)request["tipo"] == "log")
                {
                    var msg = (string?)request["msg"] ?? "";
                    Log.Write($"page: {(msg.Length > 300 ? msg[..300] : msg)}");
                    return;
                }
                var texts = (request["textos"]?.AsArray() ?? [])
                    .Take(200)
                    .Select(x => (string?)x ?? "")
                    .Select(x => x.Length > 20000 ? x[..20000] : x)
                    .ToList();
                Item?[] items;
                if (owner.Paused)
                {
                    items = new Item?[texts.Count];
                }
                else if (!WithinLimit(texts))
                {
                    Log.Write($"a page passed {MaxCharsPerMinute} characters per minute; request ignored");
                    items = new Item?[texts.Count];
                }
                else
                {
                    items = await owner.Translator.TranslateAsync(texts);
                }
                var answer = new JsonObject
                {
                    ["id"] = request["id"]?.DeepClone(),
                    ["itens"] = new JsonArray(items.Select(i => i == null ? null : (JsonNode)new JsonObject { ["t"] = i.T, ["l"] = i.L }).ToArray()),
                };
                // The default encoder escapes <, >, & and every non-ASCII character, so the answer is plain ASCII JSON,
                // which is also valid JavaScript and cannot close a tag or break the expression.
                var json = JsonSerializer.Serialize(answer);
                await CommandAsync("Runtime.evaluate", new JsonObject
                {
                    ["contextId"] = p["executionContextId"]?.DeepClone(),
                    ["expression"] = $"window.__babel && window.__babel.receber({json})",
                });
            }
            catch (Exception e)
            {
                Log.Write($"a request failed: {e.Message}");
            }
        }
    }
}
