using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Common;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentType
{
    Code,
    Explore,
    Plan
}