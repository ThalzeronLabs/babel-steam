using System.IO;
using Microsoft.Win32;

namespace Babel.Core;

/// <summary>
/// Installing is three small, reversible steps, all for the current Windows user (no administrator):
/// 1. Babel goes to %LOCALAPPDATA%\Programs\Babel, with a Start menu shortcut and, if chosen, starts with Windows;
/// 2. Steam's own start-up entry gets the debugging port, so it already opens ready to translate;
/// 3. Steam's Start menu shortcut gets it too.
/// What Steam had before is saved in the settings, and uninstalling puts it back.
/// </summary>
public static class Install
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "BabelParaSteam";

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Babel");

    public static string Exe => Path.Combine(Folder, "Babel.exe");

    static string StartMenu => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    public static string MenuShortcut => Path.Combine(StartMenu, "Babel para Steam.lnk");

    static string SteamShortcut => Path.Combine(StartMenu, "Steam", "Steam.lnk");

    public static bool RunningFromInstallFolder =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);

    public static bool StartsWithWindows
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) != null;
        }
    }

    /// <summary>Copies Babel to its folder (when run from Downloads, for example), and creates the shortcuts.</summary>
    public static void Apply(Settings s, bool startWithWindows, bool adjustSteam)
    {
        Directory.CreateDirectory(Folder);
        if (!RunningFromInstallFolder && Environment.ProcessPath is { } me)
        {
            File.Copy(me, Exe, overwrite: true);
            Log.Write($"installed to {Folder}");
        }
        Shortcut(MenuShortcut, Exe, "", "Babel para Steam: traduz a Steam para o português do Brasil");
        SetStartWithWindows(startWithWindows);
        if (adjustSteam) AdjustSteam(s);
        s.SetupDone = true;
        s.Save();
    }

    public static void SetStartWithWindows(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunName, $"\"{Exe}\" --tray");
        else k.DeleteValue(RunName, false);
    }

    /// <summary>Adds the debugging port to Steam's start-up entry and Start menu shortcut, keeping a copy of each.</summary>
    public static void AdjustSteam(Settings s)
    {
        string port = string.Join(' ', Steam.LaunchArgs(s.Port));
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (k.GetValue("Steam") is string run && !run.Contains("devtools-port"))
            {
                s.SteamRunBackup = run;
                k.SetValue("Steam", $"{run} {port}");
                Log.Write("Steam's start-up entry now opens with the port");
            }
        }
        if (File.Exists(SteamShortcut))
        {
            var args = ShortcutArgs(SteamShortcut) ?? "";
            if (!args.Contains("devtools-port"))
            {
                s.SteamShortcutBackup = args;
                SetShortcutArgs(SteamShortcut, $"{args} {port}".Trim());
                Log.Write("Steam's Start menu shortcut now opens with the port");
            }
        }
        s.Save();
    }

    /// <summary>Puts Steam back as it was and removes Babel's shortcuts. The settings, the key and the cache stay
    /// in %APPDATA%\Babel unless removeData is true.</summary>
    public static void Uninstall(Settings s, bool removeData)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (s.SteamRunBackup != null) k.SetValue("Steam", s.SteamRunBackup);
            k.DeleteValue(RunName, false);
        }
        if (s.SteamShortcutBackup != null && File.Exists(SteamShortcut)) SetShortcutArgs(SteamShortcut, s.SteamShortcutBackup);
        if (File.Exists(MenuShortcut)) File.Delete(MenuShortcut);
        s.SteamRunBackup = s.SteamShortcutBackup = null;
        s.SetupDone = false;
        if (removeData)
        {
            KeyStore.Delete();
            try
            {
                Directory.Delete(Settings.Folder, recursive: true);
            }
            catch (IOException)
            {
                // the cache may still be open; it is removed next time
            }
        }
        else s.Save();
        Log.Write("uninstalled");
    }

    // Windows shortcuts (.lnk) through the Windows Script Host, which every Windows has.
    static dynamic Shell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;

    static void Shortcut(string path, string target, string args, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var l = Shell().CreateShortcut(path);
        l.TargetPath = target;
        l.Arguments = args;
        l.WorkingDirectory = Path.GetDirectoryName(target);
        l.Description = description;
        l.IconLocation = target + ",0";
        l.Save();
    }

    static string? ShortcutArgs(string path) => (string?)Shell().CreateShortcut(path).Arguments;

    static void SetShortcutArgs(string path, string args)
    {
        var l = Shell().CreateShortcut(path);
        l.Arguments = args;
        l.Save();
    }
}
