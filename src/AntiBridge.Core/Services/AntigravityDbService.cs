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
    private const string NewOAuthKey = "antigravityUnifiedStateSync.oauthToken";
    private const string OnboardingKey = "antigravityOnboarding";

    private readonly AntigravityVersionService _versionService;

    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    public AntigravityDbService()
    {
        _versionService = new AntigravityVersionService();
    }

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
    /// Read the current token from Antigravity's database.
    /// Tries New_Format first, falls back to Old_Format.
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

            // Try New_Format first (Antigravity >= 1.16.5)
            var result = ReadNewFormat(connection);
            if (result != null)
            {
                OnStatusChanged?.Invoke("Successfully read token from Antigravity (New_Format)");
                return result;
            }

            // Fallback to Old_Format (Antigravity < 1.16.5)
            result = ReadOldFormat(connection);
            if (result != null)
            {
                OnStatusChanged?.Invoke("Successfully read token from Antigravity (Old_Format)");
                return result;
            }

            OnError?.Invoke("No login data found in Antigravity");
            return null;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to read from Antigravity: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Read token using New_Format (Antigravity >= 1.16.5).
    /// Reads from key "antigravityUnifiedStateSync.oauthToken" and decodes nested protobuf structure.
    /// OuterMessage → InnerMessage (Field 1) → InnerMessage2 (Field 2) → base64 string (Field 1) → OAuthTokenInfo
    /// </summary>
    private TokenData? ReadNewFormat(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = @key";
            command.Parameters.AddWithValue("@key", NewOAuthKey);

            var result = command.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result))
                return null;

            // Base64 decode → OuterMessage
            var outerMessage = Convert.FromBase64String(result);

            // OuterMessage → InnerMessage (Field 1, length-delimited)
            var innerMessage = ProtobufHelper.FindField(outerMessage, 1);
            if (innerMessage == null)
                return null;

            // InnerMessage → InnerMessage2 (Field 2, length-delimited)
            var innerMessage2 = ProtobufHelper.FindField(innerMessage, 2);
            if (innerMessage2 == null)
                return null;

            // InnerMessage2 → base64 string (Field 1, string)
            var base64Bytes = ProtobufHelper.FindField(innerMessage2, 1);
            if (base64Bytes == null)
                return null;

            var base64String = System.Text.Encoding.UTF8.GetString(base64Bytes);

            // Base64 decode → OAuthTokenInfo bytes
            var oauthInfoBytes = Convert.FromBase64String(base64String);

            // Extract access_token (Field 1) and refresh_token (Field 3)
            var accessTokenBytes = ProtobufHelper.FindField(oauthInfoBytes, 1);
            var refreshTokenBytes = ProtobufHelper.FindField(oauthInfoBytes, 3);

            if (refreshTokenBytes == null)
                return null;

            var accessToken = accessTokenBytes != null
                ? System.Text.Encoding.UTF8.GetString(accessTokenBytes)
                : string.Empty;
            var refreshToken = System.Text.Encoding.UTF8.GetString(refreshTokenBytes);

            return new TokenData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiryTime = DateTime.UtcNow.AddHours(1) // Default, will be refreshed
            };
        }
        catch
        {
            // New_Format read failed, return null to allow fallback
            return null;
        }
    }

    /// <summary>
    /// Read token using Old_Format (Antigravity &lt; 1.16.5).
    /// Reads from key "jetskiStateSync.agentManagerInitState" and extracts OAuth data from Field 6.
    /// </summary>
    private TokenData? ReadOldFormat(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = @key";
            command.Parameters.AddWithValue("@key", StateKey);

            var result = command.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(result))
                return null;

            // Base64 decode
            var blob = Convert.FromBase64String(result);

            // Find Field 6 (OAuth data)
            var oauthData = ProtobufHelper.FindField(blob, 6);
            if (oauthData == null)
                return null;

            // Extract fields:
            // Field 1 = access_token
            // Field 3 = refresh_token
            var accessTokenBytes = ProtobufHelper.FindField(oauthData, 1);
            var refreshTokenBytes = ProtobufHelper.FindField(oauthData, 3);

            if (refreshTokenBytes == null)
                return null;

            var accessToken = accessTokenBytes != null
                ? System.Text.Encoding.UTF8.GetString(accessTokenBytes)
                : string.Empty;
            var refreshToken = System.Text.Encoding.UTF8.GetString(refreshTokenBytes);

            return new TokenData
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiryTime = DateTime.UtcNow.AddHours(1) // Default, will be refreshed
            };
        }
        catch
        {
            // Old_Format read failed, return null
            return null;
        }
    }


    /// <summary>
    /// Inject token into Antigravity's database.
    /// Branches based on detected Antigravity version:
    /// - New format (>= 1.16.5): uses new key and protobuf structure
    /// - Old format (&lt; 1.16.5): uses legacy key and state manipulation
    /// - Unknown version (fallback): tries both, succeeds if at least one succeeds
    /// </summary>
    public bool InjectTokenToAntigravity(TokenData token)
    {
        var dbPath = GetDbPath();
        if (dbPath == null)
        {
            OnError?.Invoke("Antigravity database not found");
            return false;
        }

        var versionResult = _versionService.GetAntigravityVersion();

        if (versionResult != null)
        {
            if (versionResult.IsNewFormat)
            {
                return InjectNewFormat(dbPath, token);
            }
            else
            {
                return InjectOldFormat(dbPath, token);
            }
        }

        // Fallback: version unknown, try both formats
        OnStatusChanged?.Invoke("Version unknown, trying both formats...");

        var newFormatSuccess = false;
        var oldFormatSuccess = false;
        string? newFormatError = null;
        string? oldFormatError = null;

        try
        {
            newFormatSuccess = InjectNewFormat(dbPath, token);
        }
        catch (Exception ex)
        {
            newFormatError = ex.Message;
        }

        try
        {
            oldFormatSuccess = InjectOldFormat(dbPath, token);
        }
        catch (Exception ex)
        {
            oldFormatError = ex.Message;
        }

        if (newFormatSuccess || oldFormatSuccess)
        {
            OnStatusChanged?.Invoke("Token successfully injected (fallback mode)!");
            return true;
        }

        OnError?.Invoke($"Failed to inject token in both formats. New_Format: {newFormatError ?? "failed"}, Old_Format: {oldFormatError ?? "failed"}");
        return false;
    }

    /// <summary>
    /// Inject token using Old_Format (Antigravity &lt; 1.16.5).
    /// Reads existing state from "jetskiStateSync.agentManagerInitState", removes old fields,
    /// adds new OAuth field, and writes back.
    /// </summary>
    private bool InjectOldFormat(string dbPath, TokenData token)
    {
        try
        {
            OnStatusChanged?.Invoke("Injecting token using Old_Format...");

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

            // 3. Remove old Field 1, Field 2, and Field 6
            var cleanData = ProtobufHelper.RemoveField(blob, 6);
            cleanData = ProtobufHelper.RemoveField(cleanData, 1);
            cleanData = ProtobufHelper.RemoveField(cleanData, 2);

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

            OnStatusChanged?.Invoke("Token successfully injected using Old_Format!");
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to inject token (Old_Format): {ex.Message}");
            return false;
        }
    }


    /// <summary>
    /// Inject token using New_Format (Antigravity >= 1.16.5).
    /// Writes to key "antigravityUnifiedStateSync.oauthToken" with nested protobuf structure.
    /// </summary>
    private bool InjectNewFormat(string dbPath, TokenData token)
    {
        try
        {
            OnStatusChanged?.Invoke("Injecting token using New_Format...");

            // 1. Create OAuthTokenInfo protobuf (without Field 6 wrapper)
            var expiryTimestamp = new DateTimeOffset(token.ExpiryTime).ToUnixTimeSeconds();
            var oauthInfo = ProtobufHelper.CreateOAuthInfo(
                token.AccessToken,
                token.RefreshToken,
                expiryTimestamp
            );

            // 2. Base64 encode OAuthTokenInfo
            var base64OAuthInfo = Convert.ToBase64String(oauthInfo);

            // 3. Build InnerMessage2: Field 1 = base64(OAuthTokenInfo)
            var innerMessage2 = ProtobufHelper.EncodeStringField(1, base64OAuthInfo);

            // 4. Build InnerMessage: Field 1 = "oauthTokenInfoSentinelKey", Field 2 = InnerMessage2
            var innerMessageField1 = ProtobufHelper.EncodeStringField(1, "oauthTokenInfoSentinelKey");
            var innerMessageField2 = ProtobufHelper.EncodeLenDelimField(2, innerMessage2);
            var innerMessage = new byte[innerMessageField1.Length + innerMessageField2.Length];
            Array.Copy(innerMessageField1, 0, innerMessage, 0, innerMessageField1.Length);
            Array.Copy(innerMessageField2, 0, innerMessage, innerMessageField1.Length, innerMessageField2.Length);

            // 5. Build OuterMessage: Field 1 = InnerMessage
            var outerMessage = ProtobufHelper.EncodeLenDelimField(1, innerMessage);

            // 6. Base64 encode OuterMessage
            var finalBase64 = Convert.ToBase64String(outerMessage);

            // 7. Write to database
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (var writeCmd = connection.CreateCommand())
            {
                writeCmd.CommandText = "INSERT OR REPLACE INTO ItemTable (key, value) VALUES (@key, @value)";
                writeCmd.Parameters.AddWithValue("@key", NewOAuthKey);
                writeCmd.Parameters.AddWithValue("@value", finalBase64);
                writeCmd.ExecuteNonQuery();
            }

            // 8. Set onboarding flag
            using (var onboardCmd = connection.CreateCommand())
            {
                onboardCmd.CommandText = "INSERT OR REPLACE INTO ItemTable (key, value) VALUES (@key, @value)";
                onboardCmd.Parameters.AddWithValue("@key", OnboardingKey);
                onboardCmd.Parameters.AddWithValue("@value", "true");
                onboardCmd.ExecuteNonQuery();
            }

            OnStatusChanged?.Invoke("Token successfully injected using New_Format!");
            return true;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"Failed to inject token (New_Format): {ex.Message}");
            return false;
        }
    }
}
