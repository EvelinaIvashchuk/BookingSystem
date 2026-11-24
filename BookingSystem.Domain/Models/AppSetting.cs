namespace BookingSystem.Models;

/// <summary>
/// A runtime setting changed by an administrator (key/value). Used for values that must survive restarts
/// and redeploys but should not require editing the configuration files, e.g. the chat model.
/// </summary>
public class AppSetting
{
    /// <summary>Setting name, e.g. <c>Ai:ChatModel</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }
}
