using SeaRoute.Common;
using SeaRoute.GeoJson;
using SeaRoute.Graph;
using SeaRoute.Ports;

namespace SeaRoute;

/// <summary>
/// Service interface for maritime sea route calculation and network querying.
/// </summary>
public interface ISeaRouteEngine
{
    /// <summary>
    /// Gets the underlying maritime navigation graph.
    /// </summary>
    MaritimeGraph Graph { get; }

    /// <summary>
    /// Gets the port database indexed spatially.
    /// </summary>
    PortDatabase Ports { get; }

    /// <summary>
    /// Calculates the shortest maritime route between origin and destination coordinates.
    /// </summary>
    GeoJsonFeature CalculateRoute(Coordinate origin, Coordinate destination, SeaRouteOptions? options = null);

    /// <summary>
    /// Calculates maritime route(s) between origin and destination coordinates.
    /// When area-based port matrices match multiple ports, multiple features are returned.
    /// </summary>
    IReadOnlyList<GeoJsonFeature> CalculateRoutes(Coordinate origin, Coordinate destination, SeaRouteOptions? options = null);

    /// <summary>
    /// Calculates the shortest maritime route between origin and destination coordinates specified as lon/lat.
    /// </summary>
    GeoJsonFeature CalculateRoute(double originLon, double originLat, double destLon, double destLat, SeaRouteOptions? options = null);

    /// <summary>
    /// Calculates the shortest maritime route between two port codes (e.g. UN/LOCODE like "FRLEH", "CNTSN").
    /// </summary>
    GeoJsonFeature CalculateRoute(string originPortCode, string destPortCode, SeaRouteOptions? options = null);
}
