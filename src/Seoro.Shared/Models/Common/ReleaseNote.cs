using System.Text.Json.Serialization;

namespace Seoro.Shared.Models.Common;

public record ReleaseChange(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")]
    string Description
);

public record ReleaseNote(
    [property: JsonPropertyName("version")]
    string Version,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("changes")]
    List<ReleaseChange> Changes
);