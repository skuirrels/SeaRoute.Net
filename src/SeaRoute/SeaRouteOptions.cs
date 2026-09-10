using SeaRoute.Common;
using SeaRoute.Passages;
using SeaRoute.Ports;

namespace SeaRoute;

/// <summary>
/// Options configuring sea route calculations.
/// </summary>
public sealed class SeaRouteOptions
{
    /// <summary>Unit for measuring route distance. Defaults to <see cref="DistanceUnit.Km"/>.</summary>
    public DistanceUnit Units { get; set; } = DistanceUnit.Km;

    /// <summary>Unit as string (e.g., "km", "mi", "naut"). Setting this updates <see cref="Units"/>.</summary>
    public string UnitString
    {
        get => Units.ToUnitString();
        set => Units = DistanceUnitExtensions.Parse(value);
    }

    /// <summary>Vessel speed in knots (nautical miles per hour). Default is 24 knots.</summary>
    public double SpeedKnots { get; set; } = 24.0;

    /// <summary>
    /// Whether to explicitly prepend the origin and append the destination coordinates to the route LineString.
    /// Default is false.
    /// </summary>
    public bool AppendOriginDestination { get; set; }

    /// <summary>
    /// Passages to avoid (e.g. Suez, Panama, Gibraltar).
    /// Default is [Passage.Northwest].
    /// </summary>
    public HashSet<string> Restrictions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        Passage.Northwest
    };

    /// <summary>Whether to route through nearest ports close to origin and destination. Default is false.</summary>
    public bool IncludePorts { get; set; }

    /// <summary>Optional port selection and filtering parameters.</summary>
    public PortParameters? PortParameters { get; set; }

    /// <summary>Whether to return the list of traversed passages in the route properties. Default is false.</summary>
    public bool ReturnPassages { get; set; }

    /// <summary>Pathfinding algorithm: "dijkstra" (default) or "astar".</summary>
    public string Algorithm { get; set; } = "dijkstra";
}
