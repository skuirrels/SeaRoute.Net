using System.Text.Json;
using FluentAssertions;
using SeaRoute.Common;
using SeaRoute.Movements;
using Xunit;

namespace SeaRoute.Tests;

public class MovementTests
{
    private const string ShanghaiMovement = """
        Pickup GBLGW to Port GBFXT Road
        Port GBFXT to Port CNSHG Sea
        Delivery from port CNSHG to place CNSHZ Sea
        """;

    private const string MelbourneMovement = """
        Pickup GBLGW to Port GBFXT Road
        Port GBFXT to Port SGSIN Sea
        Port SGSIN to Port AUMEL Sea
        Delivery from port AUMEL to place AUMRS Sea
        """;

    // Codes the caller supplies: places absent from the embedded port list, plus CNSHG, which carriers use
    // for the Port of Shanghai but which the embedded list holds as Sanshan, an inland Yangtze port.
    private static readonly Dictionary<string, Coordinate> ExtraPlaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GBLGW"] = new Coordinate(-0.190278, 51.148056),   // London Gatwick
        ["CNSHG"] = new Coordinate(121.497113, 31.400091),  // Port of Shanghai, Wusongkou
        ["CNSHZ"] = new Coordinate(114.057868, 22.543099),  // Shenzhen
        ["AUMRS"] = new Coordinate(145.13, -37.92)          // placeholder near Melbourne
    };

    [Fact]
    public void Parser_ParsesPickupMainAndDeliveryLines()
    {
        var legs = MovementParser.Parse(ShanghaiMovement);

        legs.Should().HaveCount(3);

        legs[0].Kind.Should().Be(LegKind.Pickup);
        legs[0].Mode.Should().Be(TransportMode.Road);
        legs[0].From.Code.Should().Be("GBLGW");
        legs[0].To.Code.Should().Be("GBFXT");

        legs[1].Kind.Should().Be(LegKind.Main);
        legs[1].Mode.Should().Be(TransportMode.Sea);
        legs[1].From.Code.Should().Be("GBFXT");
        legs[1].To.Code.Should().Be("CNSHG");

        legs[2].Kind.Should().Be(LegKind.Delivery);
        legs[2].Mode.Should().Be(TransportMode.Sea);
        legs[2].From.Code.Should().Be("CNSHG");
        legs[2].To.Code.Should().Be("CNSHZ");
    }

    [Fact]
    public void Parser_RejectsMalformedLineWithLineNumber()
    {
        var text = "Port GBFXT to Port SGSIN Sea\nthis is not a leg";

        var act = () => MovementParser.Parse(text);

        act.Should().Throw<FormatException>().WithMessage("Line 2*");
    }

    [Fact]
    public void Movement_ThreeLegs_RoadIsStraightAndSeaIsRouted()
    {
        var result = SeaRouter.CalculateMovement(ShanghaiMovement, ExtraPlaces);

        result.Legs.Should().HaveCount(3);
        result.Units.Should().Be("km");

        var pickup = result.Legs[0];
        pickup.Leg.Mode.Should().Be(TransportMode.Road);
        pickup.Feature.Geometry.Coordinates.Should().HaveCount(2);
        pickup.Length.Should().BeInRange(120.0, 160.0);
        pickup.DurationHours.Should().BeApproximately(pickup.Length / 60.0, 1e-6);
        pickup.To.Port.Should().NotBeNull();
        pickup.To.Port!.Name.Should().Be("Felixstowe");

        var main = result.Legs[1];
        main.Leg.Mode.Should().Be(TransportMode.Sea);
        main.Feature.Geometry.Coordinates.Count.Should().BeGreaterThan(20);
        main.Feature.Properties.PortOrigin!.PortCode.Should().Be("GBFXT");
        main.To.Label.Should().Be("CNSHG");
        main.To.Port.Should().BeNull("a caller-supplied coordinate replaces the embedded record, which is Sanshan");
        main.Feature.Properties.PortDest.Should().BeNull();
        main.Length.Should().BeInRange(19000.0, 20500.0, "Felixstowe to the Port of Shanghai via Suez");

        var delivery = result.Legs[2];
        delivery.Leg.Kind.Should().Be(LegKind.Delivery);
        delivery.Length.Should().BeGreaterThan(0.0);

        result.TotalLength.Should().BeApproximately(result.Legs.Sum(l => l.Length), 1e-6);
        result.TotalDurationHours.Should().BeApproximately(result.Legs.Sum(l => l.DurationHours), 1e-6);
        result.LengthByMode[TransportMode.Sea].Should().BeApproximately(main.Length + delivery.Length, 1e-6);
        result.LengthByMode[TransportMode.Road].Should().BeApproximately(pickup.Length, 1e-6);
    }

    [Fact]
    public void Movement_FourLegs_ProducesFeatureCollectionWithLegMetadata()
    {
        var result = SeaRouter.CalculateMovement(MelbourneMovement, ExtraPlaces);

        result.Legs.Should().HaveCount(4);
        result.Legs.Count(l => l.Leg.Mode == TransportMode.Sea).Should().Be(3);

        string json = result.ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("FeatureCollection");
        var features = root.GetProperty("features");
        features.GetArrayLength().Should().Be(4);

        var second = features[1].GetProperty("properties");
        second.GetProperty("leg").GetInt32().Should().Be(2);
        second.GetProperty("mode").GetString().Should().Be("sea");
        second.GetProperty("kind").GetString().Should().Be("main");
        second.GetProperty("from").GetString().Should().Be("GBFXT");
        second.GetProperty("to").GetString().Should().Be("SGSIN");

        root.GetProperty("properties").GetProperty("legs").GetInt32().Should().Be(4);
        root.GetProperty("properties").GetProperty("total_length").GetDouble().Should().BeApproximately(result.TotalLength, 1e-6);
    }

    [Fact]
    public void Movement_ConsecutiveLegsJoinEndToEnd()
    {
        var result = SeaRouter.CalculateMovement(MelbourneMovement, ExtraPlaces);

        for (int i = 0; i < result.Legs.Count - 1; i++)
        {
            var end = result.Legs[i].Feature.Geometry.Coordinates[^1];
            var start = result.Legs[i + 1].Feature.Geometry.Coordinates[0];
            start[0].Should().BeApproximately(end[0], 1e-9, $"leg {i + 2} must start where leg {i + 1} ends");
            start[1].Should().BeApproximately(end[1], 1e-9);
        }
    }

    [Fact]
    public void Movement_ShortSeaLegSnappingToOneNode_StillHasLength()
    {
        // Melbourne port and a placeholder a few kilometres away snap to the same Marnet node.
        var result = SeaRouter.CalculateMovement("Delivery from port AUMEL to place AUMRS Sea", ExtraPlaces);

        var leg = result.Legs[0];
        leg.Feature.Geometry.Coordinates.Count.Should().BeGreaterThanOrEqualTo(2);
        leg.Length.Should().BeInRange(5.0, 60.0);
        leg.DurationHours.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void Movement_UnknownCodeWithoutCoordinate_ThrowsNamingTheCode()
    {
        var act = () => SeaRouter.CalculateMovement("Pickup GBLGW to Port GBFXT Road");

        act.Should().Throw<ArgumentException>().WithMessage("*GBLGW*");
    }

    [Fact]
    public void Movement_UsesResolverForUnknownCodes()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road") };
        request.Resolver = new StubResolver(("GBLGW", new Coordinate(-0.190278, 51.148056), "Gatwick"));

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        result.Legs[0].From.Name.Should().Be("Gatwick");
        result.Legs[0].Length.Should().BeInRange(120.0, 160.0);
    }

    [Fact]
    public void Movement_HonoursUnitsAndModeSpeeds()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Rail") };
        request.Coordinates["GBLGW"] = ExtraPlaces["GBLGW"];
        request.SeaOptions = new SeaRouteOptions { Units = DistanceUnit.NauticalMiles };
        request.SpeedsKmh[TransportMode.Rail] = 100.0;

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        result.Units.Should().Be("naut");
        double expectedHours = result.Legs[0].Length / (100.0 * 0.539956803);
        result.Legs[0].DurationHours.Should().BeApproximately(expectedHours, 1e-6);
    }

    [Fact]
    public void Parser_ReportsRealLineNumberWhenBlankLinesPrecedeTheBadLine()
    {
        var text = "Port GBFXT to Port SGSIN Sea\r\n\r\n\r\nthis is not a leg";

        var act = () => MovementParser.Parse(text);

        act.Should().Throw<FormatException>().WithMessage("Line 4*");
    }

    [Fact]
    public void Parser_RejectsUnknownModeInsteadOfGuessing()
    {
        var act = () => MovementParser.Parse("Port GBFXT to Port SGSIN Barge");

        act.Should().Throw<FormatException>().WithMessage("*Barge*");
    }

    [Fact]
    public void Parser_AcceptsModeSynonymsAndLowerCaseCodes()
    {
        var legs = MovementParser.Parse("pickup gblgw to port gbfxt truck\nport gbfxt to port sgsin vessel");

        legs[0].Mode.Should().Be(TransportMode.Road);
        legs[0].From.Code.Should().Be("GBLGW");
        legs[1].Mode.Should().Be(TransportMode.Sea);
    }

    [Fact]
    public void Movement_BlockedSeaLeg_ThrowsNamingTheLeg()
    {
        // Singapore to Piraeus with Suez and Gibraltar closed has no route; the coordinate-only destination
        // also exercises Location.FromCoordinate.
        var request = new MovementRequest
        {
            Legs =
            [
                new MovementLeg(
                    Location.FromCode("SGSIN"),
                    Location.FromCoordinate(new Coordinate(23.62904, 37.94056), "Piraeus"),
                    TransportMode.Sea)
            ],
            SeaOptions = new SeaRouteOptions { Restrictions = { Passages.Passage.Suez, Passages.Passage.Gibraltar } }
        };

        var act = () => SeaRouteEngine.Default.CalculateMovement(request);

        act.Should().Throw<InvalidOperationException>().WithMessage("Leg 1*Piraeus*");
    }

    [Fact]
    public void Movement_InvalidSuppliedCoordinate_ThrowsForStraightLegs()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road") };
        request.Coordinates["GBLGW"] = new Coordinate(-0.19, 951.1);

        var act = () => SeaRouteEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage("*Latitude*");
    }

    [Fact]
    public void Movement_LowerCaseCoordinateKeys_Resolve()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road") };
        request.Coordinates["gblgw"] = ExtraPlaces["GBLGW"];

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        result.Legs[0].Length.Should().BeInRange(120.0, 160.0);
    }

    [Fact]
    public void Movement_SeaSpeedInSpeedsKmh_IsRejected()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.SpeedsKmh[TransportMode.Sea] = 30.0;

        var act = () => SeaRouteEngine.Default.CalculateMovement(request);

        act.Should().Throw<ArgumentException>().WithMessage("*SpeedKnots*");
    }

    [Fact]
    public void Movement_ResolverDoesNotOverrideEmbeddedPortCoordinates()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.Resolver = new StubResolver(("GBFXT", new Coordinate(100.0, 0.0), "Wrong place"));

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        var felixstowe = SeaRouteEngine.Default.Ports.GetByCode("GBFXT")!;
        result.Legs[0].From.Coordinate.Should().Be(felixstowe.Coordinate);
        result.Legs[0].From.Name.Should().Be("Felixstowe");
    }

    [Fact]
    public void Movement_JunctionLocationIsResolvedOnce()
    {
        var resolver = new CountingResolver(("GBLGW", new Coordinate(-0.190278, 51.148056)), ("XXAAA", new Coordinate(1.0, 51.0)));
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road\nPort GBFXT to Port XXAAA Road\nPort XXAAA to Port GBFXT Road"),
            Resolver = resolver
        };

        SeaRouteEngine.Default.CalculateMovement(request);

        resolver.Calls.Should().Be(2, "GBLGW and XXAAA are each resolved once; GBFXT comes from the port database");
    }

    [Fact]
    public void SingleRoute_EndpointsSnappingToOneNode_ReturnsTwoPointLine()
    {
        var melbourne = SeaRouteEngine.Default.Ports.GetByCode("AUMEL")!.Coordinate;

        var route = SeaRouter.Calculate(melbourne, ExtraPlaces["AUMRS"]);

        route.Geometry.Coordinates.Should().HaveCount(2);
        route.Properties.Length.Should().BeInRange(5.0, 60.0);
    }

    [Fact]
    public void Coordinate_ToString_IsCultureInvariant()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            new Coordinate(-0.19, 51.1).ToString().Should().Be("[-0.190000, 51.100000]");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Movement_AirLegs_AreStraightLinesWithNoLanePointsOrChokePoints()
    {
        var request = new MovementRequest
        {
            Legs = MovementParser.Parse("""
                Pickup GBLGW to Airport GBLHR Road
                Airport GBLHR to Airport AUMEL Air
                Delivery from airport AUMEL to place AUMRS Road
                """),
            SeaOptions = new SeaRouteOptions { ReturnPassages = true }
        };
        request.Coordinates["GBLGW"] = ExtraPlaces["GBLGW"];
        request.Coordinates["GBLHR"] = new Coordinate(-0.4543, 51.4700);   // Heathrow
        request.Coordinates["AUMRS"] = ExtraPlaces["AUMRS"];

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        var flight = result.Legs[1];
        flight.Leg.Mode.Should().Be(TransportMode.Air);
        flight.Feature.Geometry.Coordinates.Should().HaveCount(2, "an air leg is one straight great-circle line");
        flight.Length.Should().BeInRange(16500.0, 17300.0, "Heathrow to Melbourne great-circle distance");
        flight.DurationHours.Should().BeApproximately(flight.Length / 800.0, 1e-6);
        flight.Feature.Properties.TraversedPassages.Should().BeNullOrEmpty();
        result.LengthByMode.Should().ContainKeys(TransportMode.Air, TransportMode.Road);
    }

    [Fact]
    public void Emissions_UseGlecDefaultsPerModeAndAirDistanceBand()
    {
        var f = EmissionFactors.GlecDefaults;

        f.GramsPerTonneKm(TransportMode.Sea, 15000).Should().Be(7.6);
        f.GramsPerTonneKm(TransportMode.Road, 100).Should().Be(92.0);
        f.GramsPerTonneKm(TransportMode.Rail, 500).Should().Be(28.0);
        f.GramsPerTonneKm(TransportMode.Air, 999).Should().Be(1130.0);
        f.GramsPerTonneKm(TransportMode.Air, 1000).Should().Be(700.0);
        f.GramsPerTonneKm(TransportMode.Air, 3700).Should().Be(700.0);
        f.GramsPerTonneKm(TransportMode.Air, 3701).Should().Be(630.0);
    }

    [Fact]
    public void Emissions_PerLegAndTotals_FollowIntensityTimesDistance()
    {
        var result = SeaRouter.CalculateMovement(MelbourneMovement, ExtraPlaces, cargoTonnes: 20.0);

        var road = result.Legs[0];
        road.Co2eGramsPerTonneKm.Should().Be(92.0);
        road.Co2eKgPerTonne.Should().BeApproximately(92.0 * road.Length / 1000.0, 1e-9);
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 20.0, 1e-9);

        var sea = result.Legs[1];
        sea.Co2eGramsPerTonneKm.Should().Be(7.6);
        sea.Co2eKgPerTonne.Should().BeApproximately(7.6 * sea.Length / 1000.0, 1e-9);

        result.CargoTonnes.Should().Be(20.0);
        result.TotalCo2eKgPerTonne.Should().BeApproximately(result.Legs.Sum(l => l.Co2eKgPerTonne), 1e-9);
        result.TotalCo2eKg.Should().BeApproximately(result.TotalCo2eKgPerTonne * 20.0, 1e-9);
    }

    [Fact]
    public void Emissions_WithoutCargoWeight_ReportOnlyPerTonneFigures()
    {
        var result = SeaRouter.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        result.CargoTonnes.Should().BeNull();
        result.TotalCo2eKg.Should().BeNull();
        result.Legs[0].Co2eKg.Should().BeNull();
        result.Legs[0].Co2eKgPerTonne.Should().BeGreaterThan(100.0, "15,400 km at 7.6 g per tonne-km");

        using var doc = JsonDocument.Parse(result.ToJson());
        var props = doc.RootElement.GetProperty("features")[0].GetProperty("properties");
        props.GetProperty("co2e_g_per_tonne_km").GetDouble().Should().Be(7.6);
        props.TryGetProperty("co2e_kg", out _).Should().BeFalse();
        doc.RootElement.GetProperty("properties").GetProperty("total_co2e_kg_per_tonne").GetDouble().Should().BeGreaterThan(100.0);
    }

    [Fact]
    public void Emissions_ConvertLengthToKilometresWhenUnitsDiffer()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Port GBFXT to Port SGSIN Sea") };
        request.SeaOptions = new SeaRouteOptions { Units = DistanceUnit.NauticalMiles };

        var inNaut = SeaRouteEngine.Default.CalculateMovement(request);
        var inKm = SeaRouter.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        inNaut.Legs[0].Co2eKgPerTonne.Should().BeApproximately(inKm.Legs[0].Co2eKgPerTonne, 1e-6);
    }

    [Fact]
    public void Emissions_CustomFactorsAreHonoured()
    {
        var request = new MovementRequest { Legs = MovementParser.Parse("Pickup GBLGW to Port GBFXT Road") };
        request.Coordinates["GBLGW"] = ExtraPlaces["GBLGW"];
        request.Emissions = new EmissionFactors { RoadGramsPerTonneKm = 50.0 };

        var result = SeaRouteEngine.Default.CalculateMovement(request);

        result.Legs[0].Co2eGramsPerTonneKm.Should().Be(50.0);
    }

    [Fact]
    public void Emissions_SeaLegsUsePerTeuRateWhenTeuIsGiven()
    {
        var result = SeaRouter.CalculateMovement(MelbourneMovement, ExtraPlaces, cargoTonnes: 12.0, cargoTeu: 2.0);

        var sea = result.Legs[1];
        sea.Co2eBasis.Should().Be("teu");
        sea.Feature.Properties.Co2eGramsPerTeuKm.Should().Be(76.0);
        sea.Co2eKg.Should().BeApproximately(76.0 * 2.0 * sea.Length / 1000.0, 1e-9);

        var road = result.Legs[0];
        road.Co2eBasis.Should().Be("tonnes", "non-sea legs use the stated weight");
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 12.0, 1e-9);

        result.CargoTeu.Should().Be(2.0);
        result.TotalCo2eKg.Should().BeApproximately(result.Legs.Sum(l => l.Co2eKg!.Value), 1e-9);

        using var doc = JsonDocument.Parse(result.ToJson());
        var seaProps = doc.RootElement.GetProperty("features")[1].GetProperty("properties");
        seaProps.GetProperty("co2e_basis").GetString().Should().Be("teu");
        seaProps.GetProperty("co2e_g_per_teu_km").GetDouble().Should().Be(76.0);
        doc.RootElement.GetProperty("properties").GetProperty("cargo_teu").GetDouble().Should().Be(2.0);
    }

    [Fact]
    public void Emissions_TeuOnly_InfersAverageWeightForNonSeaLegs()
    {
        var result = SeaRouter.CalculateMovement(MelbourneMovement, ExtraPlaces, cargoTeu: 1.0);

        var road = result.Legs[0];
        road.Co2eBasis.Should().Be("teu_average_weight");
        road.Co2eKg.Should().BeApproximately(road.Co2eKgPerTonne * 10.0, 1e-9, "GLEC average of 10 t per TEU");
        result.Legs[1].Co2eBasis.Should().Be("teu");
        result.CargoTonnes.Should().BeNull();
        result.TotalCo2eKg.Should().NotBeNull();
    }

    private sealed class CountingResolver(params (string Code, Coordinate Coordinate)[] entries) : ILocationResolver
    {
        public int Calls { get; private set; }

        public bool TryResolve(string code, out Coordinate coordinate, out string? name)
        {
            Calls++;
            foreach (var (c, coord) in entries)
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                {
                    coordinate = coord;
                    name = null;
                    return true;
                }
            }
            coordinate = default;
            name = null;
            return false;
        }
    }

    private sealed class StubResolver(params (string Code, Coordinate Coordinate, string Name)[] entries) : ILocationResolver
    {
        public bool TryResolve(string code, out Coordinate coordinate, out string? name)
        {
            foreach (var (c, coord, n) in entries)
            {
                if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                {
                    coordinate = coord;
                    name = n;
                    return true;
                }
            }
            coordinate = default;
            name = null;
            return false;
        }
    }
}
