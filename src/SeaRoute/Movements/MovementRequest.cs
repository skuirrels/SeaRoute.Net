using SeaRoute.Common;

namespace SeaRoute.Movements;

/// <summary>
/// A multi-leg movement to be routed: an ordered list of legs plus the options and lookups needed to route them.
/// </summary>
public sealed class MovementRequest
{
    /// <summary>Ordered legs of the movement.</summary>
    public List<MovementLeg> Legs { get; init; } = [];

    /// <summary>
    /// Coordinates for location codes, keyed case-insensitively by code. Checked before the embedded port
    /// database, so an entry here overrides a port's stored position; the port record is then not attached
    /// to the leg. Use this when the embedded list disagrees with your code conventions, for example CNSHG,
    /// which the list holds as Sanshan rather than the Port of Shanghai.
    /// </summary>
    public Dictionary<string, Coordinate> Coordinates { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Optional resolver consulted last, for codes that are neither in <see cref="Coordinates"/> nor in the
    /// embedded port database.
    /// </summary>
    public ILocationResolver? Resolver { get; set; }

    /// <summary>
    /// Options applied to every sea leg. Units and vessel speed here also govern the totals.
    /// <see cref="SeaRouteOptions.AppendOriginDestination"/> is always treated as true for movement legs so that
    /// consecutive legs join end to end.
    /// </summary>
    public SeaRouteOptions? SeaOptions { get; set; }

    /// <summary>
    /// CO2e intensity per mode used for the emission figures on each leg. Defaults to the GLEC values.
    /// </summary>
    public EmissionFactors Emissions { get; set; } = EmissionFactors.GlecDefaults;

    /// <summary>
    /// Cargo weight in tonnes. When set, each leg and the totals also report absolute CO2e in kilograms;
    /// otherwise only the per-tonne figures are reported.
    /// </summary>
    public double? CargoTonnes { get; set; }

    /// <summary>
    /// Assumed average speed in kilometres per hour for each non-sea mode, used to estimate leg duration.
    /// Defaults: Road 60, Rail 80, Air 800. Sea speed comes from <see cref="SeaOptions"/>.
    /// </summary>
    public Dictionary<TransportMode, double> SpeedsKmh { get; } = new()
    {
        [TransportMode.Road] = 60.0,
        [TransportMode.Rail] = 80.0,
        [TransportMode.Air] = 800.0
    };
}
