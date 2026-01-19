using NUnit.Framework;
using AntiBridge.Avalonia.ViewModels;
using AntiBridge.Core.Models;

namespace AntiBridge.Tests;

[TestFixture]
public class AccountRowViewModelTests
{
    [Test]
    public void Constructor_SetsBasicProperties()
    {
        // Arrange
        var account = CreateTestAccount("test@example.com", "Test User");

        // Act
        var vm = new AccountRowViewModel(account, 0);

        // Assert
        Assert.That(vm.Id, Is.EqualTo(account.Id));
        Assert.That(vm.Email, Is.EqualTo("test@example.com"));
        Assert.That(vm.Name, Is.EqualTo("Test User"));
    }

    [Test]
    public void Constructor_AlternatesRowBackground()
    {
        var account = CreateTestAccount("test@example.com", "Test User");

        var vm0 = new AccountRowViewModel(account, 0);
        var vm1 = new AccountRowViewModel(account, 1);
        var vm2 = new AccountRowViewModel(account, 2);

        // Even and odd rows should have different backgrounds
        Assert.That(vm0.RowBackground.ToString(), Is.EqualTo(vm2.RowBackground.ToString()));
        Assert.That(vm0.RowBackground.ToString(), Is.Not.EqualTo(vm1.RowBackground.ToString()));
    }

    [Test]
    public void UpdateFromQuota_SetsIsForbidden_WhenQuotaIsForbidden()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        var quota = new QuotaData { IsForbidden = true };
        vm.UpdateFromQuota(quota);

        Assert.That(vm.IsForbidden, Is.True);
    }

    [Test]
    public void UpdateFromQuota_SetsGeminiPercent_WhenGeminiModelExists()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        var quota = new QuotaData();
        quota.AddModel("gemini-3-pro-high", 75, "2026-01-20T00:00:00Z");
        vm.UpdateFromQuota(quota);

        Assert.That(vm.Gemini3ProPercent, Is.EqualTo(75));
        Assert.That(vm.Gemini3ProText, Is.EqualTo("75%"));
    }

    [Test]
    public void UpdateFromQuota_SetsClaude45Percent_WhenClaudeModelExists()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        var quota = new QuotaData();
        quota.AddModel("claude-opus-4 5-thinking", 90, "2026-01-20T00:00:00Z");
        vm.UpdateFromQuota(quota);

        Assert.That(vm.Claude45Percent, Is.EqualTo(90));
        Assert.That(vm.Claude45Text, Is.EqualTo("90%"));
    }

    [Test]
    public void UpdateLastSync_FormatsDateCorrectly()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        // Unix timestamp for 2026-01-19 10:30:00 UTC
        var timestamp = 1768816200L;
        vm.UpdateLastSync(timestamp);

        Assert.That(vm.LastSyncDate, Does.Contain("2026"));
        Assert.That(vm.LastSyncTime, Is.Not.Empty);
    }

    [Test]
    public void UpdateLastSync_ShowsDash_WhenTimestampIsZero()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        vm.UpdateLastSync(0);

        Assert.That(vm.LastSyncDate, Is.EqualTo("-"));
        Assert.That(vm.LastSyncTime, Is.Empty);
    }

    [Test]
    public void ForbiddenBadge_ReturnsCorrectText()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        Assert.That(vm.ForbiddenBadge, Is.EqualTo("🔒 403"));
    }

    [Test]
    public void ForbiddenTooltip_ReturnsVietnameseText()
    {
        var account = CreateTestAccount("test@example.com", "Test User");
        var vm = new AccountRowViewModel(account, 0);

        Assert.That(vm.ForbiddenTooltip, Does.Contain("403 Forbidden"));
    }

    private static Account CreateTestAccount(string email, string name)
    {
        return new Account
        {
            Id = Guid.NewGuid().ToString(),
            Email = email,
            Name = name,
            Token = new TokenData
            {
                AccessToken = "test_access_token",
                RefreshToken = "test_refresh_token",
                ExpiresIn = 3600
            },
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
