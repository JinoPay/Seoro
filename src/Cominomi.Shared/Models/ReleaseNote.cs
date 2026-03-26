using System.Text.Json.Serialization;

namespace Cominomi.Shared.Models;

public record ReleaseChange(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("description")] string Description
);

public record ReleaseNote(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("changes")] List<ReleaseChange> Changes
);
