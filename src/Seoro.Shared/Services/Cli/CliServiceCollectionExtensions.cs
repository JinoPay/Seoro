using AgentBridge;
using AgentBridge.Claude;
using AgentBridge.Codex;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Seoro.Shared.Models.Settings;

namespace Seoro.Shared.Services.Cli;

/// <summary>
/// AgentBridge.NET 엔진과 그 위의 Seoro <see cref="ICliProvider"/> 어댑터를 한 번에 등록하는 DI 확장.
/// Program.cs는 이 메서드만 호출하면 되며, AgentBridge 참조는 Seoro.Shared에 격리된다.
/// </summary>
public static class CliServiceCollectionExtensions
{
    public static IServiceCollection AddSeoroCliProviders(this IServiceCollection services)
    {
        // AgentBridge 엔진: IAgentProviderFactory + Claude/Codex 프로바이더.
        services.AddAgentBridge().AddClaude().AddCodex();

        // 각 AgentBridge 프로바이더를 Seoro ICliProvider 어댑터로 감싸 등록한다.
        // (CliProviderFactory가 IEnumerable<ICliProvider>를 주입받아 providerId로 매핑한다.)
        services.AddSingleton<ICliProvider>(sp => CreateAdapter(sp, "claude"));
        services.AddSingleton<ICliProvider>(sp => CreateAdapter(sp, "codex"));

        return services;
    }

    private static AgentBridgeCliProvider CreateAdapter(IServiceProvider sp, string providerId)
        => new(
            sp.GetRequiredService<IAgentProviderFactory>().Get(providerId),
            sp.GetRequiredService<IOptionsMonitor<AppSettings>>(),
            sp.GetRequiredService<ILogger<AgentBridgeCliProvider>>());
}
