using SeaRoute.Common;

namespace SeaRoute.Ports;

/// <summary>
/// Represents a geographic area (polygon) associated with preferred maritime ports and share weights.
/// </summary>
public sealed class AreaFeature
{
    /// <summary>Name or identifier of the area (e.g. "BE", "EUR").</summary>
    public string Name { get; }

    /// <summary>Polygon boundary vertices.</summary>
    public IReadOnlyList<Coordinate> Coordinates { get; }

    /// <summary>Preferred ports configured for this area.</summary>
    public IReadOnlyList<PortProps> PreferredPorts { get; }

    /// <summary>Planar area of the polygon.</summary>
    public double Area { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="AreaFeature"/>.
    /// </summary>
    public AreaFeature(IEnumerable<Coordinate> coordinates, string name, IEnumerable<PortProps>? preferredPorts = null)
    {
        Name = name;
        Coordinates = coordinates.ToList();
        PreferredPorts = (preferredPorts ?? []).ToList();
        Area = PnPoly.CalculatePlanarArea(Coordinates);
    }

    /// <summary>
    /// Checks if a geographic coordinate is inside this area polygon.
    /// </summary>
    public bool Contains(Coordinate point)
    {
        return PnPoly.ContainsPoint(Coordinates, point);
    }

    /// <summary>
    /// Calculates approximate distance in km from a point to the nearest polygon vertex.
    /// </summary>
    public double DistanceToPoint(Coordinate point)
    {
        if (Coordinates.Count == 0)
            return double.PositiveInfinity;

        double minDistance = double.PositiveInfinity;
        foreach (var vertex in Coordinates)
        {
            double d = Haversine.Distance(point, vertex, DistanceUnit.Km);
            if (d < minDistance)
            {
                minDistance = d;
            }
        }

        return minDistance;
    }
}
