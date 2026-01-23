using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AntiBridge.Core.Models;
using AntiBridge.Core.Translator;

namespace AntiBridge.Core.Services;

/// <summary>
/// Executes requests to Antigravity API
/// Ported from CLIProxyAPI/internal/runtime/executor/antigravity_executor.go
/// </summary>
public class AntigravityExecutor : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly OAuthService _oauthService;
    private readonly AccountStorageService _accountStorage;
    private readonly RetryOptions _retryOptions;
    private readonly ILoadBalancer? _loadBalancer;
    private DeviceProfile? _currentDeviceProfile;
    
    private static readonly string[] BaseUrls = 
    [
        ProxyConfig.AntigravitySandboxUrl,
        ProxyConfig.AntigravityDailyUrl,
        ProxyConfig.AntigravityBaseUrl
    ];

    private const string SystemInstruction = "You are Antigravity, a powerful agentic AI coding assistant designed by the Google Deepmind team working on Advanced Agentic Coding.";

    public event Action<string>? OnLog;

    public AntigravityExecutor(OAuthService oauthService, AccountStorageService accountStorage, RetryOptions? retryOptions = null, ILoadBalancer? loadBalancer = null)
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _oauthService = oauthService;
        _accountStorage = accountStorage;
        _retryOptions = retryOptions ?? new RetryOptions();
        _loadBalancer = loadBalancer;
    }

    /// <summary>
    /// Set the device profile to use for subsequent requests.
    /// When set, device fingerprint headers will be applied to outgoing requests.
    /// </summary>
    /// <param name="profile">The device profile to use, or null to use system defaults.</param>
    public void SetDeviceProfile(DeviceProfile? profile)
    {
        _currentDeviceProfile = profile;
    }

    /// <summary>
    /// Apply device profile headers to an HTTP request.
    /// </summary>
    /// <param name="request">The HTTP request to modify.</param>
    private void ApplyDeviceProfileToRequest(HttpRequestMessage request)
    {
        if (_currentDeviceProfile == null)
            return;

        // Apply device fingerprint headers
        request.Headers.TryAddWithoutValidation("X-Machine-Id", _currentDeviceProfile.MachineId);
        request.Headers.TryAddWithoutValidation("X-Mac-Machine-Id", _currentDeviceProfile.MacMachineId);
        request.Headers.TryAddWithoutValidation("X-Dev-Device-Id", _currentDeviceProfile.DevDeviceId);
        request.Headers.TryAddWithoutValidation("X-Sqm-Id", _currentDeviceProfile.SqmId);
    }

    /// <summary>
    /// Execute streaming request to Antigravity
    /// </summary>
    public async IAsyncEnumerable<byte[]> ExecuteStreamAsync(
        Account account,
        string modelName,
        byte[] payload,
        string sourceFormat, // "claude" or "openai"
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var tokenRefreshAttempted = false;

        while (retryCount <= _retryOptions.MaxAuthRetries)
        {
            // Ensure fresh token
            var token = await EnsureAccessTokenAsync(account);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Failed to get access token");
            }

            // Translate request
            var translatedPayload = sourceFormat.ToLower() switch
            {
                "claude" => ClaudeToAntigravityRequest.Convert(modelName, payload, true),
                "openai" => OpenAIToAntigravityRequest.Convert(modelName, payload, true),
                _ => payload
            };

            // Add Antigravity-specific fields
            translatedPayload = PrepareAntigravityPayload(modelName, translatedPayload, account);

            // Try each base URL
            HttpResponseMessage? successResponse = null;
            Exception? lastException = null;
            var got401 = false;

            foreach (var baseUrl in BaseUrls)
            {
                var url = $"{baseUrl}{ProxyConfig.StreamPath}?alt=sse";
                
                OnLog?.Invoke($"Trying {baseUrl}...");

                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new ByteArrayContent(translatedPayload)
                    };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Headers.UserAgent.ParseAdd(ProxyConfig.DefaultUserAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                    
                    // Apply device profile headers (Task 8.4)
                    ApplyDeviceProfileToRequest(request);

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync(cancellationToken);
                        OnLog?.Invoke($"Error from {baseUrl}: {response.StatusCode} - {error}");
                        
                        // Handle 401 Unauthorized - trigger retry with token refresh
                        // Requirements 2.1, 2.2, 2.3, 2.4, 2.5
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            got401 = true;
                            lastException = new HttpRequestException($"Antigravity API error: {response.StatusCode} - {error}", null, HttpStatusCode.Unauthorized);
                            break; // Exit URL loop to handle 401 retry
                        }
                        
                        // Handle 429 Too Many Requests - mark account rate limited
                        // Requirements 5.3, 5.6
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            if (_loadBalancer != null)
                            {
                                // Try to parse Retry-After header
                                TimeSpan? retryAfter = null;
                                if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                                {
                                    var retryAfterStr = retryAfterValues.FirstOrDefault();
                                    if (int.TryParse(retryAfterStr, out var seconds))
                                    {
                                        retryAfter = TimeSpan.FromSeconds(seconds);
                                    }
                                }
                                _loadBalancer.MarkRateLimited(account.Id, retryAfter);
                                OnLog?.Invoke($"Account {account.Email} marked as rate limited");
                            }
                            continue; // Try next URL
                        }
                        
                        // Check for quota exceeded error
                        if (error.Contains("quota", StringComparison.OrdinalIgnoreCase) || 
                            error.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_loadBalancer != null)
                            {
                                _loadBalancer.MarkQuotaExceeded(account.Id);
                                OnLog?.Invoke($"Account {account.Email} marked as quota exceeded");
                            }
                        }
                        
                        throw new HttpRequestException($"Antigravity API error: {response.StatusCode} - {error}");
                    }

                    successResponse = response;
                    break;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    got401 = true;
                    lastException = ex;
                    break; // Exit URL loop to handle 401 retry
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                {
                    lastException = ex;
                    continue;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    OnLog?.Invoke($"Error: {ex.Message}");
                }
            }

            // Handle 401 with retry logic
            if (got401 && retryCount < _retryOptions.MaxAuthRetries && _retryOptions.AutoRefreshToken)
            {
                OnLog?.Invoke("Received 401 Unauthorized, attempting token refresh...");
                
                // Attempt token refresh
                // Requirement 2.1: WHEN Antigravity_API trả về HTTP 401 Unauthorized, THE AntigravityExecutor SHALL tự động gọi refresh token
                var refreshSuccess = await TryRefreshTokenAsync(account);
                tokenRefreshAttempted = true;
                
                if (!refreshSuccess)
                {
                    // Requirement 2.4: WHEN refresh token thất bại, THE AntigravityExecutor SHALL trả lỗi authentication về client ngay lập tức
                    OnLog?.Invoke("Token refresh failed, returning authentication error");
                    throw new HttpRequestException("Authentication failed: Token refresh unsuccessful", lastException, HttpStatusCode.Unauthorized);
                }
                
                // Requirement 2.2: WHEN refresh token thành công, THE AntigravityExecutor SHALL retry request ban đầu một lần
                OnLog?.Invoke("Token refresh successful, retrying request...");
                retryCount++;
                continue; // Retry the request
            }

            // If we got 401 after retry, return error
            // Requirement 2.3: IF retry vẫn trả về 401, THEN THE AntigravityExecutor SHALL trả lỗi authentication về client
            if (got401)
            {
                var message = tokenRefreshAttempted 
                    ? "Authentication failed: Request still unauthorized after token refresh"
                    : "Authentication failed: 401 Unauthorized";
                throw new HttpRequestException(message, lastException, HttpStatusCode.Unauthorized);
            }

            if (successResponse == null)
            {
                throw lastException ?? new HttpRequestException("All Antigravity endpoints failed");
            }

            // Stream response (outside try-catch)
            await using var stream = await successResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (string.IsNullOrEmpty(line)) continue;

                // Parse SSE data
                if (line.StartsWith("data: "))
                {
                    var data = line[6..];
                    if (data == "[DONE]")
                    {
                        yield return "[DONE]"u8.ToArray();
                        yield break;
                    }

                    yield return Encoding.UTF8.GetBytes(data);
                }
            }
            
            yield break; // Success, exit the retry loop
        }
    }

    /// <summary>
    /// Execute non-streaming request to Antigravity
    /// </summary>
    public async Task<byte[]> ExecuteAsync(
        Account account,
        string modelName,
        byte[] payload,
        string sourceFormat,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var tokenRefreshAttempted = false;

        while (retryCount <= _retryOptions.MaxAuthRetries)
        {
            var token = await EnsureAccessTokenAsync(account);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException("Failed to get access token");
            }

            var translatedPayload = sourceFormat.ToLower() switch
            {
                "claude" => ClaudeToAntigravityRequest.Convert(modelName, payload, false),
                "openai" => OpenAIToAntigravityRequest.Convert(modelName, payload, false),
                _ => payload
            };

            translatedPayload = PrepareAntigravityPayload(modelName, translatedPayload, account);

            Exception? lastException = null;
            var got401 = false;

            foreach (var baseUrl in BaseUrls)
            {
                var url = $"{baseUrl}{ProxyConfig.GeneratePath}";
                
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new ByteArrayContent(translatedPayload)
                    };
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Headers.UserAgent.ParseAdd(ProxyConfig.DefaultUserAgent);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    
                    // Apply device profile headers (Task 8.4)
                    ApplyDeviceProfileToRequest(request);

                    var response = await _httpClient.SendAsync(request, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        // Handle 401 Unauthorized - trigger retry with token refresh
                        // Requirements 2.1, 2.2, 2.3, 2.4, 2.5
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            var error = await response.Content.ReadAsStringAsync(cancellationToken);
                            got401 = true;
                            lastException = new HttpRequestException($"Antigravity API error: {response.StatusCode} - {error}", null, HttpStatusCode.Unauthorized);
                            break; // Exit URL loop to handle 401 retry
                        }
                        
                        // Handle 429 Too Many Requests - mark account rate limited
                        // Requirements 5.3, 5.6
                        if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            if (_loadBalancer != null)
                            {
                                // Try to parse Retry-After header
                                TimeSpan? retryAfter = null;
                                if (response.Headers.TryGetValues("Retry-After", out var retryAfterValues))
                                {
                                    var retryAfterStr = retryAfterValues.FirstOrDefault();
                                    if (int.TryParse(retryAfterStr, out var seconds))
                                    {
                                        retryAfter = TimeSpan.FromSeconds(seconds);
                                    }
                                }
                                _loadBalancer.MarkRateLimited(account.Id, retryAfter);
                                OnLog?.Invoke($"Account {account.Email} marked as rate limited");
                            }
                            continue;
                        }
                        
                        var errorMsg = await response.Content.ReadAsStringAsync(cancellationToken);
                        
                        // Check for quota exceeded error
                        if (errorMsg.Contains("quota", StringComparison.OrdinalIgnoreCase) || 
                            errorMsg.Contains("exceeded", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_loadBalancer != null)
                            {
                                _loadBalancer.MarkQuotaExceeded(account.Id);
                                OnLog?.Invoke($"Account {account.Email} marked as quota exceeded");
                            }
                        }
                        
                        throw new HttpRequestException($"Antigravity API error: {response.StatusCode} - {errorMsg}");
                    }

                    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
                {
                    got401 = true;
                    lastException = ex;
                    break; // Exit URL loop to handle 401 retry
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                {
                    lastException = ex;
                    continue;
                }
            }

            // Handle 401 with retry logic
            if (got401 && retryCount < _retryOptions.MaxAuthRetries && _retryOptions.AutoRefreshToken)
            {
                OnLog?.Invoke("Received 401 Unauthorized, attempting token refresh...");
                
                // Attempt token refresh
                // Requirement 2.1: WHEN Antigravity_API trả về HTTP 401 Unauthorized, THE AntigravityExecutor SHALL tự động gọi refresh token
                var refreshSuccess = await TryRefreshTokenAsync(account);
                tokenRefreshAttempted = true;
                
                if (!refreshSuccess)
                {
                    // Requirement 2.4: WHEN refresh token thất bại, THE AntigravityExecutor SHALL trả lỗi authentication về client ngay lập tức
                    OnLog?.Invoke("Token refresh failed, returning authentication error");
                    throw new HttpRequestException("Authentication failed: Token refresh unsuccessful", lastException, HttpStatusCode.Unauthorized);
                }
                
                // Requirement 2.2: WHEN refresh token thành công, THE AntigravityExecutor SHALL retry request ban đầu một lần
                OnLog?.Invoke("Token refresh successful, retrying request...");
                retryCount++;
                continue; // Retry the request
            }

            // If we got 401 after retry, return error
            // Requirement 2.3: IF retry vẫn trả về 401, THEN THE AntigravityExecutor SHALL trả lỗi authentication về client
            if (got401)
            {
                var message = tokenRefreshAttempted 
                    ? "Authentication failed: Request still unauthorized after token refresh"
                    : "Authentication failed: 401 Unauthorized";
                throw new HttpRequestException(message, lastException, HttpStatusCode.Unauthorized);
            }

            // If we reach here without returning, all URLs failed
            throw lastException ?? new HttpRequestException("All Antigravity endpoints failed");
        }

        // Requirement 2.5: THE AntigravityExecutor SHALL chỉ retry tối đa 1 lần cho mỗi request
        throw new HttpRequestException("Authentication failed: Max retries exceeded", null, HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Count tokens using Antigravity API
    /// </summary>
    public async Task<long> CountTokensAsync(
        Account account,
        string modelName,
        byte[] payload,
        string sourceFormat,
        CancellationToken cancellationToken = default)
    {
        var token = await EnsureAccessTokenAsync(account);
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Failed to get access token");
        }

        var translatedPayload = sourceFormat.ToLower() switch
        {
            "claude" => ClaudeToAntigravityRequest.Convert(modelName, payload, false),
            "openai" => OpenAIToAntigravityRequest.Convert(modelName, payload, false),
            _ => payload
        };

        // Remove some fields for token counting
        var node = JsonHelper.Parse(translatedPayload);
        if (node != null)
        {
            JsonHelper.Delete(node, "project");
            JsonHelper.Delete(node, "model");
            JsonHelper.Delete(node, "request.safetySettings");
            translatedPayload = Encoding.UTF8.GetBytes(JsonHelper.Stringify(node));
        }

        foreach (var baseUrl in BaseUrls)
        {
            var url = $"{baseUrl}{ProxyConfig.CountTokensPath}";
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new ByteArrayContent(translatedPayload)
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd(ProxyConfig.DefaultUserAgent);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        continue;
                    continue;
                }

                var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var responseNode = JsonHelper.Parse(responseBytes);
                return JsonHelper.GetLong(responseNode, "totalTokens") ?? 0;
            }
            catch
            {
                continue;
            }
        }

        return 0;
    }

    /// <summary>
    /// Fetch available models from Antigravity
    /// </summary>
    public async Task<List<string>> FetchModelsAsync(Account account, CancellationToken cancellationToken = default)
    {
        var token = await EnsureAccessTokenAsync(account);
        if (string.IsNullOrEmpty(token))
        {
            return [];
        }

        foreach (var baseUrl in BaseUrls)
        {
            var url = $"{baseUrl}{ProxyConfig.ModelsPath}";
            
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.UserAgent.ParseAdd(ProxyConfig.DefaultUserAgent);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (!response.IsSuccessStatusCode) continue;

                var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var responseNode = JsonHelper.Parse(responseBytes);
                var models = JsonHelper.GetPath(responseNode, "models") as JsonObject;
                
                if (models == null) continue;

                var modelList = new List<string>();
                foreach (var kvp in models)
                {
                    var modelId = kvp.Key.Trim();
                    if (string.IsNullOrEmpty(modelId)) continue;
                    
                    // Skip some internal models
                    if (modelId is "chat_20706" or "chat_23310" or "gemini-2.5-flash-thinking" or "gemini-3-pro-low" or "gemini-2.5-pro")
                        continue;
                    
                    modelList.Add(modelId);
                }
                
                return modelList;
            }
            catch
            {
                continue;
            }
        }

        return [];
    }

    private async Task<string?> EnsureAccessTokenAsync(Account account)
    {
        if (!account.Token.IsExpired)
        {
            return account.Token.AccessToken;
        }

        // Refresh token
        var newToken = await _oauthService.RefreshAccessTokenAsync(account.Token.RefreshToken);
        if (newToken == null) return null;

        account.Token = newToken;
        _accountStorage.UpsertAccount(account.Email, account.Name, newToken);
        
        return newToken.AccessToken;
    }

    /// <summary>
    /// Attempt to refresh the access token for an account.
    /// Used for 401 retry logic.
    /// </summary>
    /// <param name="account">The account to refresh token for</param>
    /// <returns>True if refresh succeeded, false otherwise</returns>
    private async Task<bool> TryRefreshTokenAsync(Account account)
    {
        try
        {
            var newToken = await _oauthService.RefreshAccessTokenAsync(account.Token.RefreshToken);
            if (newToken == null)
            {
                OnLog?.Invoke("Token refresh returned null");
                return false;
            }

            account.Token = newToken;
            _accountStorage.UpsertAccount(account.Email, account.Name, newToken);
            
            OnLog?.Invoke("Token refreshed successfully");
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Token refresh failed with exception: {ex.Message}");
            return false;
        }
    }

    private byte[] PrepareAntigravityPayload(string modelName, byte[] payload, Account account)
    {
        var node = JsonHelper.Parse(payload);
        if (node == null) return payload;

        // Set model
        JsonHelper.SetString(node, "model", modelName);
        
        // Set Antigravity-specific fields
        JsonHelper.SetString(node, "userAgent", "antigravity");
        JsonHelper.SetString(node, "requestType", "agent");
        
        // Project ID
        var projectId = account.Quota?.ProjectId ?? GenerateProjectId();
        JsonHelper.SetString(node, "project", projectId);
        
        // Request ID
        JsonHelper.SetString(node, "requestId", $"agent-{Guid.NewGuid()}");
        
        // Session ID (stable based on first user message)
        var sessionId = GenerateStableSessionId(node);
        JsonHelper.SetString(node, "request.sessionId", sessionId);

        // Remove safety settings (we add our own)
        JsonHelper.Delete(node, "request.safetySettings");

        // For Claude models, add system instruction prefix
        if (modelName.Contains("claude") || modelName.Contains("gemini-3-pro"))
        {
            var existingParts = JsonHelper.GetPath(node, "request.systemInstruction.parts") as JsonArray;
            
            JsonHelper.SetString(node, "request.systemInstruction.role", "user");
            
            var newParts = new JsonArray
            {
                new JsonObject { ["text"] = SystemInstruction },
                new JsonObject { ["text"] = $"Please ignore following [ignore]{SystemInstruction}[/ignore]" }
            };
            
            if (existingParts != null)
            {
                foreach (var part in existingParts)
                {
                    newParts.Add(part?.DeepClone());
                }
            }
            
            JsonHelper.SetPath(node, "request.systemInstruction.parts", newParts);
        }

        return Encoding.UTF8.GetBytes(JsonHelper.Stringify(node));
    }

    private static string GenerateProjectId()
    {
        var adjectives = new[] { "useful", "bright", "swift", "calm", "bold" };
        var nouns = new[] { "fuze", "wave", "spark", "flow", "core" };
        var random = new Random();
        var adj = adjectives[random.Next(adjectives.Length)];
        var noun = nouns[random.Next(nouns.Length)];
        var randomPart = Guid.NewGuid().ToString("N")[..5].ToLower();
        return $"{adj}-{noun}-{randomPart}";
    }

    private static string GenerateStableSessionId(JsonNode? payload)
    {
        var contents = JsonHelper.GetPath(payload, "request.contents") as JsonArray;
        if (contents != null)
        {
            foreach (var content in contents)
            {
                if (JsonHelper.GetString(content, "role") == "user")
                {
                    var text = JsonHelper.GetString(content, "parts.0.text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
                        var n = BitConverter.ToInt64(hash, 0) & 0x7FFFFFFFFFFFFFFF;
                        return $"-{n}";
                    }
                }
            }
        }
        
        return $"-{new Random().NextInt64(9_000_000_000_000_000_000)}";
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
