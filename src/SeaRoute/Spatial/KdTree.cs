using SeaRoute.Common;

namespace SeaRoute.Spatial;

/// <summary>
/// A 2D KD-Tree for fast nearest-neighbor spatial queries on geographical coordinates.
/// Thread-safe for read queries once constructed.
/// </summary>
/// <typeparam name="T">Payload type associated with coordinates.</typeparam>
public sealed class KdTree<T>
{
    private readonly KdNode<T>? _root;
    private readonly int _count;

    /// <summary>Number of nodes in the tree.</summary>
    public int Count => _count;

    /// <summary>
    /// Builds a balanced KD-Tree from the given sequence of point-value pairs.
    /// </summary>
    public KdTree(IEnumerable<(Coordinate Point, T Value)> items)
    {
        var list = items.ToList();
        _count = list.Count;
        _root = BuildTree(list, 0, list.Count, 0);
    }

    /// <summary>
    /// Builds a balanced KD-Tree from coordinates where the payload is the coordinate itself.
    /// </summary>
    public static KdTree<Coordinate> FromCoordinates(IEnumerable<Coordinate> coordinates)
    {
        return new KdTree<Coordinate>(coordinates.Select(c => (c, c)));
    }

    /// <summary>
    /// Finds the nearest neighbor in the KD-tree to the specified coordinate.
    /// Uses 2D Euclidean distance in coordinate space.
    /// </summary>
    public (Coordinate Point, T Value)? Query(Coordinate target)
    {
        if (_root == null)
            return null;

        KdNode<T>? bestNode = null;
        double bestDistSq = double.PositiveInfinity;

        QueryRecursive(_root, target, 0, ref bestNode, ref bestDistSq);

        return bestNode != null ? (bestNode.Point, bestNode.Value) : null;
    }

    private static readonly IComparer<(Coordinate Point, T Value)> LonComparer =
        Comparer<(Coordinate Point, T Value)>.Create((a, b) => a.Point.Longitude.CompareTo(b.Point.Longitude));

    private static readonly IComparer<(Coordinate Point, T Value)> LatComparer =
        Comparer<(Coordinate Point, T Value)>.Create((a, b) => a.Point.Latitude.CompareTo(b.Point.Latitude));

    private static KdNode<T>? BuildTree(List<(Coordinate Point, T Value)> points, int start, int length, int depth)
    {
        if (length <= 0)
            return null;

        int axis = depth % 2; // 0 = Longitude (X), 1 = Latitude (Y)
        points.Sort(start, length, axis == 0 ? LonComparer : LatComparer);

        int medianOffset = length / 2;
        int medianIndex = start + medianOffset;

        return new KdNode<T>(points[medianIndex].Point, points[medianIndex].Value)
        {
            Left = BuildTree(points, start, medianOffset, depth + 1),
            Right = BuildTree(points, medianIndex + 1, length - medianOffset - 1, depth + 1)
        };
    }

    private static void QueryRecursive(
        KdNode<T> current,
        Coordinate target,
        int depth,
        ref KdNode<T>? bestNode,
        ref double bestDistSq)
    {
        double currentDistSq = Haversine.EuclideanDistanceSquared(target, current.Point);
        if (currentDistSq < bestDistSq)
        {
            bestDistSq = currentDistSq;
            bestNode = current;
        }

        int axis = depth % 2;
        double currentVal = axis == 0 ? current.Point.Longitude : current.Point.Latitude;
        double targetVal = axis == 0 ? target.Longitude : target.Latitude;

        KdNode<T>? nextBranch = targetVal < currentVal ? current.Left : current.Right;
        KdNode<T>? oppositeBranch = targetVal < currentVal ? current.Right : current.Left;

        if (nextBranch != null)
        {
            QueryRecursive(nextBranch, target, depth + 1, ref bestNode, ref bestDistSq);
        }

        double axisDiff = targetVal - currentVal;
        if (oppositeBranch != null && (axisDiff * axisDiff) < bestDistSq)
        {
            QueryRecursive(oppositeBranch, target, depth + 1, ref bestNode, ref bestDistSq);
        }
    }
}
