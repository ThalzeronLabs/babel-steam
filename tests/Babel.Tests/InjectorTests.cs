using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Babel.Core;
using Xunit;

namespace Babel.Tests;

/// <summary>
/// The whole path without Steam: a fake "Steam" on 127.0.0.1 serves the page list and one store page over the
/// DevTools protocol. The page receives the script and asks for translations through the binding, like pagina.js.
/// </summary>
public sealed class InjectorTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "babel-inj-" + Guid.NewGuid().ToString("N"));

    public InjectorTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>What the fake page does after the script arrives: a list of (event to send first, text to ask for).</summary>
    sealed record Step(JsonObject? Before, string Text, int ContextId = 7);

    static JsonObject Navigated(string url) => new()
    {
        ["method"] = "Page.frameNavigated",
        ["params"] = new JsonObject { ["frame"] = new JsonObject { ["id"] = "p1", ["url"] = url } },
    };

    sealed class Run
    {
        public readonly List<string> Commands = [];
        public readonly List<string> Answers = [];
        public readonly CountingEngine Engine = new();
    }

    async Task<Run> PlayAsync(params Step[] steps)
    {
        int port = FreePort();
        var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{port}/");
        http.Start();
        var run = new Run();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        _ = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); } catch { return; }
                if (ctx.Request.IsWebSocketRequest)
                {
                    var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
                    _ = FakePageAsync(ws, steps, run, finished, stop.Token);
                    continue;
                }
                var list = $$"""
                    [{"id":"ctx","type":"page","title":"SharedJSContext","url":"https://steamloopback.host/index.html","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/ctx"},
                     {"id":"p1","type":"page","title":"Store","url":"https://store.steampowered.com/news/","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/p1"}]
                    """;
                var bytes = Encoding.UTF8.GetBytes(list);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        using var cache = new Cache(Path.Combine(dir, $"c{port}.sqlite"), "v1");
        var settings = new Settings { Port = port };
        var injector = new Injector(settings, new Translator(run.Engine, cache, settings), _ => true);
        _ = injector.RunAsync(stop.Token);
        await finished.Task.WaitAsync(stop.Token);
        stop.Cancel();
        http.Stop();
        return run;
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    static async Task FakePageAsync(WebSocket ws, Step[] steps, Run run, TaskCompletionSource finished, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var whole = new MemoryStream();
        int next = 0;
        async Task AskNextAsync()
        {
            if (next >= steps.Length)
            {
                // give a dropped request time to (not) reach the translator, then finish
                await Task.Delay(400, ct);
                finished.TrySetResult();
                return;
            }
            var step = steps[next++];
            if (step.Before != null) await Send(ws, step.Before, ct);
            var payload = new JsonObject { ["tipo"] = "traduzir", ["id"] = next, ["textos"] = new JsonArray(step.Text) };
            await Send(ws, new JsonObject
            {
                ["method"] = "Runtime.bindingCalled",
                ["params"] = new JsonObject { ["name"] = "__babelSend", ["payload"] = payload.ToJsonString(), ["executionContextId"] = step.ContextId },
            }, ct);
            // a dropped request gets no answer: move on after a short wait
            _ = Task.Delay(600, ct).ContinueWith(async t => { if (!t.IsCanceled && run.Answers.Count < next) await AskNextAsync(); }, ct);
        }

        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buffer, ct); } catch { return; }
            if (r.MessageType == WebSocketMessageType.Close) return;
            whole.Write(buffer, 0, r.Count);
            if (!r.EndOfMessage) continue; // the page script is ~20 KB and arrives in pieces
            var msg = JsonNode.Parse(Encoding.UTF8.GetString(whole.GetBuffer(), 0, (int)whole.Length))!;
            whole.SetLength(0);
            var method = (string)msg["method"]!;
            lock (run.Commands) run.Commands.Add(method);
            var expr = (string?)msg["params"]?["expression"];
            if (method == "Runtime.evaluate" && expr != null && expr.Contains("receber("))
            {
                lock (run.Answers) run.Answers.Add(System.Text.RegularExpressions.Regex.Unescape(expr));
                await Send(ws, new JsonObject { ["id"] = (int)msg["id"]!, ["result"] = new JsonObject() }, ct);
                await AskNextAsync();
                continue;
            }
            await Send(ws, new JsonObject { ["id"] = (int)msg["id"]!, ["result"] = new JsonObject() }, ct);
            if (method == "Runtime.enable")
            {
                // Chrome reports the existing script contexts: 7 is the page itself, 8 is an embedded frame
                await Send(ws, Context(7, "p1"), ct);
                await Send(ws, Context(8, "child-frame"), ct);
            }
            if (method == "Runtime.evaluate" && next == 0) await AskNextAsync();
        }
    }

    static JsonObject Context(int id, string frame) => new()
    {
        ["method"] = "Runtime.executionContextCreated",
        ["params"] = new JsonObject { ["context"] = new JsonObject { ["id"] = id, ["auxData"] = new JsonObject { ["frameId"] = frame, ["isDefault"] = true } } },
    };

    static Task Send(WebSocket ws, JsonNode n, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(n.ToJsonString()), WebSocketMessageType.Text, true, ct);

    [Fact]
    public async Task A_page_asks_and_gets_the_translation_back()
    {
        var run = await PlayAsync(new Step(null, "The boss fight is far too hard now"));
        var answer = Assert.Single(run.Answers);
        Assert.Contains("window.__babel.receber(", answer);
        Assert.Contains("PT:The boss fight is far too hard now", answer);
        Assert.Contains("Runtime.addBinding", run.Commands);
        Assert.Contains("Page.addScriptToEvaluateOnNewDocument", run.Commands);
        // only the five allowed DevTools commands were ever sent
        Assert.All(run.Commands, m => Assert.Contains(m, Pages.AllowedCommands));
    }

    [Fact]
    public async Task A_tab_that_navigates_to_the_account_page_sends_nothing()
    {
        var run = await PlayAsync(
            new Step(null, "Patch notes for the new season are out"),
            new Step(Navigated("https://store.steampowered.com/account/"), "Wallet balance and purchase history of this account"),
            new Step(Navigated("https://store.steampowered.com/checkout/?purchasetype=self"), "Payment method ending in four digits"));
        Assert.Single(run.Answers);
        var sent = run.Engine.Calls.SelectMany(c => c).ToList();
        Assert.Equal(["Patch notes for the new season are out"], sent);
    }

    [Fact]
    public async Task Coming_back_to_a_public_page_translates_again()
    {
        var run = await PlayAsync(
            new Step(Navigated("https://store.steampowered.com/account/"), "Wallet balance and purchase history of this account"),
            new Step(Navigated("https://store.steampowered.com/news/app/570"), "The new hero is finally available for everyone"));
        Assert.Equal(["The new hero is finally available for everyone"], run.Engine.Calls.SelectMany(c => c));
    }

    [Fact]
    public async Task An_embedded_frame_cannot_ask_for_translations()
    {
        var run = await PlayAsync(new Step(null, "Text from a frame inside the page", ContextId: 8));
        Assert.Empty(run.Engine.Calls);
        Assert.Empty(run.Answers);
    }
}
