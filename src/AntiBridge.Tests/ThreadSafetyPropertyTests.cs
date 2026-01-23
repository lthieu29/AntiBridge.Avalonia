using System.Collections.Concurrent;
using AntiBridge.Core.Models;
using AntiBridge.Core.Services;
using FsCheck;
using NUnit.Framework;
using PropertyAttribute = FsCheck.NUnit.PropertyAttribute;

namespace AntiBridge.Tests;

/// <summary>
/// Property-based tests for thread-safety of concurrent services.
/// Feature: antibridge-feature-port
/// </summary>
[TestFixture]
public class ThreadSafetyPropertyTests
{
    #region Property 9.3.1: Concurrent ModelRouter access doesn't corrupt state

    /// <summary>
    /// Property: Concurrent SetCustomMapping and ResolveModel operations don't corrupt state.
    /// **Validates: Requirements 9.3.1**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ModelRouter_ConcurrentAccess_NoStateCorruption()
    {
        return Prop.ForAll(
            Gen.Choose(10, 50).ToArbitrary(), // operation count
            Gen.Choose(2, 8).ToArbitrary(),   // thread count
            (opCount, threadCount) =>
            {
                var router = new ModelRouter();
                var exceptions = new ConcurrentBag<Exception>();
                var mappingsSet = new ConcurrentDictionary<string, string>();

                var tasks = new List<Task>();
                for (int t = 0; t < threadCount; t++)
                {
                    var threadId = t;
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            for (int i = 0; i < opCount; i++)
                            {
                                var pattern = $"model-{threadId}-{i}";
                                var target = $"target-{threadId}-{i}";

                                // Mix of operations
                                if (i % 3 == 0)
                                {
                                    router.SetCustomMapping(pattern, target);
                                    mappingsSet[pattern] = target;
                                }
                                else if (i % 3 == 1)
                                {
                                    router.ResolveModel(pattern);
                                }
                                else
                                {
                                    router.GetCustomMappings();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());

                // Verify no exceptions occurred
                var noExceptions = exceptions.IsEmpty;

                // Verify all set mappings are retrievable
                var allMappingsValid = true;
                foreach (var kvp in mappingsSet)
                {
                    var resolved = router.ResolveModel(kvp.Key);
                    if (resolved != kvp.Value)
                    {
                        allMappingsValid = false;
                        break;
                    }
                }

                return (noExceptions && allMappingsValid)
                    .Label($"Exceptions: {exceptions.Count}, Mappings valid: {allMappingsValid}")
                    .Classify(threadCount <= 4, "few threads (2-4)")
                    .Classify(threadCount > 4, "many threads (5-8)");
            });
    }

    /// <summary>
    /// Property: Concurrent RemoveCustomMapping operations don't corrupt state.
    /// **Validates: Requirements 9.3.1**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property ModelRouter_ConcurrentRemove_NoStateCorruption()
    {
        return Prop.ForAll(
            Gen.Choose(20, 100).ToArbitrary(), // mapping count
            Gen.Choose(2, 6).ToArbitrary(),    // thread count
            (mappingCount, threadCount) =>
            {
                var router = new ModelRouter();
                var exceptions = new ConcurrentBag<Exception>();

                // Pre-populate mappings
                for (int i = 0; i < mappingCount; i++)
                {
                    router.SetCustomMapping($"pattern-{i}", $"target-{i}");
                }

                var tasks = new List<Task>();
                for (int t = 0; t < threadCount; t++)
                {
                    var threadId = t;
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            for (int i = threadId; i < mappingCount; i += threadCount)
                            {
                                router.RemoveCustomMapping($"pattern-{i}");
                            }
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }));
                }

                Task.WaitAll(tasks.ToArray());

                var noExceptions = exceptions.IsEmpty;
                var mappingsEmpty = router.GetCustomMappings().Count == 0;

                return (noExceptions && mappingsEmpty)
                    .Label($"Exceptions: {exceptions.Count}, Remaining mappings: {router.GetCustomMappings().Count}")
                    .Classify(mappingCount <= 50, "few mappings")
                    .Classify(mappingCount > 50, "many mappings");
            });
    }

    #endregion

    #region Property 9.3.2: Concurrent TrafficMonitor logging doesn't lose data

    /// <summary>
    /// Property: Concurrent LogRequest operations don't lose data.
    /// **Validates: Requirements 9.3.2**
    /// </summary>
    [Property(MaxTest = 30)]
    public Property TrafficMonitor_ConcurrentLogging_NoDataLoss()
    {
        return Prop.ForAll(
            Gen.Choose(10, 30).ToArbitrary(), // logs per thread
            Gen.Choose(2, 4).ToArbitrary(),   // thread count
            (logsPerThread, threadCount) =>
            {
                var dbPath = Path.Combine(Path.GetTempPath(), $"traffic_test_{Guid.NewGuid()}.db");
                var monitor = new TrafficMonitor(1000, dbPath);
                monitor.Enabled = true;

                var exceptions = new ConcurrentBag<Exception>();
                var expectedLogCount = logsPerThread * threadCount;

                try
                {
                    var tasks = new List<Task>();
                    for (int t = 0; t < threadCount; t++)
                    {
                        var threadId = t;
                        tasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                for (int i = 0; i < logsPerThread; i++)
                                {
                                    var log = new TrafficLog
                                    {
                                        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                        Method = "POST",
                                        Url = $"/api/test/{threadId}/{i}",
                                        Status = 200,
                                        DurationMs = 100 + i,
                                        Model = $"model-{threadId}",
                                        MappedModel = $"mapped-{threadId}",
                                        AccountEmail = $"user{threadId}@test.com",
                                        InputTokens = 100,
                                        OutputTokens = 50,
                                        Protocol = "anthropic"
                                    };
                                    monitor.LogRequest(log);
                                }
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        }));
                    }

                    Task.WaitAll(tasks.ToArray());

                    // Wait for async operations to complete
                    monitor.WaitForPendingSaves(5000);

                    var logs = monitor.GetLogs(expectedLogCount + 10);
                    var noExceptions = exceptions.IsEmpty;
                    var noDataLoss = logs.Count >= expectedLogCount;

                    return (noExceptions && noDataLoss)
                        .Label($"Exceptions: {exceptions.Count}, Expected: {expectedLogCount}, Actual: {logs.Count}")
                        .Classify(logs.Count == expectedLogCount, "exact count")
                        .Classify(logs.Count > expectedLogCount, "extra logs (OK)")
                        .Classify(logs.Count < expectedLogCount, "data loss (BUG!)");
                }
                finally
                {
                    monitor.Dispose();
                    try { File.Delete(dbPath); } catch { }
                }
            });
    }

    #endregion

    #region Property 9.3.3: Concurrent TokenStatsService recording is accurate

    /// <summary>
    /// Property: Concurrent RecordUsage operations produce accurate totals.
    /// **Validates: Requirements 9.3.3**
    /// </summary>
    [Property(MaxTest = 30)]
    public Property TokenStatsService_ConcurrentRecording_AccurateTotals()
    {
        return Prop.ForAll(
            Gen.Choose(10, 30).ToArbitrary(), // records per thread
            Gen.Choose(2, 4).ToArbitrary(),   // thread count
            (recordsPerThread, threadCount) =>
            {
                var dbPath = Path.Combine(Path.GetTempPath(), $"token_stats_test_{Guid.NewGuid()}.db");
                var service = new TokenStatsService(dbPath);

                var exceptions = new ConcurrentBag<Exception>();
                const int tokensPerRecord = 100;
                var expectedInputTokens = (long)recordsPerThread * threadCount * tokensPerRecord;
                var expectedOutputTokens = (long)recordsPerThread * threadCount * (tokensPerRecord / 2);

                try
                {
                    var tasks = new List<Task>();
                    for (int t = 0; t < threadCount; t++)
                    {
                        var threadId = t;
                        tasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                for (int i = 0; i < recordsPerThread; i++)
                                {
                                    service.RecordUsage(
                                        $"user{threadId}@test.com",
                                        "test-model",
                                        tokensPerRecord,
                                        tokensPerRecord / 2
                                    );
                                }
                            }
                            catch (Exception ex)
                            {
                                exceptions.Add(ex);
                            }
                        }));
                    }

                    Task.WaitAll(tasks.ToArray());

                    // Get summary for all time
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    var summary = service.GetSummary(0, now + 3600);
                    var noExceptions = exceptions.IsEmpty;
                    var accurateTotals = summary.TotalInputTokens == expectedInputTokens &&
                                        summary.TotalOutputTokens == expectedOutputTokens;

                    return (noExceptions && accurateTotals)
                        .Label($"Exceptions: {exceptions.Count}, Expected input: {expectedInputTokens}, Actual: {summary.TotalInputTokens}")
                        .Classify(accurateTotals, "accurate totals")
                        .Classify(!accurateTotals, "inaccurate totals (BUG!)");
                }
                finally
                {
                    service.Dispose();
                    try { File.Delete(dbPath); } catch { }
                }
            });
    }

    #endregion
}
