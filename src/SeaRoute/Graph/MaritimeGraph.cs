using System.Runtime.InteropServices;
using SeaRoute.Common;
using SeaRoute.Spatial;

namespace SeaRoute.Graph;

/// <summary>
/// In-memory graph representing the global maritime shipping network (Marnet).
/// Backed by a compressed sparse row adjacency layout and a 2D KD-Tree for sub-millisecond route queries.
/// </summary>
public sealed class MaritimeGraph
{
    private readonly List<Coordinate> _nodeCoordinates = [];
    private readonly Dictionary<Coordinate, int> _coordToId = [];
    private readonly List<List<GraphEdge>> _adjacency = [];
    private readonly Dictionary<(int, int), string?> _edgePassages = [];

    // Flattened adjacency built by BuildIndex(): edges for node i live in _edges[_edgeOffsets[i].._edgeOffsets[i+1]).
    private int[]? _edgeOffsets;
    private GraphEdge[]? _edges;
    private KdTree<int>? _kdTree;

    /// <summary>Number of nodes in the graph.</summary>
    public int NodeCount => _nodeCoordinates.Count;

    /// <summary>Number of directed edges in the graph.</summary>
    public int EdgeCount => _edges?.Length ?? _adjacency.Sum(a => a.Count);

    /// <summary>
    /// Gets the coordinate of a node by its index.
    /// </summary>
    public Coordinate GetCoordinate(int nodeId) => _nodeCoordinates[nodeId];

    /// <summary>
    /// Gets the outgoing edges for a node as a span over the flattened adjacency array.
    /// </summary>
    public ReadOnlySpan<GraphEdge> GetEdges(int nodeId)
    {
        if (_edges != null && _edgeOffsets != null)
        {
            int start = _edgeOffsets[nodeId];
            return new ReadOnlySpan<GraphEdge>(_edges, start, _edgeOffsets[nodeId + 1] - start);
        }

        return CollectionsMarshal.AsSpan(_adjacency[nodeId]);
    }

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
    /// Invalidates any previously built index; call <see cref="BuildIndex"/> again before querying.
    /// </summary>
    public int AddNode(Coordinate coord)
    {
        InvalidateIndex();
        int id = _nodeCoordinates.Count;
        _nodeCoordinates.Add(coord);
        _coordToId[coord] = id;
        _adjacency.Add([]);
        return id;
    }

    /// <summary>
    /// Adds a directed edge from node uId to node vId with the specified weight and optional passage.
    /// Invalidates any previously built index; call <see cref="BuildIndex"/> again before querying.
    /// </summary>
    public void AddDirectedEdge(int uId, int vId, double weight, string? passage = null)
    {
        InvalidateIndex();
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

        return AddNode(coord);
    }

    /// <summary>
    /// Adds an undirected edge between coordinates u and v.
    /// Invalidates any previously built index; call <see cref="BuildIndex"/> again before querying.
    /// </summary>
    public void AddEdge(Coordinate u, Coordinate v, double? weight = null, string? passage = null)
    {
        int uId = GetOrAddNode(u);
        int vId = GetOrAddNode(v);

        double w = weight ?? Math.Round(Haversine.Distance(u, v, DistanceUnit.Km), 1);

        AddDirectedEdge(uId, vId, w, passage);
        AddDirectedEdge(vId, uId, w, passage);
    }

    /// <summary>
    /// Finalizes graph construction: flattens the adjacency lists into a compressed sparse row layout
    /// and builds the KD-Tree index. Must be called after all nodes and edges have been added.
    /// </summary>
    public void BuildIndex()
    {
        int nodeCount = _nodeCoordinates.Count;
        var offsets = new int[nodeCount + 1];
        for (int i = 0; i < nodeCount; i++)
        {
            offsets[i + 1] = offsets[i] + _adjacency[i].Count;
        }

        var edges = new GraphEdge[offsets[nodeCount]];
        for (int i = 0; i < nodeCount; i++)
        {
            _adjacency[i].CopyTo(edges, offsets[i]);
        }

        var items = new List<(Coordinate Point, int Value)>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            items.Add((_nodeCoordinates[i], i));
        }

        _edgeOffsets = offsets;
        _edges = edges;
        _kdTree = new KdTree<int>(items);
    }

    private void InvalidateIndex()
    {
        _edgeOffsets = null;
        _edges = null;
        _kdTree = null;
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
