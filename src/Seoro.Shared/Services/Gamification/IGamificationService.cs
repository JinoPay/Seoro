
namespace Seoro.Shared.Services.Gamification;

public interface IGamificationService
{
    Task<DashboardStats> ForceRefreshDashboardAsync();
    Task<DashboardStats> GetDashboardStatsAsync();
}