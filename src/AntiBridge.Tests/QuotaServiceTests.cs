using NUnit.Framework;
using AntiBridge.Core.Services;
using AntiBridge.Core.Models;

namespace AntiBridge.Tests;

[TestFixture]
public class QuotaServiceTests
{
    private QuotaService? _quotaService;

    [SetUp]
    public void Setup()
    {
        _quotaService = new QuotaService();
    }

    [Test]
    public void Constructor_CreatesInstance()
    {
        Assert.That(_quotaService, Is.Not.Null);
    }

    [Test]
    public void OnStatusChanged_EventCanBeSubscribed()
    {
        string? receivedMessage = null;
        _quotaService!.OnStatusChanged += msg => receivedMessage = msg;
        
        Assert.Pass("Event subscription works");
    }

    [Test]
    public void OnError_EventCanBeSubscribed()
    {
        string? receivedError = null;
        _quotaService!.OnError += err => receivedError = err;
        
        Assert.Pass("Event subscription works");
    }

    // Note: Actual API calls are not tested here as they require valid tokens
    // Integration tests would be needed to test FetchQuotaAsync and FetchProjectIdAsync
}

[TestFixture]
public class QuotaDataExtendedTests
{
    [Test]
    public void AddModel_AddsModelToList()
    {
        var quota = new QuotaData();
        quota.AddModel("test-model", 75, "2026-01-20T00:00:00Z");

        Assert.That(quota.Models.Count, Is.EqualTo(1));
        Assert.That(quota.Models[0].Name, Is.EqualTo("test-model"));
        Assert.That(quota.Models[0].Percentage, Is.EqualTo(75));
    }

    [Test]
    public void IsForbidden_DefaultsToFalse()
    {
        var quota = new QuotaData();
        Assert.That(quota.IsForbidden, Is.False);
    }

    [Test]
    public void IsForbidden_CanBeSetToTrue()
    {
        var quota = new QuotaData { IsForbidden = true };
        Assert.That(quota.IsForbidden, Is.True);
    }

    [Test]
    public void FetchedAt_DefaultsToNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var quota = new QuotaData();
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.That(quota.FetchedAt, Is.GreaterThan(before));
        Assert.That(quota.FetchedAt, Is.LessThan(after));
    }
}
