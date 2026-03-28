using System.Text.Json;
using Cominomi.Shared.Models;
using Microsoft.Extensions.Logging;

namespace Cominomi.Shared.Services.StreamEventHandlers;

internal static class StreamEventUtils
{
    internal static string ExtractToolResultContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? "";

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                    parts.Add(textProp.GetString() ?? "");
            }
            return string.Join("\n", parts);
        }

        return content.ToString();
    }

    internal static decimal? TryExtractCost(StreamEvent evt)
    {
        if (evt.CostUsd is > 0) return evt.CostUsd.Value;
        if (evt.TotalCostUsd is > 0) return evt.TotalCostUsd.Value;

        if (evt.ExtensionData != null)
        {
            if (evt.ExtensionData.TryGetValue("cost_usd", out var costEl) && costEl.TryGetDecimal(out var cost) && cost > 0)
                return cost;
            if (evt.ExtensionData.TryGetValue("total_cost_usd", out var tcEl) && tcEl.TryGetDecimal(out var tc) && tc > 0)
                return tc;
        }
        return null;
    }

    internal static UsageInfo? TryExtractUsageFromExtensionData(StreamEvent evt)
    {
        if (evt.ExtensionData == null) return null;

        int input = 0, output = 0;
        int? cacheCreation = null, cacheRead = null;

        if (evt.ExtensionData.TryGetValue("input_tokens", out var inEl))
            inEl.TryGetInt32(out input);
        if (evt.ExtensionData.TryGetValue("output_tokens", out var outEl))
            outEl.TryGetInt32(out output);
        if (evt.ExtensionData.TryGetValue("cache_creation_input_tokens", out var cwEl) && cwEl.TryGetInt32(out var cw))
            cacheCreation = cw;
        if (evt.ExtensionData.TryGetValue("cache_read_input_tokens", out var crEl) && crEl.TryGetInt32(out var cr))
            cacheRead = cr;

        if (input == 0 && output == 0
            && evt.ExtensionData.TryGetValue("usage", out var usageEl)
            && usageEl.ValueKind == JsonValueKind.Object)
        {
            if (usageEl.TryGetProperty("input_tokens", out var inProp))
                inProp.TryGetInt32(out input);
            if (usageEl.TryGetProperty("output_tokens", out var outProp))
                outProp.TryGetInt32(out output);
            if (usageEl.TryGetProperty("cache_creation_input_tokens", out var cwProp) && cwProp.TryGetInt32(out var cwVal))
                cacheCreation = cwVal;
            if (usageEl.TryGetProperty("cache_read_input_tokens", out var crProp) && crProp.TryGetInt32(out var crVal))
                cacheRead = crVal;
        }

        if (input == 0 && output == 0) return null;

        return new UsageInfo
        {
            InputTokens = input,
            OutputTokens = output,
            CacheCreationInputTokens = cacheCreation,
            CacheReadInputTokens = cacheRead,
        };
    }

}
