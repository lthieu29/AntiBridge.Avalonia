using AntiBridge.Core.Models;
using Microsoft.Data.Sqlite;

namespace AntiBridge.Core.Services;

/// <summary>
/// Service for tracking and aggregating token usage statistics.
/// Provides hourly, daily, and weekly aggregation with SQLite persistence.
/// Thread-safe for concurrent recording and querying.
/// </summary>
public class TokenStatsService : IDisposable
{
    private static readonly Lazy<TokenStatsService> _instance = new(() => new TokenStatsService());
    
    /// <summary>
    /// Gets the singleton instance of the TokenStatsService.
    /// </summary>
    public static TokenStatsService Instance => _instance.Value;

    private readonly string _dbPath;
    private readonly object _writeLock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new TokenStatsService instance with the default database path.
    /// Use the static Instance property for the singleton instance.
    /// </summary>
    private TokenStatsService() : this(null)
    {
    }

    /// <summary>
    /// Creates a new TokenStatsService instance with a custom database path.
    /// This constructor is internal for testing purposes.
    /// </summary>
    /// <param name="dbPath">Path to the SQLite database file. If null, uses default app data location.</param>
    internal TokenStatsService(string? dbPath)
    {
        _dbPath = dbPath ?? GetDefaultDbPath();
        InitializeDatabase();
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
        return Path.Combine(appDataPath, "token_stats.db");
    }

    /// <summary>
    /// Initializes the SQLite database with WAL mode and creates the hourly_stats table.
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

        // Enable WAL mode for better concurrent access
        using (var walCommand = connection.CreateCommand())
        {
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            walCommand.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS hourly_stats (
                hour INTEGER NOT NULL,
                account_email TEXT NOT NULL,
                model TEXT NOT NULL,
                input_tokens INTEGER NOT NULL DEFAULT 0,
                output_tokens INTEGER NOT NULL DEFAULT 0,
                request_count INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (hour, account_email, model)
            );
            CREATE INDEX IF NOT EXISTS idx_hourly_stats_hour ON hourly_stats(hour);
            CREATE INDEX IF NOT EXISTS idx_hourly_stats_account ON hourly_stats(account_email);
            CREATE INDEX IF NOT EXISTS idx_hourly_stats_model ON hourly_stats(model);
        ";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Records token usage for a request.
    /// Aggregates data by hour, account, and model using UPSERT.
    /// Thread-safe with write lock.
    /// </summary>
    /// <param name="accountEmail">The account email associated with the request.</param>
    /// <param name="model">The model name used for the request.</param>
    /// <param name="inputTokens">Number of input tokens consumed.</param>
    /// <param name="outputTokens">Number of output tokens generated.</param>
    public void RecordUsage(string accountEmail, string model, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrEmpty(accountEmail))
            throw new ArgumentNullException(nameof(accountEmail));
        if (string.IsNullOrEmpty(model))
            throw new ArgumentNullException(nameof(model));

        var hour = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 3600 * 3600;

        lock (_writeLock)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                // UPSERT: Insert or update with aggregation
                command.CommandText = @"
                    INSERT INTO hourly_stats (hour, account_email, model, input_tokens, output_tokens, request_count)
                    VALUES (@hour, @account_email, @model, @input_tokens, @output_tokens, 1)
                    ON CONFLICT(hour, account_email, model) DO UPDATE SET
                        input_tokens = input_tokens + @input_tokens,
                        output_tokens = output_tokens + @output_tokens,
                        request_count = request_count + 1
                ";

                command.Parameters.AddWithValue("@hour", hour);
                command.Parameters.AddWithValue("@account_email", accountEmail);
                command.Parameters.AddWithValue("@model", model);
                command.Parameters.AddWithValue("@input_tokens", inputTokens);
                command.Parameters.AddWithValue("@output_tokens", outputTokens);

                command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                // Silently ignore database errors to not affect main request flow
            }
        }
    }

    /// <summary>
    /// Gets hourly token statistics for a time range.
    /// </summary>
    /// <param name="startTime">Start of the time range (Unix timestamp in seconds).</param>
    /// <param name="endTime">End of the time range (Unix timestamp in seconds).</param>
    /// <returns>List of hourly statistics within the time range.</returns>
    public List<HourlyTokenStats> GetHourlyStats(long startTime, long endTime)
    {
        var stats = new List<HourlyTokenStats>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT hour, account_email, model, input_tokens, output_tokens, request_count
                FROM hourly_stats
                WHERE hour >= @start_time AND hour <= @end_time
                ORDER BY hour DESC
            ";
            command.Parameters.AddWithValue("@start_time", startTime);
            command.Parameters.AddWithValue("@end_time", endTime);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stats.Add(new HourlyTokenStats
                {
                    Hour = reader.GetInt64(0),
                    AccountEmail = reader.GetString(1),
                    Model = reader.GetString(2),
                    InputTokens = reader.GetInt64(3),
                    OutputTokens = reader.GetInt64(4),
                    RequestCount = reader.GetInt32(5)
                });
            }
        }
        catch (Exception)
        {
            // Return empty list on error
        }

        return stats;
    }

    /// <summary>
    /// Gets daily aggregated token statistics for a time range.
    /// Aggregates hourly data into daily buckets.
    /// </summary>
    /// <param name="startTime">Start of the time range (Unix timestamp in seconds).</param>
    /// <param name="endTime">End of the time range (Unix timestamp in seconds).</param>
    /// <returns>List of daily statistics within the time range.</returns>
    public List<HourlyTokenStats> GetDailyStats(long startTime, long endTime)
    {
        var stats = new List<HourlyTokenStats>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            // Aggregate by day (86400 seconds = 24 hours)
            command.CommandText = @"
                SELECT 
                    (hour / 86400) * 86400 as day,
                    account_email,
                    model,
                    SUM(input_tokens) as input_tokens,
                    SUM(output_tokens) as output_tokens,
                    SUM(request_count) as request_count
                FROM hourly_stats
                WHERE hour >= @start_time AND hour <= @end_time
                GROUP BY day, account_email, model
                ORDER BY day DESC
            ";
            command.Parameters.AddWithValue("@start_time", startTime);
            command.Parameters.AddWithValue("@end_time", endTime);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stats.Add(new HourlyTokenStats
                {
                    Hour = reader.GetInt64(0), // Using Hour field for day timestamp
                    AccountEmail = reader.GetString(1),
                    Model = reader.GetString(2),
                    InputTokens = reader.GetInt64(3),
                    OutputTokens = reader.GetInt64(4),
                    RequestCount = reader.GetInt32(5)
                });
            }
        }
        catch (Exception)
        {
            // Return empty list on error
        }

        return stats;
    }

    /// <summary>
    /// Gets weekly aggregated token statistics for a time range.
    /// Aggregates hourly data into weekly buckets.
    /// </summary>
    /// <param name="startTime">Start of the time range (Unix timestamp in seconds).</param>
    /// <param name="endTime">End of the time range (Unix timestamp in seconds).</param>
    /// <returns>List of weekly statistics within the time range.</returns>
    public List<HourlyTokenStats> GetWeeklyStats(long startTime, long endTime)
    {
        var stats = new List<HourlyTokenStats>();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            // Aggregate by week (604800 seconds = 7 days)
            command.CommandText = @"
                SELECT 
                    (hour / 604800) * 604800 as week,
                    account_email,
                    model,
                    SUM(input_tokens) as input_tokens,
                    SUM(output_tokens) as output_tokens,
                    SUM(request_count) as request_count
                FROM hourly_stats
                WHERE hour >= @start_time AND hour <= @end_time
                GROUP BY week, account_email, model
                ORDER BY week DESC
            ";
            command.Parameters.AddWithValue("@start_time", startTime);
            command.Parameters.AddWithValue("@end_time", endTime);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                stats.Add(new HourlyTokenStats
                {
                    Hour = reader.GetInt64(0), // Using Hour field for week timestamp
                    AccountEmail = reader.GetString(1),
                    Model = reader.GetString(2),
                    InputTokens = reader.GetInt64(3),
                    OutputTokens = reader.GetInt64(4),
                    RequestCount = reader.GetInt32(5)
                });
            }
        }
        catch (Exception)
        {
            // Return empty list on error
        }

        return stats;
    }

    /// <summary>
    /// Gets a summary of token statistics for a time range.
    /// Includes totals and breakdowns by model and account.
    /// </summary>
    /// <param name="startTime">Start of the time range (Unix timestamp in seconds).</param>
    /// <param name="endTime">End of the time range (Unix timestamp in seconds).</param>
    /// <returns>Summary statistics for the time range.</returns>
    public TokenStatsSummary GetSummary(long startTime, long endTime)
    {
        var summary = new TokenStatsSummary();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();

            // Get totals
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT 
                        COALESCE(SUM(input_tokens), 0) as total_input,
                        COALESCE(SUM(output_tokens), 0) as total_output,
                        COALESCE(SUM(request_count), 0) as total_requests,
                        COUNT(DISTINCT account_email) as unique_accounts
                    FROM hourly_stats
                    WHERE hour >= @start_time AND hour <= @end_time
                ";
                command.Parameters.AddWithValue("@start_time", startTime);
                command.Parameters.AddWithValue("@end_time", endTime);

                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    summary.TotalInputTokens = reader.GetInt64(0);
                    summary.TotalOutputTokens = reader.GetInt64(1);
                    summary.TotalRequests = reader.GetInt64(2);
                    summary.UniqueAccounts = reader.GetInt32(3);
                }
            }

            // Get breakdown by model
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT 
                        model,
                        SUM(input_tokens) + SUM(output_tokens) as total_tokens
                    FROM hourly_stats
                    WHERE hour >= @start_time AND hour <= @end_time
                    GROUP BY model
                    ORDER BY total_tokens DESC
                ";
                command.Parameters.AddWithValue("@start_time", startTime);
                command.Parameters.AddWithValue("@end_time", endTime);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    summary.ByModel[reader.GetString(0)] = reader.GetInt64(1);
                }
            }

            // Get breakdown by account
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT 
                        account_email,
                        SUM(input_tokens) + SUM(output_tokens) as total_tokens
                    FROM hourly_stats
                    WHERE hour >= @start_time AND hour <= @end_time
                    GROUP BY account_email
                    ORDER BY total_tokens DESC
                ";
                command.Parameters.AddWithValue("@start_time", startTime);
                command.Parameters.AddWithValue("@end_time", endTime);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    summary.ByAccount[reader.GetString(0)] = reader.GetInt64(1);
                }
            }
        }
        catch (Exception)
        {
            // Return empty summary on error
        }

        return summary;
    }

    /// <summary>
    /// Clears all token statistics from the database.
    /// </summary>
    public void Clear()
    {
        lock (_writeLock)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                connection.Open();

                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM hourly_stats";
                command.ExecuteNonQuery();
            }
            catch (Exception)
            {
                // Silently ignore database errors
            }
        }
    }

    /// <summary>
    /// Disposes of resources used by the TokenStatsService.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
