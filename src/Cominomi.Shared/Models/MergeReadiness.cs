using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MergeReadiness
{
    Unknown,
    NoPr,
    ChecksPending,
    ChecksFailed,
    Mergeable,
    Conflict,
    Merged
}
