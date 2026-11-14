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

    /// <summary>Also translate the Steam chat windows (the messages go to Google like everything else).</summary>
    public bool TranslateChat { get; set; } = true;

    /// <summary>What Steam's own start-up entry and Start menu shortcut looked like before Babel added the port.</summary>
    public string? SteamRunBackup { get; set; }
    public string? SteamShortcutBackup { get; set; }

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

/// <summary>A small rotating log in %LOCALAPPDATA%\Babel\babel.log. It never stores translated text or the key.</summary>
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
