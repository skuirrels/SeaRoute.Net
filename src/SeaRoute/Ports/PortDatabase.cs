using SeaRoute.Common;
using SeaRoute.Spatial;

namespace SeaRoute.Ports;

/// <summary>
/// Database of world maritime ports, indexed spatially via KD-Tree.
/// </summary>
public sealed class PortDatabase
{
    private readonly List<Port> _ports;
    private readonly Dictionary<string, Port> _portsByCode;
    private readonly KdTree<Port> _kdTree;

    /// <summary>Total number of ports.</summary>
    public int Count => _ports.Count;

    /// <summary>
    /// Initializes a new instance of <see cref="PortDatabase"/>.
    /// </summary>
    public PortDatabase(IEnumerable<Port> ports)
    {
        _ports = ports.ToList();
        _portsByCode = new Dictionary<string, Port>(StringComparer.OrdinalIgnoreCase);

        var treeItems = new List<(Coordinate Point, Port Value)>(_ports.Count);
        foreach (var port in _ports)
        {
            treeItems.Add((port.Coordinate, port));
            if (!string.IsNullOrEmpty(port.PortCode))
            {
                _portsByCode.TryAdd(port.PortCode, port);
            }
        }

        _kdTree = new KdTree<Port>(treeItems);
    }

    /// <summary>
    /// Gets a port by its UN/LOCODE or port code.
    /// </summary>
    public Port? GetByCode(string portCode)
    {
        return _portsByCode.TryGetValue(portCode, out var port) ? port : null;
    }

    /// <summary>
    /// Finds the nearest port to a given geographic coordinate.
    /// </summary>
    public Port? FindNearestPort(Coordinate coordinate)
    {
        var result = _kdTree.Query(coordinate);
        return result?.Value;
    }

    /// <summary>
    /// Queries the port database applying terminal and country filters.
    /// </summary>
    public Port? QueryClosestPort(
        Coordinate point,
        bool onlyTerminals = false,
        string? country = null,
        string? toCountry = null,
        bool strict = false)
    {
        // Fast path: when no filters are requested, query the 2D KD-Tree directly in O(log N)
        if (!onlyTerminals && string.IsNullOrWhiteSpace(country) && string.IsNullOrWhiteSpace(toCountry))
        {
            return FindNearestPort(point);
        }

        IEnumerable<Port> candidates = _ports;

        if (onlyTerminals)
        {
            var terminalPorts = candidates.Where(p => p.IsTerminal).ToList();
            if (terminalPorts.Count > 0 || strict)
                candidates = terminalPorts;
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            string ctyUpper = country.Trim().ToUpperInvariant();
            var countryPorts = candidates.Where(p =>
                string.Equals(p.Country, ctyUpper, StringComparison.OrdinalIgnoreCase) ||
                p.PortCode.StartsWith(ctyUpper, StringComparison.OrdinalIgnoreCase)).ToList();

            if (countryPorts.Count > 0 || strict)
                candidates = countryPorts;
        }

        if (!string.IsNullOrWhiteSpace(toCountry))
        {
            string toCtyUpper = toCountry.Trim().ToUpperInvariant();
            var toCountryPorts = candidates.Where(p =>
                p.ToCountries.Any(tc => string.Equals(tc, toCtyUpper, StringComparison.OrdinalIgnoreCase))).ToList();

            if (toCountryPorts.Count > 0 || strict)
                candidates = toCountryPorts;
        }

        var candidateList = candidates.ToList();
        if (candidateList.Count == 0)
        {
            if (strict)
                return null;
            candidateList = _ports;
        }

        // Find closest among candidates using Euclidean distance
        Port? bestPort = null;
        double bestDistSq = double.PositiveInfinity;

        foreach (var port in candidateList)
        {
            double dSq = Haversine.EuclideanDistanceSquared(point, port.Coordinate);
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestPort = port;
            }
        }

        return bestPort;
    }

    /// <summary>
    /// Selects preferred ports matching area polygons.
    /// </summary>
    public List<Port> GetPreferredPorts(
        Coordinate point,
        IReadOnlyList<AreaFeature> areaFeatures,
        int? top = null,
        bool strictArea = true)
    {
        if (areaFeatures == null || areaFeatures.Count == 0)
            return [];

        // 1. Find smallest containing area feature
        AreaFeature? smallestArea = null;
        double minArea = double.PositiveInfinity;

        foreach (var area in areaFeatures)
        {
            if (area.Contains(point))
            {
                if (area.Area < minArea)
                {
                    minArea = area.Area;
                    smallestArea = area;
                }
            }
        }

        // 2. If not found and strictArea is false, find closest polygon within 2000 km
        if (smallestArea == null && !strictArea)
        {
            const double maxDistanceKm = 2000.0;
            double closestDistance = double.PositiveInfinity;

            foreach (var area in areaFeatures)
            {
                double dist = area.DistanceToPoint(point);
                if (dist <= maxDistanceKm && dist < closestDistance)
                {
                    closestDistance = dist;
                    smallestArea = area;
                }
            }
        }

        if (smallestArea == null || smallestArea.PreferredPorts.Count == 0)
            return [];

        double sumShares = smallestArea.PreferredPorts.Sum(p => p.Share);
        double divisor = Math.Max(sumShares, 1.0);

        var result = new List<Port>();
        foreach (var pref in smallestArea.PreferredPorts)
        {
            double normalizedShare = pref.Share / divisor;

            Port resolvedPort;
            if (_portsByCode.TryGetValue(pref.PortId, out var existing))
            {
                resolvedPort = existing.WithShare(normalizedShare);
            }
            else
            {
                var customCoord = pref.TryGetCoordinate() ?? point;
                resolvedPort = new Port
                {
                    PortCode = pref.PortId,
                    Name = pref.PortId,
                    Coordinate = customCoord,
                    Share = normalizedShare
                };
            }

            result.Add(resolvedPort);
        }

        result.Sort((a, b) => (b.Share ?? 0.0).CompareTo(a.Share ?? 0.0));

        return top.HasValue && top.Value > 0 ? result.Take(top.Value).ToList() : result;
    }

    /// <summary>
    /// Generates the port matrix (combinations of origin port and destination port).
    /// </summary>
    public List<(Port OriginPort, Port DestPort)> GetSelectedPortMatrix(
        Coordinate origin,
        Coordinate destination,
        PortParameters? parameters)
    {
        parameters ??= new PortParameters();

        var areasFrom = parameters.PortsInAreasFrom ?? parameters.PortsInAreas;
        var areasTo = parameters.PortsInAreasTo ?? parameters.PortsInAreas;

        var originPorts = new List<Port>();
        if (areasFrom != null && areasFrom.Count > 0)
        {
            originPorts = GetPreferredPorts(origin, areasFrom, strictArea: parameters.StrictArea);
        }

        if (originPorts.Count == 0)
        {
            string? toCty = parameters.CountryRestricted ? parameters.CountryPod : null;
            var closest = QueryClosestPort(
                origin,
                parameters.OnlyTerminals,
                parameters.CountryPol,
                toCty,
                parameters.Strict);

            if (closest != null)
            {
                originPorts.Add(closest.WithShare(1.0));
            }
        }

        var destPorts = new List<Port>();
        if (areasTo != null && areasTo.Count > 0)
        {
            destPorts = GetPreferredPorts(destination, areasTo, strictArea: parameters.StrictArea);
        }

        if (destPorts.Count == 0)
        {
            var closest = QueryClosestPort(
                destination,
                parameters.OnlyTerminals,
                parameters.CountryPod,
                null,
                parameters.Strict);

            if (closest != null)
            {
                destPorts.Add(closest.WithShare(1.0));
            }
        }

        var matrix = new List<(Port, Port)>();
        foreach (var fromPort in originPorts)
        {
            foreach (var toPort in destPorts)
            {
                matrix.Add((fromPort, toPort));
            }
        }

        return matrix;
    }
}
