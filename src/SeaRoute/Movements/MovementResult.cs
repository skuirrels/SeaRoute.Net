using SeaRoute.GeoJson;

namespace SeaRoute.Movements;

/// <summary>
/// Routed result for one leg of a movement.
/// </summary>
public sealed class LegResult
{
    /// <summary>1-based position of the leg in the movement.</summary>
    public int Sequence { get; }

    /// <summary>The leg as requested.</summary>
    public MovementLeg Leg { get; }

    /// <summary>Resolved start location.</summary>
    public ResolvedLocation From { get; }

    /// <summary>Resolved end location.</summary>
    public ResolvedLocation To { get; }

    /// <summary>Route geometry and properties for the leg.</summary>
    public GeoJsonFeature Feature { get; }

    /// <summary>Leg length in the movement's units.</summary>
    public double Length => Feature.Properties.Length;

    /// <summary>Estimated leg duration in hours.</summary>
    public double DurationHours => Feature.Properties.DurationHours;

    /// <summary>Well-to-wheel CO2e intensity applied, in grams per tonne-kilometre.</summary>
    public double Co2eGramsPerTonneKm => Feature.Properties.Co2eGramsPerTonneKm ?? 0.0;

    /// <summary>CO2e per tonne of cargo for this leg, in kilograms.</summary>
    public double Co2eKgPerTonne => Feature.Properties.Co2eKgPerTonne ?? 0.0;

    /// <summary>CO2e for this leg in kilograms, when the movement states a cargo weight.</summary>
    public double? Co2eKg => Feature.Properties.Co2eKg;

    internal LegResult(int sequence, MovementLeg leg, ResolvedLocation from, ResolvedLocation to, GeoJsonFeature feature)
    {
        Sequence = sequence;
        Leg = leg;
        From = from;
        To = to;
        Feature = feature;
    }
}

/// <summary>
/// Routed result for a whole movement.
/// </summary>
public sealed class MovementResult
{
    /// <summary>Per-leg results in order.</summary>
    public IReadOnlyList<LegResult> Legs { get; }

    /// <summary>Unit identifier shared by every length in the result.</summary>
    public string Units { get; }

    /// <summary>Sum of leg lengths.</summary>
    public double TotalLength { get; }

    /// <summary>Sum of leg durations in hours.</summary>
    public double TotalDurationHours { get; }

    /// <summary>Length per transport mode.</summary>
    public IReadOnlyDictionary<TransportMode, double> LengthByMode { get; }

    /// <summary>Sum of leg CO2e per tonne of cargo, in kilograms.</summary>
    public double TotalCo2eKgPerTonne { get; }

    /// <summary>Cargo weight in tonnes, when stated on the request.</summary>
    public double? CargoTonnes { get; }

    /// <summary>Sum of leg CO2e in kilograms, when a cargo weight is stated.</summary>
    public double? TotalCo2eKg { get; }

    internal MovementResult(IReadOnlyList<LegResult> legs, string units, double? cargoTonnes)
    {
        Legs = legs;
        Units = units;
        CargoTonnes = cargoTonnes;

        double totalLength = 0.0;
        double totalHours = 0.0;
        double totalCo2ePerTonne = 0.0;
        var byMode = new Dictionary<TransportMode, double>(4);
        foreach (var leg in legs)
        {
            totalLength += leg.Length;
            totalHours += leg.DurationHours;
            totalCo2ePerTonne += leg.Co2eKgPerTonne;
            byMode.TryGetValue(leg.Leg.Mode, out double soFar);
            byMode[leg.Leg.Mode] = soFar + leg.Length;
        }

        TotalLength = totalLength;
        TotalDurationHours = totalHours;
        LengthByMode = byMode;
        TotalCo2eKgPerTonne = totalCo2ePerTonne;
        TotalCo2eKg = cargoTonnes.HasValue ? totalCo2ePerTonne * cargoTonnes.Value : null;
    }

    /// <summary>
    /// Builds a GeoJSON FeatureCollection with one feature per leg and the totals as foreign members.
    /// </summary>
    public GeoJsonFeatureCollection ToFeatureCollection()
    {
        var features = new List<GeoJsonFeature>(Legs.Count);
        foreach (var leg in Legs)
            features.Add(leg.Feature);

        return new GeoJsonFeatureCollection
        {
            Features = features,
            Properties = new MovementProperties
            {
                TotalLength = TotalLength,
                Units = Units,
                TotalDurationHours = TotalDurationHours,
                LegCount = Legs.Count,
                TotalCo2eKgPerTonne = TotalCo2eKgPerTonne,
                CargoTonnes = CargoTonnes,
                TotalCo2eKg = TotalCo2eKg
            }
        };
    }

    /// <summary>
    /// Serialises the movement as a GeoJSON FeatureCollection.
    /// </summary>
    public string ToJson(bool writeIndented = false) => ToFeatureCollection().ToJson(writeIndented);
}
