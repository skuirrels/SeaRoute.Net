namespace SeaRoute.Common;

/// <summary>
/// Geodesic and Great-Circle distance calculations.
/// </summary>
public static class Haversine
{
    private const double AvgEarthRadiusMeters = 6371008.8;
    private const double EarthRadiusKm = 6371.0;

    /// <summary>
    /// Calculates the great circle distance between two coordinates in the specified distance unit.
    /// </summary>
    public static double Distance(Coordinate c1, Coordinate c2, DistanceUnit unit = DistanceUnit.Km)
    {
        double dLat = ToRadians(c2.Latitude - c1.Latitude);
        double dLon = ToRadians(c2.Longitude - c1.Longitude);

        double lat1 = ToRadians(c1.Latitude);
        double lat2 = ToRadians(c2.Latitude);

        double sinHalfDLat = Math.Sin(dLat / 2.0);
        double sinHalfDLon = Math.Sin(dLon / 2.0);

        double a = (sinHalfDLat * sinHalfDLat) + (sinHalfDLon * sinHalfDLon * Math.Cos(lat1) * Math.Cos(lat2));
        a = Math.Clamp(a, 0.0, 1.0);
        double b = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));

        return b * AvgEarthRadiusMeters * unit.GetConversionFactorFromMeters();
    }

    /// <summary>
    /// Calculates the Haversine distance in kilometers directly.
    /// Used as the A* heuristic.
    /// </summary>
    public static double DistanceKm(Coordinate c1, Coordinate c2)
    {
        double lat1 = ToRadians(c1.Latitude);
        double lat2 = ToRadians(c2.Latitude);
        double dLat = lat2 - lat1;
        double dLon = ToRadians(c2.Longitude - c1.Longitude);

        double sinHalfDLat = Math.Sin(dLat / 2.0);
        double sinHalfDLon = Math.Sin(dLon / 2.0);

        double a = (sinHalfDLat * sinHalfDLat) + (Math.Cos(lat1) * Math.Cos(lat2) * sinHalfDLon * sinHalfDLon);
        a = Math.Clamp(a, 0.0, 1.0);
        return EarthRadiusKm * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }

    /// <summary>
    /// Calculates the total path length of a sequence of coordinates in the specified unit.
    /// </summary>
    public static double CalculatePathLength(IReadOnlyList<Coordinate> coordinates, DistanceUnit unit = DistanceUnit.Km)
    {
        if (coordinates == null || coordinates.Count < 2)
            return 0.0;

        double total = 0.0;
        for (int i = 0; i < coordinates.Count - 1; i++)
        {
            total += Distance(coordinates[i], coordinates[i + 1], unit);
        }
        return total;
    }

    /// <summary>
    /// Calculates route duration in hours given vessel speed in knots and total path length in unit.
    /// </summary>
    public static double CalculateDurationHours(double speedKnots, double length, DistanceUnit unit)
    {
        if (speedKnots <= 0 || length <= 0)
            return 0.0;

        double speedInUnit = speedKnots * unit.GetSpeedCoefficient();
        if (speedInUnit <= 0)
            return 0.0;

        return length / speedInUnit;
    }

    /// <summary>
    /// Calculates 2D Euclidean distance in coordinate space (used by KD-Tree nearest neighbor).
    /// </summary>
    public static double EuclideanDistanceSquared(Coordinate c1, Coordinate c2)
    {
        double dx = c1.Longitude - c2.Longitude;
        double dy = c1.Latitude - c2.Latitude;
        return (dx * dx) + (dy * dy);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);
}
