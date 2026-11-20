using Microsoft.Data.Sqlite;
using Serilog;

namespace AudioDimension.Core.Data;

/// <summary>SQLite storage for settings, presets, favorites, projects, recents and export history.</summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string dbFile)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbFile, Pooling = false }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    public void Migrate()
    {
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        Exec(c, """
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS presets (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL,
                is_custom INTEGER NOT NULL DEFAULT 0, json TEXT NOT NULL, created_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS favorites (preset_id TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, path TEXT NOT NULL,
                created_utc TEXT NOT NULL, modified_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS recent_projects (
                path TEXT PRIMARY KEY, name TEXT NOT NULL, opened_utc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS export_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT, file_path TEXT NOT NULL, format TEXT NOT NULL,
                width INTEGER NOT NULL, height INTEGER NOT NULL, fps INTEGER NOT NULL,
                duration_seconds REAL NOT NULL, size_bytes INTEGER NOT NULL,
                status TEXT NOT NULL, created_utc TEXT NOT NULL);
            PRAGMA user_version = 1;
            """);
        Log.Information("Database ready");
    }

    public string? GetSetting(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string value)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public int Count(string table)
    {
        // table names come from internal constants only
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
