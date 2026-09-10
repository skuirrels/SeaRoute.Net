using System.Text.Json.Serialization;

namespace SeaRoute.GeoJson;

/// <summary>
/// Represents a GeoJSON Feature containing route geometry and properties.
/// </summary>
public sealed class GeoJsonFeature
{
    /// <summary>GeoJSON type: "Feature".</summary>
    [JsonPropertyName("type")]
    public string Type => "Feature";

    /// <summary>The LineString geometry containing the route coordinates.</summary>
    [JsonPropertyName("geometry")]
    public GeoJsonLineString Geometry { get; init; } = new();

    /// <summary>Calculated properties including length, duration, ports, and passages.</summary>
    [JsonPropertyName("properties")]
    public SeaRouteProperties Properties { get; init; } = new();

    /// <summary>
    /// Serializes this feature to a GeoJSON string.
    /// </summary>
    public string ToJson(bool writeIndented = false) => GeoJsonSerializer.Serialize(this, writeIndented);

    /// <inheritdoc />
    public override string ToString() => ToJson(false);
}
