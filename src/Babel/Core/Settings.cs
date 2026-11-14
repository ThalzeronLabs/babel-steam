using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Babel.Core;

/// <summary>The settings, in %APPDATA%\Babel\settings.json. Every field has a sensible default.</summary>
public sealed class Settings
{
    /// <summary>The Steam debugging port (only 127.0.0.1 can reach it).</summary>
    public int Port { get; set; } = 9333;

    /// <summary>"auto" picks the newest stable Gemini Flash-Lite at start-up; or a model name.</summary>
    public string Model { get; set; } = "auto";

    /// <summary>Shorter pieces of text are left alone (labels, buttons, names).</summary>
    public int MinWords { get; set; } = 4;

    public int BatchMaxTexts { get; set; } = 40;
    public int BatchMaxChars { get; set; } = 12000;
    public int ParallelRequests { get; set; } = 4;

    /// <summary>Also translate the chat windows. OFF by default: friends' messages are personal data of other people,
    /// and Google's terms ask not to send personal information to the free tier.</summary>
    public bool TranslateChat { get; set; }

    /// <summary>Reopen Steam with the port when it was opened without it (never with a game running). OFF by default.</summary>
    public bool ReopenSteam { get; set; }

    /// <summary>A guard for the whole app, on top of the per-page one: characters sent to Gemini per minute and per day.</summary>
    public int MaxCharsPerMinute { get; set; } = 200_000;
    public int MaxCharsPerDay { get; set; } = 2_000_000;

    /// <summary>What Steam's own start-up entry and Start menu shortcut looked like before Babel added the port.</summary>
    public string? SteamRunBackup { get; set; }
    public string? SteamShortcutBackup { get; set; }

    /// <summary>The person chose "adjust Steam". Uninstalling then also removes the port when there was no copy to
    /// restore (Steam already opened with it before Babel).</summary>
    public bool SteamAdjusted { get; set; }

    public bool SetupDone { get; set; }

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Babel");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception e)
        {
            Log.Write($"settings unreadable, using defaults: {e.Message}");
        }
        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}

/// <summary>A small rotating log in %LOCALAPPDATA%\Babel\babel.log. It never stores page text, translations or the key.</summary>
public static class Log
{
    static readonly object Gate = new();

    /// <summary>The tests point this elsewhere so they never write to the user's log.</summary>
    internal static string? PathOverride { get; set; }

    public static string FilePath => PathOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Babel", "babel.log");

    public static event Action<string>? Written;

    public static void Write(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 2_000_000) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // the log never breaks the app
        }
        Written?.Invoke(line);
    }
}
