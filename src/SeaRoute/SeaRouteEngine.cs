using SeaRoute.Common;
using SeaRoute.Data;
using SeaRoute.GeoJson;
using SeaRoute.Graph;
using SeaRoute.Passages;
using SeaRoute.Ports;

namespace SeaRoute;

/// <summary>
/// Default implementation of the <see cref="ISeaRouteEngine"/> maritime navigation engine.
/// Thread-safe and designed for high-concurrency routing.
/// </summary>
public sealed class SeaRouteEngine : ISeaRouteEngine
{
    private static readonly Lazy<SeaRouteEngine> LazyDefault = new(() => new SeaRouteEngine());

    /// <summary>
    /// Default global instance of <see cref="SeaRouteEngine"/> with embedded Marnet and Ports datasets.
    /// </summary>
    public static SeaRouteEngine Default => LazyDefault.Value;

    private readonly Lazy<MaritimeGraph> _lazyGraph;
    private readonly Lazy<PortDatabase> _lazyPorts;

    /// <inheritdoc />
    public MaritimeGraph Graph => _lazyGraph.Value;

    /// <inheritdoc />
    public PortDatabase Ports => _lazyPorts.Value;

    /// <summary>
    /// Creates a new instance of <see cref="SeaRouteEngine"/> using default embedded datasets.
    /// </summary>
    public SeaRouteEngine()
        : this(EmbeddedResources.LoadMaritimeGraph, EmbeddedResources.LoadPortDatabase)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="SeaRouteEngine"/> with custom graph and port instances.
    /// </summary>
    public SeaRouteEngine(MaritimeGraph graph, PortDatabase ports)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(ports);
        _lazyGraph = new Lazy<MaritimeGraph>(() => graph);
        _lazyPorts = new Lazy<PortDatabase>(() => ports);
    }

    /// <summary>
    /// Creates a new instance of <see cref="SeaRouteEngine"/> with custom factory delegates.
    /// </summary>
    public SeaRouteEngine(Func<MaritimeGraph> graphFactory, Func<PortDatabase> portsFactory)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(portsFactory);
        _lazyGraph = new Lazy<MaritimeGraph>(graphFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        _lazyPorts = new Lazy<PortDatabase>(portsFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(Coordinate origin, Coordinate destination, SeaRouteOptions? options = null)
    {
        var routes = CalculateRoutes(origin, destination, options);
        return routes[0];
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(double originLon, double originLat, double destLon, double destLat, SeaRouteOptions? options = null)
    {
        return CalculateRoute(new Coordinate(originLon, originLat), new Coordinate(destLon, destLat), options);
    }

    /// <inheritdoc />
    public GeoJsonFeature CalculateRoute(string originPortCode, string destPortCode, SeaRouteOptions? options = null)
    {
        var originPort = Ports.GetByCode(originPortCode)
            ?? throw new ArgumentException($"Port '{originPortCode}' not found in port database.", nameof(originPortCode));
        var destPort = Ports.GetByCode(destPortCode)
            ?? throw new ArgumentException($"Port '{destPortCode}' not found in port database.", nameof(destPortCode));

        var feature = CalculateRoute(originPort.Coordinate, destPort.Coordinate, options);
        feature.Properties.PortOrigin = originPort;
        feature.Properties.PortDest = destPort;
        return feature;
    }

    /// <inheritdoc />
    public IReadOnlyList<GeoJsonFeature> CalculateRoutes(Coordinate origin, Coordinate destination, SeaRouteOptions? options = null)
    {
        origin.Validate();
        destination.Validate();

        options ??= new SeaRouteOptions();
        var units = options.Units;
        double speedKnots = options.SpeedKnots;
        bool appendOrigDest = options.AppendOriginDestination;
        bool includePorts = options.IncludePorts;
        bool returnPassages = options.ReturnPassages;
        var restrictions = options.Restrictions;
        string algorithm = options.Algorithm;

        List<(Port? OriginPort, Port? DestPort)> portMatrix;
        if (includePorts)
        {
            var matrix = Ports.GetSelectedPortMatrix(origin, destination, options.PortParameters);
            if (matrix.Count == 0)
            {
                portMatrix = [(null, null)];
            }
            else
            {
                portMatrix = matrix.Select(m => ((Port?)m.OriginPort, (Port?)m.DestPort)).ToList();
            }
        }
        else
        {
            portMatrix = [(null, null)];
        }

        var results = new List<GeoJsonFeature>(portMatrix.Count);

        foreach (var (pFrom, pTo) in portMatrix)
        {
            var routedOrigin = pFrom != null ? pFrom.Coordinate : origin;
            var routedDest = pTo != null ? pTo.Coordinate : destination;

            var (lengthKm, shortestPath) = Graph.ShortestPath(routedOrigin, routedDest, restrictions, algorithm);

            List<Coordinate> routeCoords;
            if (shortestPath == null || double.IsInfinity(lengthKm) || shortestPath.Count == 0)
            {
                routeCoords = [];
            }
            else
            {
                routeCoords = shortestPath; // freshly allocated per query, safe to take ownership

                if (includePorts && routeCoords.Count > 0)
                {
                    if (routeCoords[0] != routedOrigin)
                        routeCoords.Insert(0, routedOrigin);
                    if (routeCoords[^1] != routedDest)
                        routeCoords.Add(routedDest);
                }

                if (appendOrigDest && routeCoords.Count > 0)
                {
                    if (routeCoords[0] != origin)
                        routeCoords.Insert(0, origin);
                    if (routeCoords[^1] != destination)
                        routeCoords.Add(destination);
                }
            }

            var (normalizedCoords, traversedPassages) = RouteNormalizer.ProcessRoute(routeCoords, Graph, returnPassages);

            double totalLength = normalizedCoords.Count > 1
                ? Haversine.CalculatePathLength(normalizedCoords, units)
                : 0.0;
            double duration = totalLength > 0 ? Haversine.CalculateDurationHours(speedKnots, totalLength, units) : 0.0;

            var feature = new GeoJsonFeature
            {
                Geometry = GeoJsonLineString.FromCoordinates(normalizedCoords),
                Properties = new SeaRouteProperties
                {
                    Length = totalLength,
                    Units = units.ToUnitString(),
                    DurationHours = duration,
                    PortOrigin = includePorts && pFrom != null ? pFrom : null,
                    PortDest = includePorts && pTo != null ? pTo : null,
                    TraversedPassages = returnPassages ? Passage.FilterValidPassages(traversedPassages) : null
                }
            };

            results.Add(feature);
        }

        return results;
    }
}
