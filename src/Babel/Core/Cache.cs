using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Babel.Core;

public record Item(string T, string L);

/// <summary>
/// Every translated text is stored with a SHA-256 of the original. Opening the same patch note again costs nothing:
/// it never reaches Gemini. The version (the model and the instructions) is part of the key, so a new glossary or
/// prompt never shows a translation made with the old rules.
/// </summary>
public sealed class Cache : IDisposable
{
    readonly SqliteConnection db;
    readonly object gate = new();
    public string Version { get; }

    public Cache(string path, string version)
    {
        Version = version;
        db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        Exec("PRAGMA journal_mode=WAL");
        Exec("CREATE TABLE IF NOT EXISTS traducao (chave TEXT PRIMARY KEY, texto TEXT NOT NULL, idioma TEXT NOT NULL, criado INTEGER NOT NULL)");
    }

    void Exec(string sql)
    {
        using var c = db.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public string Key(string source) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{Version}\0{source}")));

    public Item? Read(string source)
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT texto, idioma FROM traducao WHERE chave = $k";
            c.Parameters.AddWithValue("$k", Key(source));
            using var r = c.ExecuteReader();
            return r.Read() ? new Item(r.GetString(0), r.GetString(1)) : null;
        }
    }

    public void Write(IEnumerable<(string source, Item item)> pairs)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (gate)
        {
            using var tx = db.BeginTransaction();
            using var c = db.CreateCommand();
            c.Transaction = tx;
            c.CommandText = "INSERT OR REPLACE INTO traducao VALUES ($k, $t, $l, $c)";
            var k = c.Parameters.Add("$k", SqliteType.Text);
            var t = c.Parameters.Add("$t", SqliteType.Text);
            var l = c.Parameters.Add("$l", SqliteType.Text);
            c.Parameters.AddWithValue("$c", now);
            foreach (var (source, item) in pairs)
            {
                k.Value = Key(source);
                t.Value = item.T;
                l.Value = item.L;
                c.ExecuteNonQuery();
            }
            tx.Commit();
        }
    }

    public long Count()
    {
        lock (gate)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM traducao";
            return (long)c.ExecuteScalar()!;
        }
    }

    public void Dispose()
    {
        lock (gate) db.Dispose();
    }
}
