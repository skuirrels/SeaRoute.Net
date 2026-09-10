namespace SeaRoute.Movements;

/// <summary>
/// One leg of a movement: from one location to another by one transport mode.
/// </summary>
public sealed class MovementLeg
{
    /// <summary>Start of the leg.</summary>
    public Location From { get; }

    /// <summary>End of the leg.</summary>
    public Location To { get; }

    /// <summary>Transport mode used for the leg.</summary>
    public TransportMode Mode { get; }

    /// <summary>Role of the leg within the movement.</summary>
    public LegKind Kind { get; }

    /// <summary>
    /// Initializes a new leg.
    /// </summary>
    public MovementLeg(Location from, Location to, TransportMode mode, LegKind kind = LegKind.Main)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        To = to ?? throw new ArgumentNullException(nameof(to));
        Mode = mode;
        Kind = kind;
    }

    /// <summary>Convenience constructor from two location codes.</summary>
    public MovementLeg(string fromCode, string toCode, TransportMode mode, LegKind kind = LegKind.Main)
        : this(Location.FromCode(fromCode), Location.FromCode(toCode), mode, kind)
    {
    }

    /// <inheritdoc />
    public override string ToString() => $"{Kind} {From} to {To} {Mode}";
}
