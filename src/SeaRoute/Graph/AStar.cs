using SeaRoute.Common;

namespace SeaRoute.Graph;

/// <summary>
/// A* shortest path solver using Haversine great-circle distance as heuristic.
/// </summary>
public static class AStar
{
    /// <summary>
    /// Computes the shortest path using A* search.
    /// </summary>
    public static (double LengthKm, List<Coordinate> Path) FindPath(
        MaritimeGraph graph,
        int source,
        int target,
        IReadOnlySet<string>? restrictions)
    {
        if (source == target)
        {
            return (0.0, [graph.GetCoordinate(source)]);
        }

        Coordinate targetCoord = graph.GetCoordinate(target);

        var gScore = new Dictionary<int, double>();
        var parent = new Dictionary<int, int>();
        var openSet = new PriorityQueue<int, double>();
        var closedSet = new HashSet<int>();

        gScore[source] = 0.0;
        double h0 = Haversine.DistanceKm(graph.GetCoordinate(source), targetCoord);
        openSet.Enqueue(source, h0);

        while (openSet.Count > 0)
        {
            int current = openSet.Dequeue();

            if (current == target)
            {
                // Reconstruct path
                var path = new List<Coordinate>();
                int curr = target;
                while (curr != source)
                {
                    path.Add(graph.GetCoordinate(curr));
                    curr = parent[curr];
                }
                path.Add(graph.GetCoordinate(source));
                path.Reverse();

                return (gScore[target], path);
            }

            if (!closedSet.Add(current))
                continue;

            double currentG = gScore[current];

            foreach (var edge in graph.GetEdges(current))
            {
                if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    continue;

                int neighbor = edge.TargetNodeId;
                if (closedSet.Contains(neighbor))
                    continue;

                double tentativeG = currentG + edge.Weight;

                if (!gScore.TryGetValue(neighbor, out double existingG) || tentativeG < existingG)
                {
                    gScore[neighbor] = tentativeG;
                    parent[neighbor] = current;
                    double h = Haversine.DistanceKm(graph.GetCoordinate(neighbor), targetCoord);
                    openSet.Enqueue(neighbor, tentativeG + h);
                }
            }
        }

        return (double.PositiveInfinity, []);
    }
}
