using System.IO;

namespace Babel.Core;

/// <summary>Starts and stops the translation service: the engine (with the user's key), the cache, the queue and the
/// injector. Restarted when the key changes.</summary>
public sealed class Runtime : IDisposable
{
    public Settings Settings { get; }
    public Injector? Injector { get; private set; }
    public Translator? Translator { get; private set; }
    public Cache? Cache { get; private set; }
    public string? Model { get; private set; }
    public string? Problem { get; private set; }
    public event Action? Changed;

    CancellationTokenSource? cts;

    public Runtime(Settings settings) => Settings = settings;

    public bool Running => Injector != null;

    /// <summary>Starts with the saved key (or the echo engine for tests). Returns false and sets Problem when it cannot.</summary>
    public async Task<bool> StartAsync(bool echo = false)
    {
        Stop();
        Problem = null;
        IEngine engine;
        if (echo)
        {
            engine = new EchoEngine();
        }
        else
        {
            var key = KeyStore.Read();
            if (key == null)
            {
                Problem = "no-key";
                Changed?.Invoke();
                return false;
            }
            try
            {
                var g = await GeminiEngine.CreateAsync(key, Settings.Model);
                Model = g.Model;
                engine = g;
            }
            catch (GeminiException e)
            {
                Problem = e.Status is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden
                    ? "invalid-key" : $"http-{(int)e.Status}";
                Log.Write($"Gemini refused at start-up: {(int)e.Status}");
                Changed?.Invoke();
                return false;
            }
            catch (Exception e)
            {
                Problem = "network";
                Log.Write($"cannot reach Gemini: {e.Message}");
                Changed?.Invoke();
                return false;
            }
        }
        Directory.CreateDirectory(Settings.Folder);
        var version = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{engine.Name}\0{Prompt.Instructions()}")))[..16];
        Cache = new Cache(Path.Combine(Settings.Folder, "cache.sqlite"), version);
        Translator = new Translator(engine, Cache, Settings);
        Injector = new Injector(Settings, Translator);
        Injector.Changed += () => Changed?.Invoke();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        _ = Task.Run(() => Injector.RunAsync(token), token);
        Log.Write($"Babel started: engine {engine.Name}, port {Settings.Port}, {Cache.Count()} texts in the cache");
        Changed?.Invoke();
        return true;
    }

    public void Stop()
    {
        cts?.Cancel();
        cts = null;
        Injector = null;
        Translator = null;
        Cache?.Dispose();
        Cache = null;
        Changed?.Invoke();
    }

    public void Dispose() => Stop();
}
