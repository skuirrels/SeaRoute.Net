using SeaRoute.Common;
using SeaRoute.GeoJson;
using SeaRoute.Passages;
using SeaRoute.Ports;

namespace SeaRoute;

/// <summary>
/// Static entrypoint for calculating maritime sea routes between any two points on Earth.
/// </summary>
public static class SeaRouter
{
    private static ISeaRouteEngine? _customEngine;

    /// <summary>
    /// Gets or sets the active sea route engine. Defaults to <see cref="SeaRouteEngine.Default"/>.
    /// </summary>
    public static ISeaRouteEngine Engine
    {
        get => _customEngine ?? SeaRouteEngine.Default;
        set => _customEngine = value;
    }

    /// <summary>
    /// Resets the engine back to the default singleton instance.
    /// </summary>
    public static void ResetEngine() => _customEngine = null;

    /// <summary>
    /// Calculates the shortest sea route between two coordinates using default options.
    /// </summary>
    public static GeoJsonFeature Calculate(Coordinate origin, Coordinate destination)
    {
        return Engine.CalculateRoute(origin, destination, null);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates using the specified options.
    /// </summary>
    public static GeoJsonFeature Calculate(Coordinate origin, Coordinate destination, SeaRouteOptions options)
    {
        return Engine.CalculateRoute(origin, destination, options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates specified as (lon, lat) tuples.
    /// </summary>
    public static GeoJsonFeature Calculate(
        (double Longitude, double Latitude) origin,
        (double Longitude, double Latitude) destination,
        SeaRouteOptions? options = null)
    {
        return Engine.CalculateRoute(
            new Coordinate(origin.Longitude, origin.Latitude),
            new Coordinate(destination.Longitude, destination.Latitude),
            options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two coordinates specified as individual longitude/latitude doubles.
    /// </summary>
    public static GeoJsonFeature Calculate(
        double originLon,
        double originLat,
        double destLon,
        double destLat,
        SeaRouteOptions? options = null)
    {
        return Engine.CalculateRoute(new Coordinate(originLon, originLat), new Coordinate(destLon, destLat), options);
    }

    /// <summary>
    /// Calculates the shortest sea route between two ports identified by their UN/LOCODE or port code.
    /// </summary>
    public static GeoJsonFeature Calculate(
        string originPortCode,
        string destPortCode,
        SeaRouteOptions? options = null)
    {
        return Engine.CalculateRoute(originPortCode, destPortCode, options);
    }

    /// <summary>
    /// Calculates the shortest sea route with explicit parameters.
    /// </summary>
    public static GeoJsonFeature Calculate(
        Coordinate origin,
        Coordinate destination,
        DistanceUnit units = DistanceUnit.Km,
        double speedKnots = 24.0,
        bool appendOrigDest = false,
        IEnumerable<string>? restrictions = null,
        bool includePorts = false,
        PortParameters? portParams = null,
        bool returnPassages = false,
        string algorithm = "dijkstra")
    {
        var options = new SeaRouteOptions
        {
            Units = units,
            SpeedKnots = speedKnots,
            AppendOriginDestination = appendOrigDest,
            IncludePorts = includePorts,
            PortParameters = portParams,
            ReturnPassages = returnPassages,
            Algorithm = algorithm
        };

        if (restrictions != null)
        {
            options.Restrictions = new HashSet<string>(restrictions, StringComparer.OrdinalIgnoreCase);
        }

        return Engine.CalculateRoute(origin, destination, options);
    }

    /// <summary>
    /// Calculates sea routes returning multiple features if area matrices match multiple port combinations.
    /// </summary>
    public static IReadOnlyList<GeoJsonFeature> CalculateRoutes(
        Coordinate origin,
        Coordinate destination,
        SeaRouteOptions? options = null)
    {
        return Engine.CalculateRoutes(origin, destination, options);
    }
}
