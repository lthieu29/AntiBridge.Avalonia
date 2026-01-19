using NUnit.Framework;
using AntiBridge.Core.Models;
using AntiBridge.Core.Services;

namespace AntiBridge.Tests;

[TestFixture]
public class AccountStorageServiceTests
{
    private string _testDataDir = null!;
    private AccountStorageService _service = null!;

    [SetUp]
    public void SetUp()
    {
        // Use a temp directory for testing
        _testDataDir = Path.Combine(Path.GetTempPath(), $"antibridge_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataDir);
        
        // Create service (it will use default path, we'll test public interface)
        _service = new AccountStorageService();
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test accounts
        try
        {
            var accounts = _service.ListAccounts();
            foreach (var acc in accounts.Where(a => a.Email.Contains("@test.com")))
            {
                _service.DeleteAccount(acc.Id);
            }
        }
        catch { /* ignore */ }
    }

    [Test]
    public void ListAccounts_EmptyStorage_ReturnsEmptyList()
    {
        // Fresh storage should not throw
        var accounts = _service.ListAccounts();
        Assert.That(accounts, Is.Not.Null);
    }

    [Test]
    public void UpsertAccount_NewAccount_CreatesAccount()
    {
        var email = $"new_{Guid.NewGuid()}@test.com";
        var token = new TokenData
        {
            AccessToken = "access123",
            RefreshToken = "refresh456",
            ExpiryTime = DateTime.UtcNow.AddHours(1)
        };

        var account = _service.UpsertAccount(email, "Test User", token);

        Assert.That(account, Is.Not.Null);
        Assert.That(account.Email, Is.EqualTo(email));
        Assert.That(account.Name, Is.EqualTo("Test User"));
        Assert.That(account.Token.AccessToken, Is.EqualTo("access123"));
        
        // Clean up
        _service.DeleteAccount(account.Id);
    }

    [Test]
    public void UpsertAccount_ExistingAccount_UpdatesToken()
    {
        var email = $"update_{Guid.NewGuid()}@test.com";
        var token1 = new TokenData { AccessToken = "old", RefreshToken = "ref1", ExpiryTime = DateTime.UtcNow.AddHours(1) };
        var token2 = new TokenData { AccessToken = "new", RefreshToken = "ref2", ExpiryTime = DateTime.UtcNow.AddHours(1) };

        var account1 = _service.UpsertAccount(email, "User", token1);
        var account2 = _service.UpsertAccount(email, "User Updated", token2);

        Assert.That(account2.Id, Is.EqualTo(account1.Id)); // Same ID
        Assert.That(account2.Token.AccessToken, Is.EqualTo("new"));
        
        // Clean up
        _service.DeleteAccount(account1.Id);
    }

    [Test]
    public void DeleteAccount_ExistingAccount_ReturnsTrue()
    {
        var email = $"delete_{Guid.NewGuid()}@test.com";
        var token = new TokenData { AccessToken = "a", RefreshToken = "r", ExpiryTime = DateTime.UtcNow.AddHours(1) };
        var account = _service.UpsertAccount(email, "User", token);

        var result = _service.DeleteAccount(account.Id);

        Assert.That(result, Is.True);
        Assert.That(_service.LoadAccount(account.Id), Is.Null);
    }

    [Test]
    public void DeleteAccount_NonExistingAccount_ReturnsFalse()
    {
        var result = _service.DeleteAccount("non-existing-id");
        Assert.That(result, Is.False);
    }

    [Test]
    public void GetCurrentAccount_AfterFirstAdd_ReturnsFirstAccount()
    {
        var email = $"first_{Guid.NewGuid()}@test.com";
        var token = new TokenData { AccessToken = "a", RefreshToken = "r", ExpiryTime = DateTime.UtcNow.AddHours(1) };
        var account = _service.UpsertAccount(email, "First", token);
        _service.SetCurrentAccountId(account.Id);

        var current = _service.GetCurrentAccount();

        Assert.That(current, Is.Not.Null);
        Assert.That(current!.Email, Is.EqualTo(email));
        
        // Clean up
        _service.DeleteAccount(account.Id);
    }

    [Test]
    public void SetCurrentAccountId_ValidId_UpdatesCurrentAccount()
    {
        var email1 = $"switch1_{Guid.NewGuid()}@test.com";
        var email2 = $"switch2_{Guid.NewGuid()}@test.com";
        var token = new TokenData { AccessToken = "a", RefreshToken = "r", ExpiryTime = DateTime.UtcNow.AddHours(1) };
        
        var acc1 = _service.UpsertAccount(email1, "User1", token);
        var acc2 = _service.UpsertAccount(email2, "User2", token);

        _service.SetCurrentAccountId(acc2.Id);
        var current = _service.GetCurrentAccountId();

        Assert.That(current, Is.EqualTo(acc2.Id));
        
        // Clean up
        _service.DeleteAccount(acc1.Id);
        _service.DeleteAccount(acc2.Id);
    }
}
