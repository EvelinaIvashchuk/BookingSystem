using BookingSystem.Ai;
using BookingSystem.Models;

namespace BookingSystem.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalCars { get; set; }
    public int AvailableCars { get; set; }
    public int TotalRentals { get; set; }
    public int PendingRentals { get; set; }
    public int TodaysRentals { get; set; }

    public IEnumerable<Rental> RecentPending { get; set; } = [];

    /// <summary>Chat model in use now (the administrator's choice or the configured default).</summary>
    public string ActiveAiModel { get; set; } = string.Empty;

    /// <summary>Models the administrator can choose for the chat.</summary>
    public IReadOnlyList<ChatModelOption> AiModels { get; set; } = [];

    /// <summary>The configured default model (<c>Ai:DefaultModel</c>).</summary>
    public string DefaultAiModel { get; set; } = string.Empty;

    /// <summary>Car ranking model in use now (lightgbm, mlp or two_tower).</summary>
    public string ActiveRankingModel { get; set; } = string.Empty;

    /// <summary>Ranking models the administrator can choose.</summary>
    public IReadOnlyList<RankingModelOption> RankingModels { get; set; } = [];
}
