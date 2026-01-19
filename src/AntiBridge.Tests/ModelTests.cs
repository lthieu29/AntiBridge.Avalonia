using NUnit.Framework;
using AntiBridge.Core.Models;

namespace AntiBridge.Tests;

[TestFixture]
public class TokenDataTests
{
    [Test]
    public void IsExpired_FutureExpiry_ReturnsFalse()
    {
        var token = new TokenData
        {
            AccessToken = "test",
            RefreshToken = "refresh",
            ExpiryTime = DateTime.UtcNow.AddHours(1)
        };

        Assert.That(token.IsExpired, Is.False);
    }

    [Test]
    public void IsExpired_PastExpiry_ReturnsTrue()
    {
        var token = new TokenData
        {
            AccessToken = "test",
            RefreshToken = "refresh",
            ExpiryTime = DateTime.UtcNow.AddHours(-1)
        };

        Assert.That(token.IsExpired, Is.True);
    }

    [Test]
    public void IsExpired_ExpiresWithinBuffer_ReturnsTrue()
    {
        // Token expires in 2 minutes, but we consider it expired with 5 min buffer
        var token = new TokenData
        {
            AccessToken = "test",
            RefreshToken = "refresh",
            ExpiryTime = DateTime.UtcNow.AddMinutes(2)
        };

        Assert.That(token.IsExpired, Is.True);
    }
}

[TestFixture]
public class AccountTests
{
    [Test]
    public void NewAccount_HasDefaultValues()
    {
        var account = new Account();

        Assert.That(account.Id, Is.EqualTo(string.Empty));
        Assert.That(account.Email, Is.EqualTo(string.Empty));
        Assert.That(account.Token, Is.Not.Null);
        Assert.That(account.CreatedAt, Is.GreaterThan(0));
    }

    [Test]
    public void UpdateLastUsed_UpdatesTimestamp()
    {
        var account = new Account();
        var original = account.LastUsed;

        Thread.Sleep(1100); // Wait more than 1 second
        account.UpdateLastUsed();

        Assert.That(account.LastUsed, Is.GreaterThan(original));
    }
}

[TestFixture]
public class QuotaDataTests
{
    [Test]
    public void ModelQuota_DefaultValues()
    {
        var quota = new ModelQuota
        {
            Name = "gemini-2.5-pro",
            Percentage = 75
        };

        Assert.That(quota.Name, Is.EqualTo("gemini-2.5-pro"));
        Assert.That(quota.Percentage, Is.EqualTo(75));
    }

    [Test]
    public void QuotaData_EmptyModels_IsEmpty()
    {
        var quota = new QuotaData();

        Assert.That(quota.Models, Is.Empty);
    }
}
