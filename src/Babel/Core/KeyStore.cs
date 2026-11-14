using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Babel.Core;

/// <summary>
/// The Gemini key, encrypted with Windows DPAPI: only this Windows user, on this PC, can decrypt the file.
/// Copied to another PC or read by another user, it is useless. The key never goes to the log or the screen.
/// </summary>
public static class KeyStore
{
    public static string FilePath => Path.Combine(Settings.Folder, "gemini.key");

    static readonly byte[] Entropy = "Babel para Steam / chave do Gemini"u8.ToArray();

    public static void Save(string key)
    {
        Directory.CreateDirectory(Settings.Folder);
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(FilePath, data);
    }

    /// <summary>The key, from the GEMINI_API_KEY environment variable or the encrypted file; null if there is none.</summary>
    public static string? Read()
    {
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        if (!File.Exists(FilePath)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            Log.Write("the saved key cannot be opened (another Windows user or another PC)");
            return null;
        }
    }

    public static void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
    }

    /// <summary>The last four characters, to show which key is saved without showing the key.</summary>
    public static string? Hint(string? key) => key is { Length: > 8 } ? "…" + key[^4..] : null;

    /// <summary>Google AI Studio keys start with "AIza" and have 39 characters.</summary>
    public static bool LooksValid(string? key) =>
        key != null && key.Trim() is { Length: >= 30 } k && k.StartsWith("AIza", StringComparison.Ordinal) && !k.Any(char.IsWhiteSpace);
}
