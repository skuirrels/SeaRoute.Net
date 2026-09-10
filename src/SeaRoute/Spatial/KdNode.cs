using SeaRoute.Common;

namespace SeaRoute.Spatial;

/// <summary>
/// A node in a 2D KD-Tree.
/// </summary>
/// <typeparam name="T">Payload type associated with the node.</typeparam>
public sealed class KdNode<T>
{
    /// <summary>Geographical coordinate position of the node.</summary>
    public Coordinate Point { get; }

    /// <summary>Payload value.</summary>
    public T Value { get; }

    /// <summary>Left child subtree.</summary>
    public KdNode<T>? Left { get; internal set; }

    /// <summary>Right child subtree.</summary>
    public KdNode<T>? Right { get; internal set; }

    /// <summary>
    /// Initializes a new instance of <see cref="KdNode{T}"/>.
    /// </summary>
    public KdNode(Coordinate point, T value)
    {
        Point = point;
        Value = value;
    }
}
