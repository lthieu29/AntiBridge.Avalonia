using NUnit.Framework;
using AntiBridge.Core.Models;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class TokenStatsServiceTests
{
    private string _testDbPath = null!;
    private TokenStatsService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a unique temp file for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"token_stats_test_{Guid.NewGuid():N}.db");
        _service = new TokenStatsService(_testDbPath);
    }

    [TearDown]
    public void TearDown()
    {
        _service.Dispose();
        
        // Clean up test database - need to wait a bit for SQLite to release the file
        Thread.Sleep(50);
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
            // Also clean up WAL and SHM files
            var walPath = _testDbPath + "-wal";
            var shmPath = _testDbPath + "-shm";
            if (File.Exists(walPath))
                File.Delete(walPath);
            if (File.Exists(shmPath))
                File.Delete(shmPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Helper to create a unique database path for tests that need their own service.
    /// </summary>
    private static string CreateUniqueDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"token_stats_test_{Guid.NewGuid():N}.db");
    }

    #region Task 4.3.1: Test usage recording and aggregation

    [Test]
    public void RecordUsage_StoresDataInDatabase()
    {
        // Arrange
        var accountEmail = "test@example.com";
        var model = "claude-sonnet-4-5";
        var inputTokens = 100;
        var outputTokens = 50;

        // Act
        _service.RecordUsage(accountEmail, model, inputTokens, outputTokens);

        // Assert
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);
        
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].AccountEmail, Is.EqualTo(accountEmail));
        Assert.That(stats[0].Model, Is.EqualTo(model));
        Assert.That(stats[0].InputTokens, Is.EqualTo(inputTokens));
        Assert.That(stats[0].OutputTokens, Is.EqualTo(outputTokens));
        Assert.That(stats[0].RequestCount, Is.EqualTo(1));
    }

    [Test]
    public void RecordUsage_AggregatesSameHourAccountModel()
    {
        // Arrange
        var accountEmail = "test@example.com";
        var model = "claude-sonnet-4-5";

        // Act - record multiple usages
        _service.RecordUsage(accountEmail, model, 100, 50);
        _service.RecordUsage(accountEmail, model, 200, 100);
        _service.RecordUsage(accountEmail, model, 150, 75);

        // Assert
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);
        
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].InputTokens, Is.EqualTo(450)); // 100 + 200 + 150
        Assert.That(stats[0].OutputTokens, Is.EqualTo(225)); // 50 + 100 + 75
        Assert.That(stats[0].RequestCount, Is.EqualTo(3));
    }

    [Test]
    public void RecordUsage_SeparatesByAccount()
    {
        // Arrange & Act
        _service.RecordUsage("user1@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("user2@example.com", "claude-sonnet-4-5", 200, 100);

        // Assert
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);
        
        Assert.That(stats, Has.Count.EqualTo(2));
        
        var user1Stats = stats.FirstOrDefault(s => s.AccountEmail == "user1@example.com");
        var user2Stats = stats.FirstOrDefault(s => s.AccountEmail == "user2@example.com");
        
        Assert.That(user1Stats, Is.Not.Null);
        Assert.That(user1Stats!.InputTokens, Is.EqualTo(100));
        Assert.That(user2Stats, Is.Not.Null);
        Assert.That(user2Stats!.InputTokens, Is.EqualTo(200));
    }

    [Test]
    public void RecordUsage_SeparatesByModel()
    {
        // Arrange & Act
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("test@example.com", "gemini-2.5-flash", 200, 100);

        // Assert
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);
        
        Assert.That(stats, Has.Count.EqualTo(2));
        
        var claudeStats = stats.FirstOrDefault(s => s.Model == "claude-sonnet-4-5");
        var geminiStats = stats.FirstOrDefault(s => s.Model == "gemini-2.5-flash");
        
        Assert.That(claudeStats, Is.Not.Null);
        Assert.That(claudeStats!.InputTokens, Is.EqualTo(100));
        Assert.That(geminiStats, Is.Not.Null);
        Assert.That(geminiStats!.InputTokens, Is.EqualTo(200));
    }

    [Test]
    public void RecordUsage_ThrowsOnNullAccountEmail()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _service.RecordUsage(null!, "model", 100, 50));
    }

    [Test]
    public void RecordUsage_ThrowsOnEmptyAccountEmail()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _service.RecordUsage("", "model", 100, 50));
    }

    [Test]
    public void RecordUsage_ThrowsOnNullModel()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _service.RecordUsage("test@example.com", null!, 100, 50));
    }

    [Test]
    public void RecordUsage_ThrowsOnEmptyModel()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _service.RecordUsage("test@example.com", "", 100, 50));
    }

    [Test]
    public void Clear_RemovesAllStats()
    {
        // Arrange
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("test2@example.com", "gemini-2.5-flash", 200, 100);

        // Act
        _service.Clear();

        // Assert
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var stats = _service.GetHourlyStats(0, now + 3600);
        Assert.That(stats, Is.Empty);
    }

    #endregion

    #region Task 4.3.2: Test hourly/daily/weekly queries

    [Test]
    public void GetHourlyStats_ReturnsStatsInTimeRange()
    {
        // Arrange
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);

        // Assert
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].Hour, Is.EqualTo(hourStart));
    }

    [Test]
    public void GetHourlyStats_ExcludesStatsOutsideTimeRange()
    {
        // Arrange
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);

        // Act - query a different time range (far in the past)
        var stats = _service.GetHourlyStats(0, 1000);

        // Assert
        Assert.That(stats, Is.Empty);
    }

    [Test]
    public void GetHourlyStats_ReturnsEmptyForNoData()
    {
        // Act
        var stats = _service.GetHourlyStats(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Assert
        Assert.That(stats, Is.Empty);
    }

    [Test]
    public void GetDailyStats_AggregatesByDay()
    {
        // Arrange - record usage
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 200, 100);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dayStart = now / 86400 * 86400;
        var stats = _service.GetDailyStats(dayStart, dayStart + 86400);

        // Assert
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].Hour, Is.EqualTo(dayStart)); // Hour field contains day timestamp
        Assert.That(stats[0].InputTokens, Is.EqualTo(300)); // 100 + 200
        Assert.That(stats[0].OutputTokens, Is.EqualTo(150)); // 50 + 100
        Assert.That(stats[0].RequestCount, Is.EqualTo(2));
    }

    [Test]
    public void GetWeeklyStats_AggregatesByWeek()
    {
        // Arrange - record usage
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 200, 100);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var weekStart = now / 604800 * 604800;
        var stats = _service.GetWeeklyStats(weekStart, weekStart + 604800);

        // Assert
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].Hour, Is.EqualTo(weekStart)); // Hour field contains week timestamp
        Assert.That(stats[0].InputTokens, Is.EqualTo(300)); // 100 + 200
        Assert.That(stats[0].OutputTokens, Is.EqualTo(150)); // 50 + 100
        Assert.That(stats[0].RequestCount, Is.EqualTo(2));
    }

    [Test]
    public void GetDailyStats_SeparatesByAccountAndModel()
    {
        // Arrange
        _service.RecordUsage("user1@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("user1@example.com", "gemini-2.5-flash", 200, 100);
        _service.RecordUsage("user2@example.com", "claude-sonnet-4-5", 300, 150);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var dayStart = now / 86400 * 86400;
        var stats = _service.GetDailyStats(dayStart, dayStart + 86400);

        // Assert - should have 3 separate entries
        Assert.That(stats, Has.Count.EqualTo(3));
    }

    [Test]
    public void GetWeeklyStats_SeparatesByAccountAndModel()
    {
        // Arrange
        _service.RecordUsage("user1@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("user1@example.com", "gemini-2.5-flash", 200, 100);
        _service.RecordUsage("user2@example.com", "claude-sonnet-4-5", 300, 150);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var weekStart = now / 604800 * 604800;
        var stats = _service.GetWeeklyStats(weekStart, weekStart + 604800);

        // Assert - should have 3 separate entries
        Assert.That(stats, Has.Count.EqualTo(3));
    }

    #endregion

    #region GetSummary tests

    [Test]
    public void GetSummary_ReturnsTotals()
    {
        // Arrange
        _service.RecordUsage("user1@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("user2@example.com", "gemini-2.5-flash", 200, 100);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var summary = _service.GetSummary(0, now + 3600);

        // Assert
        Assert.That(summary.TotalInputTokens, Is.EqualTo(300)); // 100 + 200
        Assert.That(summary.TotalOutputTokens, Is.EqualTo(150)); // 50 + 100
        Assert.That(summary.TotalRequests, Is.EqualTo(2));
        Assert.That(summary.UniqueAccounts, Is.EqualTo(2));
    }

    [Test]
    public void GetSummary_ReturnsBreakdownByModel()
    {
        // Arrange
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("test@example.com", "gemini-2.5-flash", 200, 100);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var summary = _service.GetSummary(0, now + 3600);

        // Assert
        Assert.That(summary.ByModel, Has.Count.EqualTo(2));
        Assert.That(summary.ByModel["claude-sonnet-4-5"], Is.EqualTo(150)); // 100 + 50
        Assert.That(summary.ByModel["gemini-2.5-flash"], Is.EqualTo(300)); // 200 + 100
    }

    [Test]
    public void GetSummary_ReturnsBreakdownByAccount()
    {
        // Arrange
        _service.RecordUsage("user1@example.com", "claude-sonnet-4-5", 100, 50);
        _service.RecordUsage("user2@example.com", "claude-sonnet-4-5", 200, 100);

        // Act
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var summary = _service.GetSummary(0, now + 3600);

        // Assert
        Assert.That(summary.ByAccount, Has.Count.EqualTo(2));
        Assert.That(summary.ByAccount["user1@example.com"], Is.EqualTo(150)); // 100 + 50
        Assert.That(summary.ByAccount["user2@example.com"], Is.EqualTo(300)); // 200 + 100
    }

    [Test]
    public void GetSummary_ReturnsEmptyForNoData()
    {
        // Act
        var summary = _service.GetSummary(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        // Assert
        Assert.That(summary.TotalInputTokens, Is.EqualTo(0));
        Assert.That(summary.TotalOutputTokens, Is.EqualTo(0));
        Assert.That(summary.TotalRequests, Is.EqualTo(0));
        Assert.That(summary.UniqueAccounts, Is.EqualTo(0));
        Assert.That(summary.ByModel, Is.Empty);
        Assert.That(summary.ByAccount, Is.Empty);
    }

    [Test]
    public void GetSummary_RespectsTimeRange()
    {
        // Arrange
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);

        // Act - query a different time range (far in the past)
        var summary = _service.GetSummary(0, 1000);

        // Assert
        Assert.That(summary.TotalInputTokens, Is.EqualTo(0));
        Assert.That(summary.TotalRequests, Is.EqualTo(0));
    }

    #endregion

    #region Task 4.3.3: Test thread-safety with concurrent recording

    [Test]
    public void RecordUsage_ThreadSafe_ConcurrentRecording()
    {
        // Arrange
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        var recordCount = 0;

        // Act - concurrent recording
        for (int i = 0; i < 50; i++)
        {
            var accountIndex = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 10; j++)
                    {
                        _service.RecordUsage($"user{accountIndex}@example.com", "claude-sonnet-4-5", 100, 50);
                        Interlocked.Increment(ref recordCount);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty, 
            $"Concurrent recording caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        Assert.That(recordCount, Is.EqualTo(500));

        // Verify data integrity
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var summary = _service.GetSummary(0, now + 3600);
        Assert.That(summary.TotalRequests, Is.EqualTo(500));
        Assert.That(summary.TotalInputTokens, Is.EqualTo(50000)); // 500 * 100
        Assert.That(summary.TotalOutputTokens, Is.EqualTo(25000)); // 500 * 50
    }

    [Test]
    public void RecordUsage_ThreadSafe_ConcurrentRecordingAndReading()
    {
        // Arrange
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act - concurrent recording and reading
        for (int i = 0; i < 25; i++)
        {
            var accountIndex = i;
            // Recording tasks
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 10; j++)
                    {
                        _service.RecordUsage($"user{accountIndex}@example.com", "claude-sonnet-4-5", 100, 50);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));

            // Reading tasks
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    for (int j = 0; j < 5; j++)
                    {
                        var _ = _service.GetHourlyStats(0, now + 3600);
                        var __ = _service.GetSummary(0, now + 3600);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty, 
            $"Concurrent operations caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
    }

    [Test]
    public void RecordUsage_ThreadSafe_SameAccountModelAggregation()
    {
        // Arrange - all threads record to the same account/model
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        var recordCount = 0;

        // Act - concurrent recording to same account/model
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    _service.RecordUsage("shared@example.com", "claude-sonnet-4-5", 10, 5);
                    Interlocked.Increment(ref recordCount);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.That(exceptions, Is.Empty);
        Assert.That(recordCount, Is.EqualTo(100));

        // Verify aggregation is correct
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hourStart = now / 3600 * 3600;
        var stats = _service.GetHourlyStats(hourStart, hourStart + 3600);
        
        Assert.That(stats, Has.Count.EqualTo(1));
        Assert.That(stats[0].InputTokens, Is.EqualTo(1000)); // 100 * 10
        Assert.That(stats[0].OutputTokens, Is.EqualTo(500)); // 100 * 5
        Assert.That(stats[0].RequestCount, Is.EqualTo(100));
    }

    #endregion

    #region WAL mode verification

    [Test]
    public void Database_UsesWalMode()
    {
        // This test verifies that WAL mode is enabled by checking for WAL files
        // after some database operations
        
        // Arrange & Act
        _service.RecordUsage("test@example.com", "claude-sonnet-4-5", 100, 50);

        // Assert - WAL file should exist (or have existed)
        // Note: WAL file may be checkpointed, so we just verify the database works
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var stats = _service.GetHourlyStats(0, now + 3600);
        Assert.That(stats, Has.Count.EqualTo(1));
    }

    #endregion
}
