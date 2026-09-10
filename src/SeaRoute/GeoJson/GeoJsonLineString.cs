using System.Text.Json.Serialization;
using SeaRoute.Common;

namespace SeaRoute.GeoJson;

/// <summary>
/// GeoJSON LineString geometry.
/// </summary>
public sealed class GeoJsonLineString
{
    /// <summary>Type of geometry: "LineString".</summary>
    [JsonPropertyName("type")]
    public string Type => "LineString";

    /// <summary>List of coordinates as [longitude, latitude] arrays.</summary>
    [JsonPropertyName("coordinates")]
    public List<double[]> Coordinates { get; init; } = [];

    /// <summary>
    /// Creates a LineString from Coordinate objects.
    /// </summary>
    public static GeoJsonLineString FromCoordinates(IEnumerable<Coordinate> coords)
    {
        if (coords is IReadOnlyList<Coordinate> list)
        {
            var result = new List<double[]>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                result.Add([list[i].Longitude, list[i].Latitude]);
            }
            return new GeoJsonLineString { Coordinates = result };
        }

        var res = new List<double[]>();
        foreach (var c in coords)
        {
            res.Add([c.Longitude, c.Latitude]);
        }
        return new GeoJsonLineString { Coordinates = res };
    }
}
