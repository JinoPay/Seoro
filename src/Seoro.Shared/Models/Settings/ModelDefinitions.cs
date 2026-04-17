using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Settings;

public record ModelInfo(string Id, string DisplayName)
{
    [JsonPropertyName("pricing")] public ModelPricing? Pricing { get; init; }

    [JsonPropertyName("contextWindow")] public int ContextWindow { get; init; } = 200_000;

    [JsonPropertyName("keywords")] public string[] Keywords { get; init; } = [];

    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>1 = 느림/강력함, 2 = 균형잡힘, 3 = 빠름</summary>
    [JsonPropertyName("speedTier")]
    public int SpeedTier { get; init; } = 2;

    [JsonPropertyName("isAlias")] public bool IsAlias { get; init; }

    /// <summary>"standard", "extended", "hybrid", "alias" 중 하나</summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>이 모델을 지원하는 프로바이더 ID. "claude" 또는 "codex".</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "claude";
}

public record ModelPricing(
    [property: JsonPropertyName("input")] decimal Input,
    [property: JsonPropertyName("output")] decimal Output,
    [property: JsonPropertyName("cacheWrite")] decimal CacheWrite,
    [property: JsonPropertyName("cacheRead")] decimal CacheRead,
    [property: JsonPropertyName("extendedInput")] decimal? ExtendedInput = null,
    [property: JsonPropertyName("extendedOutput")] decimal? ExtendedOutput = null,
    [property: JsonPropertyName("extendedCacheWrite")] decimal? ExtendedCacheWrite = null,
    [property: JsonPropertyName("extendedCacheRead")] decimal? ExtendedCacheRead = null,
    [property: JsonPropertyName("extendedThreshold")] int ExtendedThreshold = 200_000);

public record ModelConfig
{
    [JsonPropertyName("models")] public ModelInfo[] Models { get; init; } = [];

    [JsonPropertyName("defaultModelId")] public string DefaultModelId { get; init; } = "opus";

    [JsonPropertyName("defaultPricingFallbackId")]
    public string DefaultPricingFallbackId { get; init; } = "sonnet";
}

public static class ModelDefinitions
{
    private static ModelConfig _config = CreateDefaultConfig();
    private static bool _isCliV47 = false;
    private static bool _customConfigLoaded = false;

    public static ModelInfo Default => All.FirstOrDefault(m => m.Id == _config.DefaultModelId) ?? All[0];
    public static ModelInfo DefaultCodex => All.FirstOrDefault(m => m.Provider == "codex") ?? Default;

    public static ModelInfo[] All => _config.Models;
    public static string PricingFallbackId => _config.DefaultPricingFallbackId;

    public static int GetContextWindow(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        var model = All.FirstOrDefault(m => m.Id == normalized);
        return model?.ContextWindow ?? 200_000;
    }

    public static bool SupportsMaxEffort(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        return normalized is "opus" or "opus[1m]" or "opusplan";
    }

    public static bool SupportsXHighEffort(string modelId)
    {
        return _isCliV47 && SupportsMaxEffort(modelId);
    }

    public static string GetMaxEffortLevel(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        if (normalized is not ("opus" or "opus[1m]" or "opusplan"))
            return "high";
        return _isCliV47 ? "xhigh" : "max";
    }

    /// <summary>
    ///     CLI 버전에 따라 모델 표시명과 컨텍스트를 갱신합니다.
    ///     CLI >= 2.1.111이면 Opus 4.7 / Sonnet 4.6으로 업데이트합니다.
    /// </summary>
    public static void ApplyCliVersion(string cliVersion)
    {
        // models.json 커스텀 파일이 로드된 경우 버전 분기 적용 안 함
        if (_customConfigLoaded) return;

        _isCliV47 = !VersionComparer.IsOutdated(cliVersion, SeoroConstants.Claude47MinVersion);
        if (!_isCliV47) return;

        var updated = _config.Models.Select(m => m.Id switch
        {
            "opus" => m with
            {
                DisplayName = "Claude Opus 4.7",
                ContextWindow = 1_000_000,
                Description = "최고 지능, 적응형 사고 (Opus 4.7)"
            },
            "sonnet" => m with { DisplayName = "Claude Sonnet 4.6" },
            "opus[1m]" => m with { DisplayName = "Opus 4.7 (Extended 1M)" },
            "sonnet[1m]" => m with { DisplayName = "Sonnet 4.6 (Extended 1M)" },
            "opusplan" => m with { Description = "Opus 4.7이 플래닝, Sonnet 4.6이 실행" },
            _ => m
        }).ToArray();

        _config = _config with { Models = updated };
    }

    public static ModelPricing? GetPricing(string modelId)
    {
        var normalized = NormalizeModelId(modelId);
        var model = All.FirstOrDefault(m => m.Id == normalized);
        if (model?.Pricing != null) return model.Pricing;

        // 폴백
        return All.FirstOrDefault(m => m.Id == PricingFallbackId)?.Pricing;
    }

    public static string NormalizeModelId(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return Default.Id;
        if (All.Any(m => m.Id == modelId)) return modelId;

        // 키워드 기반 정규화
        foreach (var model in All)
        foreach (var keyword in model.Keywords)
            if (modelId.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return model.Id;

        // 사용자 정의 API 모델 ID는 있는 그대로 유지
        if (modelId.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("o4", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ||
            modelId.StartsWith("codex-", StringComparison.OrdinalIgnoreCase))
            return modelId;

        // 알 수 없는 짧은 문자열 (예: "Model") — 기본값으로 정규화
        return Default.Id;
    }

    /// <summary>
    ///     JSON 파일에서 모델을 로드합니다. 파일이 없거나 유효하지 않은 경우 내장 기본값으로 폴백합니다.
    /// </summary>
    public static async Task LoadFromFileAsync(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var config = JsonSerializer.Deserialize<ModelConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (config?.Models is { Length: > 0 })
            {
                _config = config;
                _customConfigLoaded = true;
            }
        }
        catch
        {
            // 내장 기본값 유지
        }
    }

    /// <summary>
    ///     적용 가능한 경우 계층형 가격 책정을 사용하여 예상 비용을 계산합니다.
    ///     확장 컨텍스트 모델(예: opus[1m])의 경우 입력 토큰이 임계값(200K)을 초과하는 세션은
    ///     분할됩니다: 처음 200K 토큰은 표준 요금을 사용하고 나머지는 확장 요금을 사용합니다.
    ///     이는 Anthropic의 요청당 전부 또는 무 요금 청구를 세션 수준에서 근사합니다.
    /// </summary>
    public static decimal CalculateTieredCost(
        ModelPricing pricing,
        long inputTokens,
        long outputTokens,
        long cacheCreationTokens = 0,
        long cacheReadTokens = 0)
    {
        if (pricing.ExtendedInput == null || inputTokens <= pricing.ExtendedThreshold)
        {
            // 정액 요금: 표준 모델 또는 임계값 이하인 확장 모델
            return (inputTokens * pricing.Input
                    + outputTokens * pricing.Output
                    + cacheCreationTokens * pricing.CacheWrite
                    + cacheReadTokens * pricing.CacheRead) / 1_000_000m;
        }

        // 계층형: 임계값에서 입력 분할
        var stdInputTokens = (decimal)pricing.ExtendedThreshold;
        var extInputTokens = (decimal)(inputTokens - pricing.ExtendedThreshold);
        var totalInput = (decimal)inputTokens;

        // 출력/캐시 분할은 확장된 입력의 비율에 비례
        var extRatio = extInputTokens / totalInput;
        var stdRatio = 1m - extRatio;

        var extOutput = pricing.ExtendedOutput ?? pricing.Output;
        var extCacheWrite = pricing.ExtendedCacheWrite ?? pricing.CacheWrite;
        var extCacheRead = pricing.ExtendedCacheRead ?? pricing.CacheRead;

        return (stdInputTokens * pricing.Input
                + extInputTokens * pricing.ExtendedInput.Value
                + outputTokens * stdRatio * pricing.Output
                + outputTokens * extRatio * extOutput
                + cacheCreationTokens * stdRatio * pricing.CacheWrite
                + cacheCreationTokens * extRatio * extCacheWrite
                + cacheReadTokens * stdRatio * pricing.CacheRead
                + cacheReadTokens * extRatio * extCacheRead) / 1_000_000m;
    }

    /// <summary>팝오버 표시 그룹화를 위해 "실제" (별칭 제외) 모델만 검색하는 헬퍼.</summary>
    public static IEnumerable<IGrouping<string?, ModelInfo>> GetGroupedModels(string? provider = null)
    {
        var models = provider is null ? All : All.Where(m => m.Provider == provider);
        return models.GroupBy(m => m.Category);
    }

    private static ModelConfig CreateDefaultConfig()
    {
        return new ModelConfig
        {
            DefaultModelId = "opus",
            DefaultPricingFallbackId = "sonnet",
            Models =
            [
                // ── 표준 ──
                new ModelInfo("opus", "Claude Opus 4.6")
                {
                    Keywords = ["opus"],
                    Pricing = new ModelPricing(5.0m, 25.0m, 6.25m, 0.50m),
                    ContextWindow = 200_000,
                    Description = "최고 지능, 적응형 사고",
                    SpeedTier = 1,
                    Category = "standard"
                },
                new ModelInfo("sonnet", "Claude Sonnet 4.5")
                {
                    Keywords = ["sonnet"],
                    Pricing = new ModelPricing(3.0m, 15.0m, 3.75m, 0.30m),
                    ContextWindow = 200_000,
                    Description = "속도와 지능의 균형",
                    SpeedTier = 2,
                    Category = "standard"
                },
                new ModelInfo("haiku", "Claude Haiku 4.5")
                {
                    Keywords = ["haiku"],
                    Pricing = new ModelPricing(1.0m, 5.0m, 1.25m, 0.10m),
                    ContextWindow = 200_000,
                    Description = "빠르고 가벼운 모델",
                    SpeedTier = 3,
                    Category = "standard"
                },
                // ── 확장 컨텍스트 (1M) ──
                // Anthropic 가격 책정 문서의 확장 가격:
                // 입력 >200K: 2배 표준. 출력 >200K: 1.5배 표준.
                // 캐시 배수 (1.25배 쓰기, 0.1배 읽기)는 확장 기본 위에 적용.
                new ModelInfo("opus[1m]", "Opus (Extended 1M)")
                {
                    Keywords = ["opus[1m]"],
                    Pricing = new ModelPricing(5.0m, 25.0m, 6.25m, 0.50m,
                        ExtendedInput: 10.0m, ExtendedOutput: 37.5m,
                        ExtendedCacheWrite: 12.5m, ExtendedCacheRead: 1.0m),
                    ContextWindow = 1_000_000,
                    Description = "Opus + 1M 컨텍스트 윈도우",
                    SpeedTier = 1,
                    Category = "extended"
                },
                new ModelInfo("sonnet[1m]", "Sonnet (Extended 1M)")
                {
                    Keywords = ["sonnet[1m]"],
                    Pricing = new ModelPricing(3.0m, 15.0m, 3.75m, 0.30m,
                        ExtendedInput: 6.0m, ExtendedOutput: 22.5m,
                        ExtendedCacheWrite: 7.5m, ExtendedCacheRead: 0.60m),
                    ContextWindow = 1_000_000,
                    Description = "Sonnet + 1M 컨텍스트 윈도우",
                    SpeedTier = 2,
                    Category = "extended"
                },
                // ── 하이브리드 / 별칭 ──
                new ModelInfo("opusplan", "Opus Plan")
                {
                    Keywords = ["opusplan"],
                    Description = "Opus가 플래닝, Sonnet이 실행",
                    SpeedTier = 1,
                    IsAlias = true,
                    Category = "hybrid"
                },
                new ModelInfo("default", "Default")
                {
                    Keywords = ["default"],
                    Description = "Anthropic 권장 모델 자동 선택",
                    SpeedTier = 2,
                    IsAlias = true,
                    Category = "alias"
                },
                // ── Codex / OpenAI ──
                new ModelInfo("gpt-5.4", "GPT-5.4")
                {
                    Keywords = ["gpt-5.4"],
                    Pricing = new ModelPricing(2.50m, 15.0m, 0m, 0.25m),
                    ContextWindow = 1_000_000,
                    Description = "플래그십 코딩+추론+에이전트 모델",
                    SpeedTier = 1,
                    Provider = "codex",
                    Category = "standard"
                },
                new ModelInfo("gpt-5.4-mini", "GPT-5.4 Mini")
                {
                    Keywords = ["gpt-5.4-mini"],
                    Pricing = new ModelPricing(0.75m, 4.50m, 0m, 0.075m),
                    ContextWindow = 400_000,
                    Description = "빠른 코딩, 서브에이전트용",
                    SpeedTier = 3,
                    Provider = "codex",
                    Category = "standard"
                },
                new ModelInfo("gpt-5.3-codex", "GPT-5.3 Codex")
                {
                    Keywords = ["gpt-5.3-codex", "codex"],
                    Pricing = new ModelPricing(1.75m, 14.0m, 0m, 0.175m),
                    ContextWindow = 400_000,
                    Description = "복잡한 소프트웨어 엔지니어링 특화",
                    SpeedTier = 1,
                    Provider = "codex",
                    Category = "standard"
                },
new ModelInfo("gpt-5.2", "GPT-5.2")
                {
                    Keywords = ["gpt-5.2"],
                    Pricing = new ModelPricing(1.75m, 14.0m, 0m, 0.175m),
                    ContextWindow = 400_000,
                    Description = "디버깅, 깊은 추론에 강점",
                    SpeedTier = 1,
                    Provider = "codex",
                    Category = "standard"
                },
            ]
        };
    }
}