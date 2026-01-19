using System.Data;
using Microsoft.Data.Sqlite;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Service for reading and writing tokens to Antigravity IDE's SQLite database.
/// Ported from Antigravity-Manager db.rs and migration.rs
/// </summary>
public class AntigravityDbService
{
    private const string StateKey = "jetskiStateSync.agentManagerInitState";
    private const string OnboardingKey = "antigravityOnboarding";

    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    /// <summary>
    /// Get the path to Antigravity's state database
    /// </summary>
    public string? GetDbPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Linux
        if (OperatingSystem.IsLinux())
        {
            var linuxPath = Path.Combine(home, ".config", "Antigravity", "User", "globalStorage", "state.vscdb");
            if (File.Exists(linuxPath)) return linuxPath;
        }
        // macOS
        else if (OperatingSystem.IsMacOS())
        {
            var macPath = Path.Combine(home, "Library", "Application Support", "Antigravity", "User", "globalStorage", "state.vscdb");
            if (File.Exists(macPath)) return macPath;
        }
        // Windows
        else if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var winPath = Path.Combine(appData, "Antigravity", "User", "globalStorage", "state.vscdb");
            if (File.Exists(winPath)) return winPath;
        }

        return null;
    }

    /// <summary>
    /// Check if Antigravity is installed
    /// </summary>
    public bool IsAntigravityInstalled() => GetDbPath() != null;

    /// <summary>
    /// Read the current token from Antigravity's database
    /// </summary>
    public TokenData? ReadTokenFromAntigravity()
    {
        var dbPath = GetDbPath();
        if (dbPath == null)
        {
            OnError?.Invoke("Antigravity database not found");
            return null;
        }

        try
        {
            OnStatusChanged?.Invoke("Reading from Antigravity database...");

            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = @key";
            command.Parameters.AddWithValue("@key", StateKey);

            var result = command.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result))
            {
                OnError?.Invoke("No login data found in Antigravity");
                return null;
            }

            // Base64 decode
            var blob = Convert.FromBase64String(result);

            // Find Field 6 (OAuth data)
            var oauthData = ProtobufHelper.FindField(blob, 6);
            if (oauthData == null)
            {
                OnError?.Invoke("No OAuth data in Antigravity state");
                return null;
            }

            // Extract fields:
            // Field 1 = access_token
            // Field 3 = refresh_token
            var accessTokenBytes = ProtobufHelper.FindField(oauthData, 1);
            var refreshTokenBytes = ProtobufHelper.FindField(oauthData, 3);

            if (refreshTokenBytes == null)
            {
                OnError?.Invoke("No refresh token found");
                return null;
            }

            var accessToken = accessTokenBytes != null 
                ? System.Text.Encoding.UTF8.GetString(accessTokenBytes) 
                : string.Empty;
            var refreshToken = System.Text.Encoding.UTF8.GetString(refreshTokenBytes);

            OnStatusChanged?.Invoke("Successfully read token from Antigravity");

            return new TokenData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiryTime = DateTime.UtcNow.AddHours(1) // Default, will be refreshed
            };
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to read from Antigravity: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Inject token into Antigravity's database
    /// </summary>
    public bool InjectTokenToAntigravity(TokenData token)
    {
        var dbPath = GetDbPath();
        if (dbPath == null)
        {
            OnError?.Invoke("Antigravity database not found");
            return false;
        }

        try
        {
            OnStatusChanged?.Invoke("Injecting token into Antigravity...");

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            // 1. Read current state
            string? currentData;
            using (var readCmd = connection.CreateCommand())
            {
                readCmd.CommandText = "SELECT value FROM ItemTable WHERE key = @key";
                readCmd.Parameters.AddWithValue("@key", StateKey);
                currentData = readCmd.ExecuteScalar() as string;
            }

            if (string.IsNullOrEmpty(currentData))
            {
                OnError?.Invoke("No existing state in Antigravity - please login to Antigravity first");
                return false;
            }

            // 2. Base64 decode
            var blob = Convert.FromBase64String(currentData);

            // 3. Remove old Field 6
            var cleanData = ProtobufHelper.RemoveField(blob, 6);

            // 4. Create new Field 6 with our token
            var expiryTimestamp = new DateTimeOffset(token.ExpiryTime).ToUnixTimeSeconds();
            var newField = ProtobufHelper.CreateOAuthField(
                token.AccessToken,
                token.RefreshToken,
                expiryTimestamp
            );

            // 5. Combine and encode
            var finalData = new byte[cleanData.Length + newField.Length];
            Array.Copy(cleanData, 0, finalData, 0, cleanData.Length);
            Array.Copy(newField, 0, finalData, cleanData.Length, newField.Length);
            var finalBase64 = Convert.ToBase64String(finalData);

            // 6. Write back to database
            using (var writeCmd = connection.CreateCommand())
            {
                writeCmd.CommandText = "UPDATE ItemTable SET value = @value WHERE key = @key";
                writeCmd.Parameters.AddWithValue("@value", finalBase64);
                writeCmd.Parameters.AddWithValue("@key", StateKey);
                writeCmd.ExecuteNonQuery();
            }

            // 7. Set onboarding flag
            using (var onboardCmd = connection.CreateCommand())
            {
                onboardCmd.CommandText = "INSERT OR REPLACE INTO ItemTable (key, value) VALUES (@key, @value)";
                onboardCmd.Parameters.AddWithValue("@key", OnboardingKey);
                onboardCmd.Parameters.AddWithValue("@value", "true");
                onboardCmd.ExecuteNonQuery();
            }

            OnStatusChanged?.Invoke("Token successfully injected into Antigravity!");
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to inject token: {ex.Message}");
            return false;
        }
    }
}
