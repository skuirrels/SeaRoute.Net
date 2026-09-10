using System.Text.Json.Serialization;
using SeaRoute.Ports;

namespace SeaRoute.GeoJson;

/// <summary>
/// Properties associated with a calculated sea route GeoJSON feature.
/// </summary>
public sealed class SeaRouteProperties
{
    /// <summary>Total route length in the requested units.</summary>
    [JsonPropertyName("length")]
    public double Length { get; set; }

    /// <summary>Unit used for the length measurement (e.g. "km", "naut", "mi").</summary>
    [JsonPropertyName("units")]
    public string Units { get; set; } = "km";

    /// <summary>Estimated voyage duration in hours.</summary>
    [JsonPropertyName("duration_hours")]
    public double DurationHours { get; set; }

    /// <summary>Origin port details if ports are included.</summary>
    [JsonPropertyName("port_origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Port? PortOrigin { get; set; }

    /// <summary>Destination port details if ports are included.</summary>
    [JsonPropertyName("port_dest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Port? PortDest { get; set; }

    /// <summary>List of passages, straits, or canals traversed along the route.</summary>
    [JsonPropertyName("traversed_passages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TraversedPassages { get; set; }

    /// <summary>1-based leg number when the feature is part of a multi-leg movement.</summary>
    [JsonPropertyName("leg")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Leg { get; set; }

    /// <summary>Transport mode of the leg: "sea", "road", "rail" or "air".</summary>
    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    /// <summary>Role of the leg: "main", "pickup" or "delivery".</summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>Code or name of the leg's start location.</summary>
    [JsonPropertyName("from")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? From { get; set; }

    /// <summary>Code or name of the leg's end location.</summary>
    [JsonPropertyName("to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? To { get; set; }
}
