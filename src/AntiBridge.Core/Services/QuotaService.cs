using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AntiBridge.Core.Models;

namespace AntiBridge.Core.Services;

/// <summary>
/// Service to fetch quota information from Google Cloud Code API.
/// Ported from Antigravity-Manager quota.rs
/// </summary>
public class QuotaService
{
    private const string QuotaApiUrl = "https://cloudcode-pa.googleapis.com/v1internal:fetchAvailableModels";
    private const string LoadCodeAssistUrl = "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist";
    private const string UserAgent = "antigravity/1.11.3 Darwin/arm64";

    private readonly HttpClient _httpClient;

    public event Action<string>? OnStatusChanged;
    public event Action<string>? OnError;

    public QuotaService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Fetch project ID from Google Cloud Code
    /// </summary>
    public async Task<(string? ProjectId, string? SubscriptionTier)> FetchProjectIdAsync(string accessToken)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, LoadCodeAssistUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { metadata = new { ideType = "ANTIGRAVITY" } }),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                OnStatusChanged?.Invoke($"loadCodeAssist failed: {response.StatusCode}");
                return (null, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<LoadProjectResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var projectId = result?.CloudaicompanionProject;
            var tier = result?.PaidTier?.Id ?? result?.CurrentTier?.Id;

            return (projectId, tier);
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"FetchProjectId error: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Fetch quota for all models
    /// </summary>
    public async Task<QuotaData?> FetchQuotaAsync(string accessToken, string? projectId = null)
    {
        try
        {
            OnStatusChanged?.Invoke("Fetching quota...");

            // Get project ID if not provided
            if (string.IsNullOrEmpty(projectId))
            {
                var (pid, _) = await FetchProjectIdAsync(accessToken);
                projectId = pid ?? "bamboo-precept-lgxtn"; // Default fallback
            }

            var request = new HttpRequestMessage(HttpMethod.Post, QuotaApiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { project = projectId }),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.SendAsync(request);

            // Handle 403 Forbidden
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                OnStatusChanged?.Invoke("Account forbidden (403)");
                return new QuotaData { IsForbidden = true };
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                OnError?.Invoke($"Quota API error: {response.StatusCode} - {error}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var quotaResponse = JsonSerializer.Deserialize<QuotaApiResponse>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (quotaResponse?.Models == null)
            {
                OnError?.Invoke("Invalid quota response");
                return null;
            }

            var quotaData = new QuotaData();

            foreach (var (modelName, modelInfo) in quotaResponse.Models)
            {
                if (modelInfo.QuotaInfo == null) continue;

                var percentage = (int)((modelInfo.QuotaInfo.RemainingFraction ?? 0) * 100);
                var resetTime = modelInfo.QuotaInfo.ResetTime ?? string.Empty;

                // Only keep relevant models (gemini, claude)
                if (modelName.Contains("gemini", StringComparison.OrdinalIgnoreCase) ||
                    modelName.Contains("claude", StringComparison.OrdinalIgnoreCase))
                {
                    quotaData.AddModel(modelName, percentage, resetTime);
                }
            }

            OnStatusChanged?.Invoke($"Fetched {quotaData.Models.Count} model quotas");
            return quotaData;
        }
        catch (Exception ex)
        {
            OnError?.Invoke($"FetchQuota error: {ex.Message}");
            return null;
        }
    }

    // Response DTOs
    private class LoadProjectResponse
    {
        [JsonPropertyName("cloudaicompanionProject")]
        public string? CloudaicompanionProject { get; set; }

        [JsonPropertyName("currentTier")]
        public TierInfo? CurrentTier { get; set; }

        [JsonPropertyName("paidTier")]
        public TierInfo? PaidTier { get; set; }
    }

    private class TierInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private class QuotaApiResponse
    {
        public Dictionary<string, ModelInfo>? Models { get; set; }
    }

    private class ModelInfo
    {
        [JsonPropertyName("quotaInfo")]
        public QuotaInfo? QuotaInfo { get; set; }
    }

    private class QuotaInfo
    {
        [JsonPropertyName("remainingFraction")]
        public double? RemainingFraction { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }
    }
}
