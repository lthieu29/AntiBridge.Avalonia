namespace AntiBridge.Core.Models;

/// <summary>
/// OAuth token data for Google authentication.
/// </summary>
public class TokenData
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public long ExpiresIn { get; set; }
    public DateTime ExpiryTime { get; set; }
    public string? Email { get; set; }
    public string? ProjectId { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiryTime.AddMinutes(-5);

    public static TokenData Create(string accessToken, string refreshToken, long expiresIn, string? email = null)
    {
        return new TokenData
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn,
            ExpiryTime = DateTime.UtcNow.AddSeconds(expiresIn),
            Email = email
        };
    }
}
