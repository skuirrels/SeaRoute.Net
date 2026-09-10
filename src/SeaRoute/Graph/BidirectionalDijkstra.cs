using SeaRoute.Common;

namespace SeaRoute.Graph;

/// <summary>
/// High-performance Bidirectional Dijkstra shortest path solver.
/// </summary>
public static class BidirectionalDijkstra
{
    /// <summary>
    /// Computes the shortest path and distance in kilometers between source and target nodes,
    /// avoiding any passages present in the restrictions set.
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

        int nodeCount = graph.NodeCount;
        var distF = new Dictionary<int, double>();
        var distB = new Dictionary<int, double>();
        var parentF = new Dictionary<int, int>();
        var parentB = new Dictionary<int, int>();

        var queueF = new PriorityQueue<int, double>();
        var queueB = new PriorityQueue<int, double>();

        distF[source] = 0.0;
        queueF.Enqueue(source, 0.0);

        distB[target] = 0.0;
        queueB.Enqueue(target, 0.0);

        double bestDistance = double.PositiveInfinity;
        int meetNode = -1;

        while (queueF.Count > 0 && queueB.Count > 0)
        {
            // Alternate or take the side with smaller minimum key
            if (queueF.TryPeek(out _, out double minF) && queueB.TryPeek(out _, out double minB))
            {
                if (minF + minB >= bestDistance)
                {
                    break;
                }
            }

            // Expand forward side
            if (queueF.Count > 0 && (queueB.Count == 0 || queueF.PeekPriority() <= queueB.PeekPriority()))
            {
                if (!queueF.TryDequeue(out int u, out double prioF) || prioF > distF[u])
                    continue;

                double dU = distF[u];

                foreach (var edge in graph.GetEdges(u))
                {
                    if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    {
                        continue; // Restricted passage skipped
                    }

                    int v = edge.TargetNodeId;
                    double cost = dU + edge.Weight;

                    if (!distF.TryGetValue(v, out double currentDist) || cost < currentDist)
                    {
                        distF[v] = cost;
                        parentF[v] = u;
                        queueF.Enqueue(v, cost);

                        if (distB.TryGetValue(v, out double bDist))
                        {
                            double total = cost + bDist;
                            if (total < bestDistance)
                            {
                                bestDistance = total;
                                meetNode = v;
                            }
                        }
                    }
                }
            }
            else if (queueB.Count > 0)
            {
                // Expand backward side
                if (!queueB.TryDequeue(out int u, out double prioB) || prioB > distB[u])
                    continue;

                double dU = distB[u];

                foreach (var edge in graph.GetEdges(u))
                {
                    if (edge.Passage != null && restrictions != null && restrictions.Contains(edge.Passage))
                    {
                        continue; // Restricted passage skipped
                    }

                    int v = edge.TargetNodeId;
                    double cost = dU + edge.Weight;

                    if (!distB.TryGetValue(v, out double currentDist) || cost < currentDist)
                    {
                        distB[v] = cost;
                        parentB[v] = u;
                        queueB.Enqueue(v, cost);

                        if (distF.TryGetValue(v, out double fDist))
                        {
                            double total = fDist + cost;
                            if (total < bestDistance)
                            {
                                bestDistance = total;
                                meetNode = v;
                            }
                        }
                    }
                }
            }
        }

        if (meetNode == -1 || double.IsPositiveInfinity(bestDistance))
        {
            return (double.PositiveInfinity, []);
        }

        // Reconstruct path: source -> meetNode -> target
        var forwardPath = new List<int>();
        int curr = meetNode;
        while (curr != source)
        {
            forwardPath.Add(curr);
            curr = parentF[curr];
        }
        forwardPath.Add(source);
        forwardPath.Reverse();

        curr = meetNode;
        while (curr != target)
        {
            curr = parentB[curr];
            forwardPath.Add(curr);
        }

        var coordinates = new List<Coordinate>(forwardPath.Count);
        for (int i = 0; i < forwardPath.Count; i++)
        {
            coordinates.Add(graph.GetCoordinate(forwardPath[i]));
        }

        return (bestDistance, coordinates);
    }

    private static double PeekPriority(this PriorityQueue<int, double> queue)
    {
        queue.TryPeek(out _, out double priority);
        return priority;
    }
}
