using Cominomi.Shared.Models;

namespace Cominomi.Shared.Services;

public interface IGamificationService
{
    Task<DashboardStats> GetDashboardStatsAsync();
}
