using System.Text.Json.Serialization;

namespace SeaRoute.GeoJson;

/// <summary>
/// GeoJSON FeatureCollection, used for multi-leg movements.
/// </summary>
public sealed class GeoJsonFeatureCollection
{
    /// <summary>GeoJSON type: "FeatureCollection".</summary>
    [JsonPropertyName("type")]
    public string Type => "FeatureCollection";

    /// <summary>Member features, one per leg.</summary>
    [JsonPropertyName("features")]
    public List<GeoJsonFeature> Features { get; init; } = [];

    /// <summary>Movement totals, written as a foreign member permitted by RFC 7946 section 6.1.</summary>
    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MovementProperties? Properties { get; init; }

    /// <summary>
    /// Serialises this collection to a GeoJSON string.
    /// </summary>
    public string ToJson(bool writeIndented = false) => GeoJsonSerializer.Serialize(this, writeIndented);

    /// <inheritdoc />
    public override string ToString() => ToJson(false);
}

/// <summary>
/// Totals for a multi-leg movement.
/// </summary>
public sealed class MovementProperties
{
    /// <summary>Sum of leg lengths in <see cref="Units"/>.</summary>
    [JsonPropertyName("total_length")]
    public double TotalLength { get; set; }

    /// <summary>Unit identifier.</summary>
    [JsonPropertyName("units")]
    public string Units { get; set; } = "km";

    /// <summary>Sum of leg durations in hours.</summary>
    [JsonPropertyName("total_duration_hours")]
    public double TotalDurationHours { get; set; }

    /// <summary>Number of legs.</summary>
    [JsonPropertyName("legs")]
    public int LegCount { get; set; }
}
