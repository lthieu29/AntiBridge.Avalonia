namespace AntiBridge.Core.Models;

/// <summary>
/// Configuration for the proxy server
/// </summary>
public class ProxyConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public bool EnableClaude { get; set; } = true;
    public bool EnableOpenAI { get; set; } = true;
    public string? DefaultModel { get; set; }
    
    // Antigravity API endpoints
    public const string AntigravityBaseUrl = "https://cloudcode-pa.googleapis.com";
    public const string AntigravitySandboxUrl = "https://daily-cloudcode-pa.sandbox.googleapis.com";
    public const string AntigravityDailyUrl = "https://daily-cloudcode-pa.googleapis.com";
    
    public const string StreamPath = "/v1internal:streamGenerateContent";
    public const string GeneratePath = "/v1internal:generateContent";
    public const string CountTokensPath = "/v1internal:countTokens";
    public const string ModelsPath = "/v1internal:fetchAvailableModels";
    
    public const string DefaultUserAgent = "antigravity/1.104.0 darwin/arm64";
}
