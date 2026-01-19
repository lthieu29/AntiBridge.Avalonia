using NUnit.Framework;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class AntigravityProcessServiceTests
{
    [Test]
    public void Constructor_CreatesInstance()
    {
        var service = new AntigravityProcessService();
        Assert.That(service, Is.Not.Null);
    }

    [Test]
    public void OnStatusChanged_EventCanBeSubscribed()
    {
        var service = new AntigravityProcessService();
        string? receivedMessage = null;

        service.OnStatusChanged += msg => receivedMessage = msg;
        
        // Event subscription should work without error
        Assert.Pass("Event subscription works");
    }

    [Test]
    public void OnError_EventCanBeSubscribed()
    {
        var service = new AntigravityProcessService();
        string? receivedError = null;

        service.OnError += err => receivedError = err;
        
        // Event subscription should work without error
        Assert.Pass("Event subscription works");
    }

    // Note: RestartAntigravity() is not tested because it requires actual process manipulation
    // which is not suitable for unit tests. Integration tests would be needed for that.
}
