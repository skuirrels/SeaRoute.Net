using SeaRoute.Common;
using SeaRoute.Data;
using SeaRoute.GeoJson;
using SeaRoute.Graph;
using SeaRoute.Movements;
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
                // A one-node path means both endpoints snap to the same network node. Emit a straight line
                // between the routed endpoints so the LineString always has two positions and a real length.
                routeCoords = shortestPath.Count == 1
                    ? [routedOrigin, routedDest]
                    : shortestPath; // freshly allocated per query, safe to take ownership

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

    /// <inheritdoc />
    public MovementResult CalculateMovement(MovementRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Legs.Count == 0)
            throw new ArgumentException("A movement needs at least one leg.", nameof(request));
        if (request.SpeedsKmh.ContainsKey(TransportMode.Sea))
            throw new ArgumentException("Sea speed is taken from SeaOptions.SpeedKnots; remove the Sea entry from SpeedsKmh.", nameof(request));

        // Movement legs always include their resolved endpoints so consecutive legs join end to end.
        var seaOptions = (request.SeaOptions ?? new SeaRouteOptions()).Clone();
        seaOptions.AppendOriginDestination = true;
        var units = seaOptions.Units;

        var resolvedByCode = new Dictionary<string, ResolvedLocation>(StringComparer.OrdinalIgnoreCase);
        var legResults = new List<LegResult>(request.Legs.Count);

        for (int i = 0; i < request.Legs.Count; i++)
        {
            var leg = request.Legs[i];
            int sequence = i + 1;
            var from = ResolveLocation(leg.From, request, resolvedByCode);
            var to = ResolveLocation(leg.To, request, resolvedByCode);

            var feature = leg.Mode == TransportMode.Sea
                ? CalculateSeaLeg(sequence, leg, from, to, seaOptions)
                : CalculateStraightLeg(from, to, leg.Mode, units, request.SpeedsKmh);

            feature.Properties.Leg = sequence;
            feature.Properties.Mode = leg.Mode.ToWireString();
            feature.Properties.Kind = leg.Kind.ToWireString();
            feature.Properties.From = from.Label;
            feature.Properties.To = to.Label;
            feature.Properties.PortHours = leg.Mode == TransportMode.Sea ? 2.0 * request.PortDwellHours : 0.0;
            feature.Properties.TransitHours = feature.Properties.DurationHours + feature.Properties.PortHours;
            ApplyEmissions(feature, leg.Mode, units, request);

            legResults.Add(new LegResult(sequence, leg, from, to, feature));
        }

        return new MovementResult(legResults, units.ToUnitString(), request.CargoTonnes, request.CargoTeu);
    }

    /// <summary>
    /// Stamps the leg with its GLEC-style CO2e figures: intensity in g per tonne-km, kg per tonne of cargo for
    /// the leg, and absolute kg when the request states a cargo weight. Length is converted to kilometres first.
    /// </summary>
    private static void ApplyEmissions(GeoJsonFeature feature, TransportMode mode, DistanceUnit units, MovementRequest request)
    {
        var factors = request.Emissions;
        double lengthKm = feature.Properties.Length / (units.GetConversionFactorFromMeters() * 1000.0);
        double gramsPerTonneKm = factors.GramsPerTonneKm(mode, lengthKm);
        double kgPerTonne = gramsPerTonneKm * lengthKm / 1000.0;

        feature.Properties.Co2eGramsPerTonneKm = gramsPerTonneKm;
        feature.Properties.Co2eKgPerTonne = kgPerTonne;

        if (mode == TransportMode.Sea && request.CargoTeu.HasValue)
        {
            // Sea legs are charged per container when a TEU count is known: a light box still moves a whole slot.
            feature.Properties.Co2eGramsPerTeuKm = factors.SeaGramsPerTeuKm;
            feature.Properties.Co2eKg = factors.SeaGramsPerTeuKm * request.CargoTeu.Value * lengthKm / 1000.0;
            feature.Properties.Co2eBasis = "teu";
            return;
        }

        if (request.CargoTonnes.HasValue)
        {
            feature.Properties.Co2eKg = kgPerTonne * request.CargoTonnes.Value;
            feature.Properties.Co2eBasis = "tonnes";
        }
        else if (request.CargoTeu.HasValue)
        {
            feature.Properties.Co2eKg = kgPerTonne * request.CargoTeu.Value * factors.AverageTonnesPerTeu;
            feature.Properties.Co2eBasis = "teu_average_weight";
        }
    }

    private GeoJsonFeature CalculateSeaLeg(int sequence, MovementLeg leg, ResolvedLocation from, ResolvedLocation to, SeaRouteOptions options)
    {
        var feature = CalculateRoute(from.Coordinate, to.Coordinate, options);

        if (feature.Geometry.Coordinates.Count < 2)
        {
            throw new InvalidOperationException(
                $"Leg {sequence} ({leg}) has no sea route between {from.Label} and {to.Label} under the current passage restrictions.");
        }

        feature.Properties.PortOrigin ??= from.Port;
        feature.Properties.PortDest ??= to.Port;
        return feature;
    }

    private static GeoJsonFeature CalculateStraightLeg(
        ResolvedLocation from,
        ResolvedLocation to,
        TransportMode mode,
        DistanceUnit units,
        IReadOnlyDictionary<TransportMode, double> speedsKmh)
    {
        if (!speedsKmh.TryGetValue(mode, out double speedKmh) || speedKmh <= 0)
            throw new ArgumentException($"No positive speed configured for mode {mode}. Set MovementRequest.SpeedsKmh[{mode}].");

        var coords = RouteNormalizer.NormalizeRoute([from.Coordinate, to.Coordinate]);
        double length = Haversine.CalculatePathLength(coords, units);

        // Reuse the sea-leg duration helper by expressing the road speed in knots.
        double speedKnots = speedKmh / DistanceUnit.Km.GetSpeedCoefficient();

        return new GeoJsonFeature
        {
            Geometry = GeoJsonLineString.FromCoordinates(coords),
            Properties = new SeaRouteProperties
            {
                Length = length,
                Units = units.ToUnitString(),
                DurationHours = Haversine.CalculateDurationHours(speedKnots, length, units),
                PortOrigin = from.Port,
                PortDest = to.Port
            }
        };
    }

    private ResolvedLocation ResolveLocation(
        Location location,
        MovementRequest request,
        Dictionary<string, ResolvedLocation> resolvedByCode)
    {
        bool cacheable = location.Code != null && !location.Coordinate.HasValue;
        if (cacheable && resolvedByCode.TryGetValue(location.Code!, out var cached))
            return cached;

        var resolved = ResolveUncached(location, request);
        resolved.Coordinate.Validate();

        if (cacheable)
            resolvedByCode[location.Code!] = resolved;

        return resolved;
    }

    /// <summary>
    /// Resolution order: explicit coordinate on the location, then <see cref="MovementRequest.Coordinates"/>,
    /// then the embedded port database, then <see cref="MovementRequest.Resolver"/>.
    /// </summary>
    private ResolvedLocation ResolveUncached(Location location, MovementRequest request)
    {
        Port? port = location.Code != null ? Ports.GetByCode(location.Code) : null;

        if (location.Coordinate.HasValue)
            return new ResolvedLocation(location.Code, location.Name ?? port?.Name, location.Coordinate.Value, port);

        string code = location.Code!;

        // A caller-supplied coordinate wins outright. The embedded port record is not attached, because its
        // stored position may differ from the override (the dataset's CNSHG is Sanshan, not Shanghai).
        if (request.Coordinates.TryGetValue(code, out var overridden))
            return new ResolvedLocation(code, null, overridden, null);

        if (port != null)
            return new ResolvedLocation(code, port.Name, port.Coordinate, port);

        if (request.Resolver != null && request.Resolver.TryResolve(code, out var fromResolver, out var name))
            return new ResolvedLocation(code, name, fromResolver, null);

        throw new ArgumentException(
            $"Location '{code}' is not in the embedded port database. Add its coordinate to MovementRequest.Coordinates or supply an ILocationResolver.");
    }
}
