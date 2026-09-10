using SeaRoute.Common;
using SeaRoute.Spatial;

namespace SeaRoute.Graph;

/// <summary>
/// In-memory graph representing the global maritime shipping network (Marnet).
/// Backed by adjacency lists and a 2D KD-Tree for sub-millisecond route queries.
/// </summary>
public sealed class MaritimeGraph
{
    private readonly List<Coordinate> _nodeCoordinates = [];
    private readonly Dictionary<Coordinate, int> _coordToId = [];
    private readonly List<List<GraphEdge>> _adjacency = [];
    private readonly Dictionary<(int, int), string?> _edgePassages = [];
    private KdTree<int>? _kdTree;

    /// <summary>Number of nodes in the graph.</summary>
    public int NodeCount => _nodeCoordinates.Count;

    /// <summary>
    /// Gets the coordinate of a node by its index.
    /// </summary>
    public Coordinate GetCoordinate(int nodeId) => _nodeCoordinates[nodeId];

    /// <summary>
    /// Gets the adjacent edges for a node.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetEdges(int nodeId) => _adjacency[nodeId];

    /// <summary>
    /// Gets the passage associated with an edge (if any).
    /// </summary>
    public string? GetPassage(int u, int v)
    {
        return _edgePassages.TryGetValue((u, v), out var passage) ? passage : null;
    }

    /// <summary>
    /// Gets the passage associated with an edge between two coordinates (if any).
    /// </summary>
    public string? GetPassage(Coordinate u, Coordinate v)
    {
        if (_coordToId.TryGetValue(u, out int uId) && _coordToId.TryGetValue(v, out int vId))
        {
            return GetPassage(uId, vId);
        }
        return null;
    }

    /// <summary>
    /// Adds a node with its coordinate, returning its assigned integer node ID.
    /// </summary>
    public int AddNode(Coordinate coord)
    {
        int id = _nodeCoordinates.Count;
        _nodeCoordinates.Add(coord);
        _coordToId[coord] = id;
        _adjacency.Add([]);
        return id;
    }

    /// <summary>
    /// Adds a directed edge from node uId to node vId with the specified weight and optional passage.
    /// </summary>
    public void AddDirectedEdge(int uId, int vId, double weight, string? passage = null)
    {
        _adjacency[uId].Add(new GraphEdge(vId, weight, passage));
        _edgePassages[(uId, vId)] = passage;
    }

    /// <summary>
    /// Adds a node if it does not already exist, returning its integer node ID.
    /// </summary>
    public int GetOrAddNode(Coordinate coord)
    {
        if (_coordToId.TryGetValue(coord, out int id))
            return id;

        id = _nodeCoordinates.Count;
        _nodeCoordinates.Add(coord);
        _coordToId[coord] = id;
        _adjacency.Add([]);
        return id;
    }

    /// <summary>
    /// Adds an undirected edge between coordinates u and v.
    /// </summary>
    public void AddEdge(Coordinate u, Coordinate v, double? weight = null, string? passage = null)
    {
        int uId = GetOrAddNode(u);
        int vId = GetOrAddNode(v);

        double w = weight ?? Math.Round(Haversine.Distance(u, v, DistanceUnit.Km), 1);

        _adjacency[uId].Add(new GraphEdge(vId, w, passage));
        _adjacency[vId].Add(new GraphEdge(uId, w, passage));

        _edgePassages[(uId, vId)] = passage;
        _edgePassages[(vId, uId)] = passage;
    }

    /// <summary>
    /// Finalizes graph construction and builds the KD-Tree index.
    /// Must be called after all nodes and edges have been added.
    /// </summary>
    public void BuildIndex()
    {
        var items = new List<(Coordinate Point, int Value)>(_nodeCoordinates.Count);
        for (int i = 0; i < _nodeCoordinates.Count; i++)
        {
            items.Add((_nodeCoordinates[i], i));
        }
        _kdTree = new KdTree<int>(items);
    }

    /// <summary>
    /// Finds the nearest graph node index to the given geographic coordinate.
    /// </summary>
    public int FindNearestNode(Coordinate coordinate)
    {
        if (_kdTree == null)
            throw new InvalidOperationException("Graph index has not been built. Call BuildIndex() first.");

        var nearest = _kdTree.Query(coordinate);
        if (nearest == null)
            throw new InvalidOperationException("No nodes in maritime graph.");

        return nearest.Value.Value;
    }

    /// <summary>
    /// Computes the shortest path on the maritime graph between two coordinates, applying passage restrictions.
    /// </summary>
    public (double LengthKm, List<Coordinate> Path) ShortestPath(
        Coordinate origin,
        Coordinate destination,
        IReadOnlySet<string>? restrictions = null,
        string? algorithm = "dijkstra")
    {
        int originNodeId = FindNearestNode(origin);
        int destNodeId = FindNearestNode(destination);

        if (originNodeId == destNodeId)
        {
            return (0.0, [_nodeCoordinates[originNodeId]]);
        }

        if (string.Equals(algorithm, "astar", StringComparison.OrdinalIgnoreCase))
        {
            return AStar.FindPath(this, originNodeId, destNodeId, restrictions);
        }

        return BidirectionalDijkstra.FindPath(this, originNodeId, destNodeId, restrictions);
    }
}
