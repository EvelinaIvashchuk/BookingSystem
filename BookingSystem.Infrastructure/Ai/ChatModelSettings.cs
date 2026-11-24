using BookingSystem.Data;
using BookingSystem.Models;
using Microsoft.Extensions.Options;

namespace BookingSystem.Ai;

/// <summary>
/// Stores the administrator's chat model choice in the <c>AppSettings</c> table (key <see cref="SettingKey"/>).
/// The list of choosable models comes from configuration, so an administrator can never select a model
/// the application has no key for.
/// </summary>
public sealed class ChatModelSettings(ApplicationDbContext db, IOptions<AiOptions> options, TimeProvider clock) : IChatModelSettings
{
    public const string SettingKey = "Ai:ChatModel";

    public IReadOnlyList<ChatModelOption> GetAvailableModels()
    {
        var ai = options.Value;
        var ids = new List<string> { ai.DefaultModel };
        ids.AddRange(ai.Pricing.Select(p => p.Model));

        var result = new List<ChatModelOption>();
        foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()))
        {
            if (result.Any(o => o.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (!TryParse(id, out var model))
                continue;
            if (!ai.Providers.TryGetValue(model.Provider, out var provider)
                || string.IsNullOrWhiteSpace(provider.Endpoint) || string.IsNullOrWhiteSpace(provider.ApiKey))
                continue;

            var price = ai.Pricing.FirstOrDefault(p => p.Model.Equals(id, StringComparison.OrdinalIgnoreCase));
            result.Add(new ChatModelOption(id, model.Provider, model.Model, price?.InputPer1M ?? 0, price?.OutputPer1M ?? 0));
        }

        return result;
    }

    public async Task<string> GetActiveModelAsync(CancellationToken cancellationToken = default)
    {
        var chosen = await db.GetSettingAsync(SettingKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var match = GetAvailableModels().FirstOrDefault(o => o.Id.Equals(chosen, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.Id;
        }

        return options.Value.DefaultModel;
    }

    public async Task SetActiveModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var match = GetAvailableModels().FirstOrDefault(o => o.Id.Equals(modelId?.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Model '{modelId}' is not available.", nameof(modelId));

        await db.SetSettingAsync(SettingKey, match.Id, clock.GetUtcNow().UtcDateTime, cancellationToken);
    }

    private static bool TryParse(string id, out ModelRef model)
    {
        try
        {
            model = ModelRef.Parse(id);
            return true;
        }
        catch (FormatException)
        {
            model = default;
            return false;
        }
    }
}
