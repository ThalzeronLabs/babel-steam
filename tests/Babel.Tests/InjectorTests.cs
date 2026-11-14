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
/// The whole path without Steam: a fake "Steam" on 127.0.0.1 serves the page list and one page over the DevTools
/// protocol. The page receives the script, asks for a translation through the binding, and must get the answer back.
/// </summary>
public sealed class InjectorTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "babel-inj-" + Guid.NewGuid().ToString("N"));

    public InjectorTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    [Fact]
    public async Task A_page_asks_and_gets_the_translation_back()
    {
        int port = FreePort();
        var http = new HttpListener();
        http.Prefixes.Add($"http://127.0.0.1:{port}/");
        http.Start();
        var received = new List<string>();
        var answered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        _ = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await http.GetContextAsync(); } catch { return; }
                if (ctx.Request.IsWebSocketRequest)
                {
                    var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
                    _ = FakePageAsync(ws, received, answered, stop.Token);
                    continue;
                }
                var list = $$"""
                    [{"id":"ctx","type":"page","title":"SharedJSContext","url":"https://steamloopback.host/index.html","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/ctx"},
                     {"id":"p1","type":"page","title":"Patch notes","url":"https://store.steampowered.com/news/","webSocketDebuggerUrl":"ws://127.0.0.1:{{port}}/p1"}]
                    """;
                var bytes = Encoding.UTF8.GetBytes(list);
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });

        using var cache = new Cache(Path.Combine(dir, "c.sqlite"), "v1");
        var settings = new Settings { Port = port };
        var injector = new Injector(settings, new Translator(new EchoEngine(), cache, settings));
        _ = injector.RunAsync(stop.Token);

        var answer = await answered.Task.WaitAsync(stop.Token);
        stop.Cancel();
        http.Stop();

        Assert.Contains("Runtime.addBinding", received);
        Assert.Contains("Page.addScriptToEvaluateOnNewDocument", received);
        Assert.Contains("window.__babel.receber(", answer);
        Assert.Contains("[PT] The boss fight is far too hard now", Regex(answer));
        Assert.DoesNotContain("SharedJSContext-injected", received); // the hidden context never gets the script
    }

    // the answer is plain-ASCII JSON inside a JS call; decode the escapes to compare the text
    static string Regex(string s) => System.Text.RegularExpressions.Regex.Unescape(s);

    static async Task FakePageAsync(WebSocket ws, List<string> received, TaskCompletionSource<string> answered, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var whole = new MemoryStream();
        bool asked = false;
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
            lock (received) received.Add(method);
            var expr = (string?)msg["params"]?["expression"];
            if (method == "Runtime.evaluate" && expr != null && expr.Contains("receber("))
            {
                answered.TrySetResult(expr);
                continue;
            }
            await Send(ws, new JsonObject { ["id"] = (int)msg["id"]!, ["result"] = new JsonObject() }, ct);
            if (method == "Runtime.evaluate" && !asked)
            {
                // the script is in the page: now the page asks for a translation, like pagina.js does
                asked = true;
                var payload = new JsonObject { ["tipo"] = "traduzir", ["id"] = 1, ["textos"] = new JsonArray("The boss fight is far too hard now") };
                await Send(ws, new JsonObject
                {
                    ["method"] = "Runtime.bindingCalled",
                    ["params"] = new JsonObject { ["name"] = "__babelSend", ["payload"] = payload.ToJsonString(), ["executionContextId"] = 7 },
                }, ct);
            }
        }
    }

    static Task Send(WebSocket ws, JsonNode n, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(n.ToJsonString()), WebSocketMessageType.Text, true, ct);
}
