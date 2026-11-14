namespace Babel.Core;

/// <summary>
/// The translation queue. Requests from every Steam page are joined into batches (one call for many texts), the
/// cache is checked first, and the same text asked by two pages at once goes to Gemini only once.
/// </summary>
public sealed class Translator(IEngine engine, Cache cache, Settings settings)
{
    readonly object gate = new();
    readonly Dictionary<string, TaskCompletionSource<Item?>> inFlight = new();
    readonly List<string> queue = [];
    readonly SemaphoreSlim slots = new(Math.Max(1, settings.ParallelRequests));
    bool scheduled;

    public long FromCache, Translated, Failed;
    public IEngine Engine => engine;

    public async Task<Item?[]> TranslateAsync(IReadOnlyList<string> texts)
    {
        var tasks = new Task<Item?>[texts.Count];
        lock (gate)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                var text = texts[i];
                if (cache.Read(text) is { } ready)
                {
                    Interlocked.Increment(ref FromCache);
                    tasks[i] = Task.FromResult<Item?>(ready);
                }
                else if (inFlight.TryGetValue(text, out var waiting))
                {
                    tasks[i] = waiting.Task;
                }
                else
                {
                    var tcs = new TaskCompletionSource<Item?>(TaskCreationOptions.RunContinuationsAsynchronously);
                    inFlight[text] = tcs;
                    queue.Add(text);
                    tasks[i] = tcs.Task;
                }
            }
            if (queue.Count > 0 && !scheduled)
            {
                // Wait 50 ms to join requests that arrive almost together into one batch.
                scheduled = true;
                _ = Task.Delay(50).ContinueWith(_ => Dispatch(), TaskScheduler.Default);
            }
        }
        return await Task.WhenAll(tasks);
    }

    void Dispatch()
    {
        var batches = new List<List<string>>();
        lock (gate)
        {
            scheduled = false;
            while (queue.Count > 0)
            {
                var batch = new List<string>();
                int size = 0;
                while (queue.Count > 0 && batch.Count < settings.BatchMaxTexts
                       && (batch.Count == 0 || size + queue[0].Length <= settings.BatchMaxChars))
                {
                    batch.Add(queue[0]);
                    size += queue[0].Length;
                    queue.RemoveAt(0);
                }
                batches.Add(batch);
            }
        }
        foreach (var b in batches) _ = RunAsync(b);
    }

    async Task RunAsync(List<string> batch)
    {
        IReadOnlyList<Item> result;
        try
        {
            await slots.WaitAsync();
            try
            {
                result = await engine.TranslateAsync(batch);
            }
            finally
            {
                slots.Release();
            }
        }
        catch (Exception e)
        {
            if (batch.Count > 1)
            {
                // One problematic text (the filter, a malformed answer) must not sink the whole batch: split in
                // half until the culprit is alone.
                Log.Write($"batch of {batch.Count} failed ({e.Message}); splitting");
                int half = batch.Count / 2;
                await Task.WhenAll(RunAsync(batch[..half]), RunAsync(batch[half..]));
                return;
            }
            Log.Write($"one text left untranslated ({e.Message})");
            Interlocked.Increment(ref Failed);
            Deliver(batch[0], null);
            return;
        }
        cache.Write(batch.Zip(result, (s, i) => (s, i)));
        Interlocked.Add(ref Translated, batch.Count);
        for (int i = 0; i < batch.Count; i++) Deliver(batch[i], result[i]);
    }

    void Deliver(string text, Item? item)
    {
        TaskCompletionSource<Item?>? tcs;
        lock (gate)
        {
            if (inFlight.Remove(text, out tcs)) { }
        }
        tcs?.TrySetResult(item);
    }
}
