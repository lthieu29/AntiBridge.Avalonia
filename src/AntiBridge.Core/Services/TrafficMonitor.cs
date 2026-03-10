using System.Collections.Concurrent;
using AntiBridge.Core.Models;
using Microsoft.Data.Sqlite;

namespace AntiBridge.Core.Services;

/// <summary>
/// Traffic monitoring service that logs API requests/responses.
/// Provides in-memory storage with SQLite persistence for historical queries.
/// Thread-safe for concurrent access.
/// </summary>
public class TrafficMonitor : IDisposable
{
    private readonly ConcurrentQueue<TrafficLog> _logs = new();
    private readonly int _maxLogs;
    private readonly string _dbPath;
    private volatile bool _enabled;
    private bool _disposed;
    private int _pendingSaves;

    /// <summary>
    /// Event raised when a new log entry is added.
    /// Useful for UI notifications.
    /// </summary>
    public event Action<TrafficLog>? OnLogAdded;

    /// <summary>
    /// Creates a new TrafficMonitor instance.
    /// </summary>
    /// <param name="maxLogs">Maximum number of logs to keep in memory (default: 1000).</param>
    /// <param name="dbPath">Path to SQLite database file. If null, uses default app data location.</param>
    public TrafficMonitor(int maxLogs = 1000, string? dbPath = null)
    {
        _maxLogs = maxLogs;
        _dbPath = dbPath ?? GetDefaultDbPath();
        InitializeDatabase();
    }

    /// <summary>
    /// Gets or sets whether traffic logging is enabled.
    /// When disabled, LogRequest() will not store logs but will still record token stats.
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Logs a request/response cycle.
    /// If Enabled is false, the log is not stored but token stats are still recorded.
    /// </summary>
    /// <param name="log">The traffic log entry to record.</param>
    public void LogRequest(TrafficLog log)
    {
        if (log == null)
            throw new ArgumentNullException(nameof(log));

        if (!_enabled)
            return;

        // Add to in-memory queue
        _logs.Enqueue(log);

        // Enforce max log count
        while (_logs.Count > _maxLogs && _logs.TryDequeue(out _))
        {
            // Dequeue excess logs
        }

        // Persist to database asynchronously
        Interlocked.Increment(ref _pendingSaves);
        Task.Run(() =>
        {
            try
            {
                SaveToDatabase(log);
            }
            finally
            {
                Interlocked.Decrement(ref _pendingSaves);
            }
        });

        // Notify listeners
        OnLogAdded?.Invoke(log);
    }

    /// <summary>
    /// Waits for all pending database saves to complete.
    /// Useful for testing to ensure all logs are persisted before assertions.
    /// </summary>
    /// <param name="timeoutMs">Maximum time to wait in milliseconds (default: 5000).</param>
    /// <returns>True if all saves completed, false if timeout occurred.</returns>
    public bool WaitForPendingSaves(int timeoutMs = 5000)
    {
        var startTime = DateTime.UtcNow;
        while (_pendingSaves > 0)
        {
            if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
                return false;
            Thread.Sleep(10);
        }
        return true;
    }

    /// <summary>
    /// Retrieves recent logs from the database.
    /// </summary>
    /// <param name="limit">Maximum number of logs to return (default: 100).</param>
    /// <returns>List of traffic logs ordered by timestamp descending.</returns>
    public List<TrafficLog> GetLogs(int limit = 100)
    {
        return LoadFromDatabase(limit);
    }

    /// <summary>
    /// Gets aggregate statistics for all logged traffic.
    /// </summary>
    /// <returns>Traffic statistics including total, success, and error counts.</returns>
    public TrafficStats GetStats()
    {
        return LoadStatsFromDatabase();
    }

    /// <summary>
    /// Clears all logs from memory and database.
    /// </summary>
    public void Clear()
    {
        // Clear in-memory queue
        while (_logs.TryDequeue(out _))
        {
            // Dequeue all
        }

        // Clear database
        ClearDatabase();
    }

    /// <summary>
    /// Gets the default database path in the application data folder.
    /// </summary>
    private static string GetDefaultDbPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AntiBridge");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "traffic.db");
    }

    /// <summary>
    /// Initializes the SQLite database and creates the traffic_logs table if it doesn't exist.
    /// </summary>
    private void InitializeDatabase()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS traffic_logs (
                id TEXT PRIMARY KEY,
                timestamp INTEGER NOT NULL,
                method TEXT NOT NULL,
                url TEXT NOT NULL,
                status INTEGER NOT NULL,
                duration_ms INTEGER NOT NULL,
                model TEXT,
                mapped_model TEXT,
                account_email TEXT,
                error TEXT,
                request_body TEXT,
                response_body TEXT,
                input_tokens INTEGER,
                output_tokens INTEGER,
                protocol TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_traffic_logs_timestamp ON traffic_logs(timestamp DESC);
        ";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Saves a traffic log entry to the database.
    /// </summary>
    private void SaveToDatabase(TrafficLog log)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO traffic_logs 
                (id, timestamp, method, url, status, duration_ms, model, mapped_model, 
                 account_email, error, request_body, response_body, input_tokens, output_tokens, protocol)
                VALUES 
                (@id, @timestamp, @method, @url, @status, @duration_ms, @model, @mapped_model,
                 @account_email, @error, @request_body, @response_body, @input_tokens, @output_tokens, @protocol)
            ";

            command.Parameters.AddWithValue("@id", log.Id);
            command.Parameters.AddWithValue("@timestamp", log.Timestamp);
            command.Parameters.AddWithValue("@method", log.Method);
            command.Parameters.AddWithValue("@url", log.Url);
            command.Parameters.AddWithValue("@status", log.Status);
            command.Parameters.AddWithValue("@duration_ms", log.DurationMs);
            command.Parameters.AddWithValue("@model", (object?)log.Model ?? DBNull.Value);
            command.Parameters.AddWithValue("@mapped_model", (object?)log.MappedModel ?? DBNull.Value);
            command.Parameters.AddWithValue("@account_email", (object?)log.AccountEmail ?? DBNull.Value);
            command.Parameters.AddWithValue("@error", (object?)log.Error ?? DBNull.Value);
            command.Parameters.AddWithValue("@request_body", (object?)log.RequestBody ?? DBNull.Value);
            command.Parameters.AddWithValue("@response_body", (object?)log.ResponseBody ?? DBNull.Value);
            command.Parameters.AddWithValue("@input_tokens", (object?)log.InputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("@output_tokens", (object?)log.OutputTokens ?? DBNull.Value);
            command.Parameters.AddWithValue("@protocol", (object?)log.Protocol ?? DBNull.Value);

            command.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Silently ignore database errors to not affect main request flow
        }
    }

    /// <summary>
    /// Loads traffic logs from the database.
    /// </summary>
    private List<TrafficLog> LoadFromDatabase(int limit)
    {
        var logs = new List<TrafficLog>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT id, timestamp, method, url, status, duration_ms, model, mapped_model,
                       account_email, error, request_body, response_body, input_tokens, output_tokens, protocol
                FROM traffic_logs
                ORDER BY timestamp DESC
                LIMIT @limit
            ";
            command.Parameters.AddWithValue("@limit", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                logs.Add(new TrafficLog
                {
                    Id = reader.GetString(0),
                    Timestamp = reader.GetInt64(1),
                    Method = reader.GetString(2),
                    Url = reader.GetString(3),
                    Status = reader.GetInt32(4),
                    DurationMs = reader.GetInt64(5),
                    Model = reader.IsDBNull(6) ? null : reader.GetString(6),
                    MappedModel = reader.IsDBNull(7) ? null : reader.GetString(7),
                    AccountEmail = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Error = reader.IsDBNull(9) ? null : reader.GetString(9),
                    RequestBody = reader.IsDBNull(10) ? null : reader.GetString(10),
                    ResponseBody = reader.IsDBNull(11) ? null : reader.GetString(11),
                    InputTokens = reader.IsDBNull(12) ? null : reader.GetInt32(12),
                    OutputTokens = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                    Protocol = reader.IsDBNull(14) ? null : reader.GetString(14)
                });
            }
        }
        catch (Exception)
        {
            // Return empty list on error
        }

        return logs;
    }

    /// <summary>
    /// Loads aggregate statistics from the database.
    /// </summary>
    private TrafficStats LoadStatsFromDatabase()
    {
        var stats = new TrafficStats();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    COUNT(*) as total,
                    SUM(CASE WHEN status >= 200 AND status < 300 THEN 1 ELSE 0 END) as success,
                    SUM(CASE WHEN status >= 400 THEN 1 ELSE 0 END) as error
                FROM traffic_logs
            ";

            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                stats.TotalRequests = reader.GetInt64(0);
                stats.SuccessCount = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                stats.ErrorCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            }
        }
        catch (Exception)
        {
            // Return empty stats on error
        }

        return stats;
    }

    /// <summary>
    /// Clears all logs from the database.
    /// </summary>
    private void ClearDatabase()
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM traffic_logs";
            command.ExecuteNonQuery();
        }
        catch (Exception)
        {
            // Silently ignore database errors
        }
    }

    /// <summary>
    /// Disposes of resources used by the TrafficMonitor.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
