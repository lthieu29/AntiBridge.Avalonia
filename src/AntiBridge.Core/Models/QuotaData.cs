namespace AntiBridge.Core.Models;

/// <summary>
/// Quota information for AI models.
/// </summary>
public class QuotaData
{
    public List<ModelQuota> Models { get; set; } = [];
    public bool IsForbidden { get; set; }
    public string? SubscriptionTier { get; set; }
    public string? ProjectId { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    public void AddModel(string name, int percentage, string resetTime)
    {
        Models.Add(new ModelQuota
        {
            Name = name,
            Percentage = percentage,
            ResetTime = resetTime
        });
    }
}

/// <summary>
/// Quota for a single AI model.
/// </summary>
public class ModelQuota
{
    public string Name { get; set; } = string.Empty;
    public int Percentage { get; set; }
    public string ResetTime { get; set; } = string.Empty;
}
