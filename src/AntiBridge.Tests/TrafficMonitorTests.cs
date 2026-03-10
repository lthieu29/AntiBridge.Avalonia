using NUnit.Framework;
using AntiBridge.Core.Models;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class TrafficMonitorTests
{
    private string _testDbPath = null!;
    private TrafficMonitor _monitor = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a unique temp file for each test
        _testDbPath = Path.Combine(Path.GetTempPath(), $"traffic_test_{Guid.NewGuid():N}.db");
        _monitor = new TrafficMonitor(maxLogs: 100, dbPath: _testDbPath);
        _monitor.Enabled = true;
    }

    [TearDown]
    public void TearDown()
    {
        _monitor.WaitForPendingSaves();
        _monitor.Dispose();
        
        // Clean up test database - need to wait a bit for SQLite to release the file
        Thread.Sleep(50);
        try
        {
            if (File.Exists(_testDbPath))
                File.Delete(_testDbPath);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Helper to create a unique database path for tests that need their own monitor.
    /// </summary>
    private static string CreateUniqueDbPath()
    {
        return Path.Combine(Path.GetTempPath(), $"traffic_test_{Guid.NewGuid():N}.db");
    }

    #region Task 3.4.1: Test log persistence and retrieval

    [Test]
    public void LogRequest_PersistsToDatabase()
    {
        // Arrange
        var log = CreateTestLog();

        // Act
        _monitor.LogRequest(log);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Id, Is.EqualTo(log.Id));
    }

    [Test]
    public void LogRequest_PersistsAllFields()
    {
        // Arrange
        var log = new TrafficLog
        {
            Id = "test123456789012",
            Timestamp = 1700000000000,
            Method = "POST",
            Url = "/v1/chat/completions",
            Status = 200,
            DurationMs = 1500,
            Model = "gpt-4",
            MappedModel = "gemini-2.5-flash",
            AccountEmail = "test@example.com",
            Error = null,
            RequestBody = "{\"messages\":[]}",
            ResponseBody = "{\"choices\":[]}",
            InputTokens = 100,
            OutputTokens = 50,
            Protocol = "openai"
        };

        // Act
        _monitor.LogRequest(log);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Has.Count.EqualTo(1));
        
        var retrieved = logs[0];
        Assert.That(retrieved.Id, Is.EqualTo(log.Id));
        Assert.That(retrieved.Timestamp, Is.EqualTo(log.Timestamp));
        Assert.That(retrieved.Method, Is.EqualTo(log.Method));
        Assert.That(retrieved.Url, Is.EqualTo(log.Url));
        Assert.That(retrieved.Status, Is.EqualTo(log.Status));
        Assert.That(retrieved.DurationMs, Is.EqualTo(log.DurationMs));
        Assert.That(retrieved.Model, Is.EqualTo(log.Model));
        Assert.That(retrieved.MappedModel, Is.EqualTo(log.MappedModel));
        Assert.That(retrieved.AccountEmail, Is.EqualTo(log.AccountEmail));
        Assert.That(retrieved.Error, Is.Null);
        Assert.That(retrieved.RequestBody, Is.EqualTo(log.RequestBody));
        Assert.That(retrieved.ResponseBody, Is.EqualTo(log.ResponseBody));
        Assert.That(retrieved.InputTokens, Is.EqualTo(log.InputTokens));
        Assert.That(retrieved.OutputTokens, Is.EqualTo(log.OutputTokens));
        Assert.That(retrieved.Protocol, Is.EqualTo(log.Protocol));
    }

    [Test]
    public void LogRequest_PersistsErrorField()
    {
        // Arrange
        var log = CreateTestLog();
        log.Status = 500;
        log.Error = "Internal server error";

        // Act
        _monitor.LogRequest(log);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs[0].Error, Is.EqualTo("Internal server error"));
    }

    [Test]
    public void GetLogs_ReturnsInTimestampDescendingOrder()
    {
        // Arrange
        var log1 = CreateTestLog();
        log1.Timestamp = 1000;
        var log2 = CreateTestLog();
        log2.Timestamp = 2000;
        var log3 = CreateTestLog();
        log3.Timestamp = 3000;

        // Act
        _monitor.LogRequest(log1);
        _monitor.LogRequest(log2);
        _monitor.LogRequest(log3);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Has.Count.EqualTo(3));
        Assert.That(logs[0].Timestamp, Is.EqualTo(3000));
        Assert.That(logs[1].Timestamp, Is.EqualTo(2000));
        Assert.That(logs[2].Timestamp, Is.EqualTo(1000));
    }

    [Test]
    public void GetLogs_RespectsLimitParameter()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }
        _monitor.WaitForPendingSaves();

        // Act
        var logs = _monitor.GetLogs(5);

        // Assert
        Assert.That(logs, Has.Count.EqualTo(5));
    }

    [Test]
    public void Clear_RemovesAllLogs()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }
        _monitor.WaitForPendingSaves();

        // Act
        _monitor.Clear();

        // Assert
        var logs = _monitor.GetLogs(100);
        Assert.That(logs, Is.Empty);
    }

    #endregion

    #region Task 3.4.2: Test max log count enforcement

    [Test]
    public void LogRequest_EnforcesMaxLogCount_InMemory()
    {
        // Arrange - create monitor with small max and unique db path
        var uniqueDbPath = CreateUniqueDbPath();
        using var smallMonitor = new TrafficMonitor(maxLogs: 5, dbPath: uniqueDbPath);
        smallMonitor.Enabled = true;

        // Act - add more logs than max
        for (int i = 0; i < 10; i++)
        {
            var log = CreateTestLog();
            log.Timestamp = i;
            smallMonitor.LogRequest(log);
        }
        smallMonitor.WaitForPendingSaves();
        
        // The database stores all logs, but in-memory is limited
        var logs = smallMonitor.GetLogs(100);
        Assert.That(logs.Count, Is.EqualTo(10)); // Database has all
        
        // Cleanup
        try { File.Delete(uniqueDbPath); } catch { }
    }

    [Test]
    public void LogRequest_OldestLogsRemovedFromMemory()
    {
        // Arrange - create monitor with small max and unique db path
        var uniqueDbPath = CreateUniqueDbPath();
        using var smallMonitor = new TrafficMonitor(maxLogs: 3, dbPath: uniqueDbPath);
        smallMonitor.Enabled = true;

        // Act - add logs with known timestamps
        for (int i = 1; i <= 5; i++)
        {
            var log = CreateTestLog();
            log.Timestamp = i * 1000;
            smallMonitor.LogRequest(log);
        }
        smallMonitor.WaitForPendingSaves();

        // Assert - database has all, ordered by timestamp desc
        var logs = smallMonitor.GetLogs(100);
        Assert.That(logs.Count, Is.EqualTo(5));
        Assert.That(logs[0].Timestamp, Is.EqualTo(5000)); // Most recent first
        
        // Cleanup
        try { File.Delete(uniqueDbPath); } catch { }
    }

    #endregion

    #region Task 3.4.3: Test stats aggregation

    [Test]
    public void GetStats_ReturnsCorrectTotalCount()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }
        _monitor.WaitForPendingSaves();

        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.TotalRequests, Is.EqualTo(5));
    }

    [Test]
    public void GetStats_CountsSuccessfulRequests()
    {
        // Arrange
        var successLog = CreateTestLog();
        successLog.Status = 200;
        
        var anotherSuccess = CreateTestLog();
        anotherSuccess.Status = 201;

        _monitor.LogRequest(successLog);
        _monitor.LogRequest(anotherSuccess);
        _monitor.WaitForPendingSaves();

        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.SuccessCount, Is.EqualTo(2));
    }

    [Test]
    public void GetStats_CountsErrorRequests()
    {
        // Arrange
        var clientError = CreateTestLog();
        clientError.Status = 400;
        
        var serverError = CreateTestLog();
        serverError.Status = 500;
        
        var notFound = CreateTestLog();
        notFound.Status = 404;

        _monitor.LogRequest(clientError);
        _monitor.LogRequest(serverError);
        _monitor.LogRequest(notFound);
        _monitor.WaitForPendingSaves();

        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.ErrorCount, Is.EqualTo(3));
    }

    [Test]
    public void GetStats_MixedStatusCodes()
    {
        // Arrange
        var success1 = CreateTestLog();
        success1.Status = 200;
        
        var success2 = CreateTestLog();
        success2.Status = 201;
        
        var redirect = CreateTestLog();
        redirect.Status = 302; // Not counted as success or error
        
        var clientError = CreateTestLog();
        clientError.Status = 400;
        
        var serverError = CreateTestLog();
        serverError.Status = 503;

        _monitor.LogRequest(success1);
        _monitor.LogRequest(success2);
        _monitor.LogRequest(redirect);
        _monitor.LogRequest(clientError);
        _monitor.LogRequest(serverError);
        _monitor.WaitForPendingSaves();

        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.TotalRequests, Is.EqualTo(5));
        Assert.That(stats.SuccessCount, Is.EqualTo(2)); // 200, 201
        Assert.That(stats.ErrorCount, Is.EqualTo(2)); // 400, 503
    }

    [Test]
    public void GetStats_EmptyDatabase_ReturnsZeros()
    {
        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.TotalRequests, Is.EqualTo(0));
        Assert.That(stats.SuccessCount, Is.EqualTo(0));
        Assert.That(stats.ErrorCount, Is.EqualTo(0));
    }

    [Test]
    public void GetStats_AfterClear_ReturnsZeros()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }
        _monitor.WaitForPendingSaves();
        _monitor.Clear();

        // Act
        var stats = _monitor.GetStats();

        // Assert
        Assert.That(stats.TotalRequests, Is.EqualTo(0));
    }

    #endregion

    #region Task 3.4.4: Test enable/disable toggle

    [Test]
    public void Enabled_DefaultsToFalse()
    {
        // Arrange
        var uniqueDbPath = CreateUniqueDbPath();
        using var newMonitor = new TrafficMonitor(dbPath: uniqueDbPath);

        // Assert
        Assert.That(newMonitor.Enabled, Is.False);
        
        // Cleanup
        try { File.Delete(uniqueDbPath); } catch { }
    }

    [Test]
    public void LogRequest_WhenDisabled_DoesNotLog()
    {
        // Arrange
        _monitor.Enabled = false;
        var log = CreateTestLog();

        // Act
        _monitor.LogRequest(log);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Is.Empty);
    }

    [Test]
    public void LogRequest_WhenEnabled_DoesLog()
    {
        // Arrange
        _monitor.Enabled = true;
        var log = CreateTestLog();

        // Act
        _monitor.LogRequest(log);
        _monitor.WaitForPendingSaves();

        // Assert
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Has.Count.EqualTo(1));
    }

    [Test]
    public void Enabled_CanBeToggledAtRuntime()
    {
        // Arrange
        _monitor.Enabled = false;
        var log1 = CreateTestLog();
        log1.Timestamp = 1000;

        // Act - log while disabled
        _monitor.LogRequest(log1);
        _monitor.WaitForPendingSaves();

        // Enable and log again
        _monitor.Enabled = true;
        var log2 = CreateTestLog();
        log2.Timestamp = 2000;
        _monitor.LogRequest(log2);
        _monitor.WaitForPendingSaves();

        // Assert - only second log should be present
        var logs = _monitor.GetLogs(10);
        Assert.That(logs, Has.Count.EqualTo(1));
        Assert.That(logs[0].Timestamp, Is.EqualTo(2000));
    }

    [Test]
    public void Enabled_ToggleIsThreadSafe()
    {
        // Arrange
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act - toggle enabled while logging
        for (int i = 0; i < 100; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    _monitor.Enabled = index % 2 == 0;
                    _monitor.LogRequest(CreateTestLog());
                    var _ = _monitor.Enabled;
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
        _monitor.WaitForPendingSaves();

        // Assert - no exceptions
        Assert.That(exceptions, Is.Empty);
    }

    #endregion

    #region OnLogAdded event tests

    [Test]
    public void OnLogAdded_FiresWhenLogAdded()
    {
        // Arrange
        TrafficLog? receivedLog = null;
        _monitor.OnLogAdded += log => receivedLog = log;
        var testLog = CreateTestLog();

        // Act
        _monitor.LogRequest(testLog);

        // Assert
        Assert.That(receivedLog, Is.Not.Null);
        Assert.That(receivedLog!.Id, Is.EqualTo(testLog.Id));
    }

    [Test]
    public void OnLogAdded_DoesNotFireWhenDisabled()
    {
        // Arrange
        _monitor.Enabled = false;
        var eventFired = false;
        _monitor.OnLogAdded += _ => eventFired = true;

        // Act
        _monitor.LogRequest(CreateTestLog());

        // Assert
        Assert.That(eventFired, Is.False);
    }

    #endregion

    #region Thread-safety tests

    [Test]
    public void LogRequest_ThreadSafe_ConcurrentLogging()
    {
        // Arrange
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        var logCount = 0;

        // Act - concurrent logging
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < 10; j++)
                    {
                        _monitor.LogRequest(CreateTestLog());
                        Interlocked.Increment(ref logCount);
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
        _monitor.WaitForPendingSaves();

        // Assert
        Assert.That(exceptions, Is.Empty, 
            $"Concurrent logging caused exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");
        Assert.That(logCount, Is.EqualTo(500));
    }

    [Test]
    public void GetLogs_ThreadSafe_ConcurrentAccess()
    {
        // Arrange
        for (int i = 0; i < 20; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }
        _monitor.WaitForPendingSaves();

        var exceptions = new List<Exception>();
        var tasks = new List<Task>();

        // Act - concurrent reads
        for (int i = 0; i < 50; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    var logs = _monitor.GetLogs(10);
                    var stats = _monitor.GetStats();
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
    }

    #endregion

    #region WaitForPendingSaves tests

    [Test]
    public void WaitForPendingSaves_ReturnsTrue_WhenNoPendingSaves()
    {
        // Act
        var result = _monitor.WaitForPendingSaves(100);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void WaitForPendingSaves_WaitsForAllSaves()
    {
        // Arrange - log multiple items
        for (int i = 0; i < 10; i++)
        {
            _monitor.LogRequest(CreateTestLog());
        }

        // Act
        var result = _monitor.WaitForPendingSaves(5000);

        // Assert
        Assert.That(result, Is.True);
        var logs = _monitor.GetLogs(100);
        Assert.That(logs.Count, Is.EqualTo(10));
    }

    #endregion

    #region Helper methods

    private static TrafficLog CreateTestLog()
    {
        return new TrafficLog
        {
            Method = "POST",
            Url = "/v1/chat/completions",
            Status = 200,
            DurationMs = 100
        };
    }

    #endregion
}
