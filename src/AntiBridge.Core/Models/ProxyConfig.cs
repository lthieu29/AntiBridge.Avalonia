namespace AntiBridge.Core.Models;

/// <summary>
/// Configuration for the proxy server
/// </summary>
public class ProxyConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8081;
    public bool EnableClaude { get; set; } = true;
    public bool EnableOpenAI { get; set; } = true;
    public string? DefaultModel { get; set; }
    
    /// <summary>
    /// Signature cache configuration options
    /// </summary>
    public SignatureCacheConfig SignatureCache { get; set; } = new();
    
    /// <summary>
    /// Load balancer configuration options
    /// </summary>
    public LoadBalancerConfig LoadBalancer { get; set; } = new();
    
    /// <summary>
    /// Retry configuration options
    /// </summary>
    public RetryConfig Retry { get; set; } = new();
    
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

/// <summary>
/// Configuration for signature cache
/// </summary>
public class SignatureCacheConfig
{
    /// <summary>
    /// Enable signature caching
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Time-to-live for cache entries in minutes (default: 60)
    /// </summary>
    public int TtlMinutes { get; set; } = 60;
    
    /// <summary>
    /// Maximum number of entries (default: 10000)
    /// </summary>
    public int MaxEntries { get; set; } = 10000;
    
    /// <summary>
    /// Cleanup interval in minutes (default: 5)
    /// </summary>
    public int CleanupIntervalMinutes { get; set; } = 5;
}

/// <summary>
/// Configuration for load balancer
/// </summary>
public class LoadBalancerConfig
{
    /// <summary>
    /// Enable load balancing across multiple accounts
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// Load balancing strategy: "RoundRobin" or "FillFirst"
    /// </summary>
    public string Strategy { get; set; } = "RoundRobin";
    
    /// <summary>
    /// Default rate limit duration in seconds (default: 60)
    /// </summary>
    public int DefaultRateLimitSeconds { get; set; } = 60;
}

/// <summary>
/// Configuration for retry behavior
/// </summary>
public class RetryConfig
{
    /// <summary>
    /// Maximum retry attempts for 401 errors (default: 1)
    /// </summary>
    public int MaxAuthRetries { get; set; } = 1;
    
    /// <summary>
    /// Enable automatic token refresh on 401
    /// </summary>
    public bool AutoRefreshToken { get; set; } = true;
}
