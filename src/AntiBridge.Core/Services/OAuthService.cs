using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Google OAuth2 service for Antigravity authentication.
/// Ported from Antigravity-Manager oauth.rs and oauth_server.rs
/// </summary>
public class OAuthService
{
    // Google OAuth configuration (from Antigravity-Manager)
    private const string ClientId = "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com";
    private const string ClientSecret = "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";
    private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string UserInfoUrl = "https://www.googleapis.com/oauth2/v2/userinfo";

    private static readonly string[] Scopes =
    [
        "https://www.googleapis.com/auth/cloud-platform",
        "https://www.googleapis.com/auth/userinfo.email",
        "https://www.googleapis.com/auth/userinfo.profile",
        "https://www.googleapis.com/auth/cclog",
        "https://www.googleapis.com/auth/experimentsandconfigs"
    ];

    private readonly HttpClient _httpClient;
    private HttpListener? _callbackListener;
    private CancellationTokenSource? _cancellationTokenSource;

    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    public OAuthService()
    {
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Generate OAuth authorization URL
    /// </summary>
    public string GetAuthUrl(string redirectUri)
    {
        var scopeString = string.Join(" ", Scopes);
        var queryParams = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scopeString,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
            ["include_granted_scopes"] = "true"
        };

        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        return $"{AuthUrl}?{queryString}";
    }

    /// <summary>
    /// Start OAuth flow with local callback server
    /// </summary>
    public async Task<TokenData?> StartOAuthFlowAsync()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        // Find available port
        var port = FindAvailablePort();
        var redirectUri = $"http://localhost:{port}/oauth-callback/";

        // Start local HTTP listener
        _callbackListener = new HttpListener();
        _callbackListener.Prefixes.Add(redirectUri);

        try
        {
            _callbackListener.Start();
            OnStatusChanged?.Invoke($"Waiting for OAuth callback on port {port}...");

            // Generate and return auth URL for browser
            var authUrl = GetAuthUrl(redirectUri.TrimEnd('/'));

            // Open browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            // Wait for callback
            var context = await _callbackListener.GetContextAsync();
            var code = context.Request.QueryString["code"];

            // Send success response to browser
            var responseHtml = code != null
                ? "<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h1 style='color:green;'>✅ Authorization Successful!</h1><p>You can close this window.</p></body></html>"
                : "<html><body style='font-family:sans-serif;text-align:center;padding:50px;'><h1 style='color:red;'>❌ Authorization Failed</h1></body></html>";

            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer);
            context.Response.Close();

            if (string.IsNullOrEmpty(code))
            {
                OnError?.Invoke("No authorization code received");
                return null;
            }

            OnStatusChanged?.Invoke("Exchanging code for tokens...");

            // Exchange code for tokens
            return await ExchangeCodeAsync(code, redirectUri.TrimEnd('/'));
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"OAuth error: {ex.Message}");
            return null;
        }
        finally
        {
            _callbackListener?.Stop();
            _callbackListener?.Close();
        }
    }

    /// <summary>
    /// Exchange authorization code for tokens
    /// </summary>
    public async Task<TokenData?> ExchangeCodeAsync(string code, string redirectUri)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code"
        });

        var response = await _httpClient.PostAsync(TokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            OnError?.Invoke($"Token exchange failed: {error}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (tokenResponse == null) return null;

        return TokenData.Create(
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken ?? string.Empty,
            tokenResponse.ExpiresIn
        );
    }

    /// <summary>
    /// Refresh access token using refresh token
    /// </summary>
    public async Task<TokenData?> RefreshAccessTokenAsync(string refreshToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        });

        var response = await _httpClient.PostAsync(TokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            OnError?.Invoke($"Token refresh failed: {error}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        if (tokenResponse == null) return null;

        return TokenData.Create(
            tokenResponse.AccessToken,
            refreshToken, // Keep original refresh token
            tokenResponse.ExpiresIn
        );
    }

    /// <summary>
    /// Get user info from Google
    /// </summary>
    public async Task<UserInfo?> GetUserInfoAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, UserInfoUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<UserInfo>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });
    }

    /// <summary>
    /// Cancel ongoing OAuth flow
    /// </summary>
    public void CancelOAuthFlow()
    {
        _cancellationTokenSource?.Cancel();
        _callbackListener?.Stop();
    }

    private static int FindAvailablePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Response DTOs
    private class TokenResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string? RefreshToken { get; set; }
        public long ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
    }
}

public class UserInfo
{
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? GivenName { get; set; }
    public string? FamilyName { get; set; }
    public string? Picture { get; set; }

    public string GetDisplayName() =>
        Name ?? (GivenName != null && FamilyName != null ? $"{GivenName} {FamilyName}" : GivenName ?? Email);
}
