using System.Diagnostics;
using SeaRoute;
using SeaRoute.Common;
using SeaRoute.Passages;
using SeaRoute.Ports;

// SeaRoute.Net sample: exercises each public entry point and prints the results.
// Run with:  dotnet run --project src/SeaRoute.Sample
// Pass --geojson to print the full GeoJSON feature for the first route.

bool printGeoJson = args.Contains("--geojson", StringComparer.OrdinalIgnoreCase);

Console.WriteLine("SeaRoute.Net sample");
Console.WriteLine(new string('=', 60));

// 1. Coordinates in, GeoJSON feature out. First call also decompresses the embedded datasets.
var stopwatch = Stopwatch.StartNew();
var marseille = new Coordinate(5.333333, 43.333333);
var capeTown = new Coordinate(18.366667, -33.916667);
var route = SeaRouter.Calculate(marseille, capeTown, appendOrigDest: true);
stopwatch.Stop();

Print("1. Marseille to Cape Town",
    $"{route.Properties.Length:N1} {route.Properties.Units}, " +
    $"{route.Properties.DurationHours:N1} h at 16 kn, " +
    $"{route.Geometry.Coordinates.Count} points, cold start {stopwatch.ElapsedMilliseconds} ms");

// 2. Passage restrictions: avoid Suez, so the route goes round the Cape of Good Hope.
var gulf = new Coordinate(52.99, 25.01);
var caribbean = new Coordinate(-61.87, 17.15);
var viaCape = SeaRouter.Calculate(gulf, caribbean, restrictions: [Passage.Suez], returnPassages: true);
Print("2. Persian Gulf to Caribbean avoiding Suez",
    $"{viaCape.Properties.Length:N0} km via {string.Join(", ", viaCape.Properties.TraversedPassages!)}");

// 3. Port codes (UN/LOCODE) in.
var portToPort = SeaRouter.Calculate("FRLEH", "CNTSN");
Print("3. FRLEH to CNTSN by port code",
    $"{portToPort.Properties.PortOrigin!.Name} to {portToPort.Properties.PortDest!.Name}, {portToPort.Properties.Length:N0} km");

// 4. Inland points resolved to the nearest container terminals.
var paris = new Coordinate(2.333333, 48.866667);
var tokyo = new Coordinate(139.679174, 35.778467);
var viaPorts = SeaRouter.Calculate(
    paris, tokyo,
    includePorts: true,
    appendOrigDest: true,
    portParams: new PortParameters { OnlyTerminals = true });
Print("4. Paris to Tokyo via nearest terminals",
    $"{viaPorts.Properties.PortOrigin?.PortCode} ({viaPorts.Properties.PortOrigin?.Name}) to " +
    $"{viaPorts.Properties.PortDest?.PortCode} ({viaPorts.Properties.PortDest?.Name}), {viaPorts.Properties.Length:N0} km");

// 5. Alternative algorithm and units.
var yokohama = new Coordinate(139.64, 35.44);
var losAngeles = new Coordinate(-118.24, 33.74);
var transPacific = SeaRouter.Calculate(yokohama, losAngeles, units: DistanceUnit.NauticalMiles, algorithm: "astar");
Print("5. Yokohama to Los Angeles, A*, nautical miles",
    $"{transPacific.Properties.Length:N0} {transPacific.Properties.Units}, crosses the antimeridian without a longitude jump");

// 6. Unreachable when every passage is closed: empty geometry, zero length.
var singapore = new Coordinate(103.85457, 1.25760);
var piraeus = new Coordinate(23.62904, 37.94056);
var blocked = SeaRouter.Calculate(singapore, piraeus, restrictions: [Passage.Suez, Passage.Gibraltar]);
Print("6. Singapore to Piraeus with Suez and Gibraltar closed",
    $"{blocked.Geometry.Coordinates.Count} points, {blocked.Properties.Length} km");

// 7. Preferred ports per area: one route per port share.
Coordinate[] belgium =
[
    new(2.539, 51.129), new(2.658, 50.797), new(3.123, 50.780), new(4.180, 50.029),
    new(4.885, 50.153), new(4.844, 49.817), new(5.368, 49.660), new(5.463, 49.502),
    new(5.839, 49.606), new(5.738, 49.963), new(6.413, 50.380), new(5.740, 50.813),
    new(5.823, 51.124), new(4.706, 51.474), new(3.830, 51.621), new(3.315, 51.346),
    new(2.539, 51.129)
];
var areaBelgium = new AreaFeature(belgium, "BE", [new PortProps("BEANR", 250), new PortProps("FRLEH", 200)]);
var brussels = new Coordinate(4.352, 50.851);
var routes = SeaRouter.CalculateRoutes(brussels, tokyo, new SeaRouteOptions
{
    IncludePorts = true,
    PortParameters = new PortParameters { PortsInAreasFrom = [areaBelgium] }
});
Print("7. Brussels to Tokyo with weighted preferred ports",
    string.Join("; ", routes.Select(r =>
        $"{r.Properties.PortOrigin?.PortCode} share {r.Properties.PortOrigin?.Share:P0}: {r.Properties.Length:N0} km")));

// 8. GeoJSON output, ready for Leaflet, Mapbox or any GIS tool.
string geoJson = route.ToJson(writeIndented: printGeoJson);
Print("8. GeoJSON", printGeoJson ? Environment.NewLine + geoJson : $"{geoJson.Length:N0} characters, first 100: {geoJson[..100]}...");

// 9. The README diagram's worked example: Shanghai to London, following each step.
var shanghai = new Coordinate(121.47, 31.23);
var london = new Coordinate(-0.12, 51.51);
var graph = SeaRouteEngine.Default.Graph;
var shanghaiLane = graph.GetCoordinate(graph.FindNearestNode(shanghai));
var londonLane = graph.GetCoordinate(graph.FindNearestNode(london));
var lanePath = SeaRouter.Calculate(shanghai, london, returnPassages: true);
var finished = SeaRouter.Calculate(shanghai, london, appendOrigDest: true);
Print("9. Shanghai to London, step by step",
    $"request        from 121.47°E 31.23°N to 0.12°W 51.51°N, km, 16 knots{Environment.NewLine}" +
    $"   snap           Shanghai lane point {Haversine.Distance(shanghai, shanghaiLane):N1} km away, London lane point {Haversine.Distance(london, londonLane):N1} km away{Environment.NewLine}" +
    $"   shortest path  {lanePath.Geometry.Coordinates.Count} lane points, {lanePath.Properties.Length:N0} km via {PassageNames(lanePath.Properties.TraversedPassages)}{Environment.NewLine}" +
    $"   finished route {finished.Geometry.Coordinates.Count} points, {finished.Properties.Length:N0} km, {finished.Properties.DurationHours:N1} h{Environment.NewLine}" +
    $"   result         GeoJSON Feature, {finished.ToJson().Length:N0} characters");

// 10 to 12. Multi-leg movements, one leg per line. Sea legs are routed; road and air legs are straight lines.
//    Codes resolve from the embedded port list and UN/LOCODE list. UN/LOCODE publishes no coordinates for
//    about a fifth of its entries, including these three, so the caller supplies them.
var places = new Dictionary<string, Coordinate>(StringComparer.OrdinalIgnoreCase)
{
    ["GBLGW"] = new(-0.190278, 51.148056),   // Gatwick Apt/London
    ["CNSHZ"] = new(121.4737, 31.2304),      // Shanghai Railway Station
    ["AUMRS"] = new(145.13, -37.92)          // Melrose, placeholder near Melbourne
};

PrintMovement("10. Movement with one sea leg", """
    Pickup GBLGW to Port GBFXT Road
    Port GBFXT to Port CNSHG Sea
    Delivery from port CNSHG to place CNSHZ Sea
    """, places);

PrintMovement("11. Movement with several sea legs, a light 12 t load in one 40-foot container", """
    Pickup GBLGW to Port GBFXT Road
    Port GBFXT to Port SGSIN Sea
    Port SGSIN to Port AUMEL Sea
    Delivery from port AUMEL to place AUMRS Sea
    """, places, tonnes: 12.0, teu: 2.0);

PrintMovement("12. Movement with an air leg", """
    Pickup GBLGW to Airport GBLHR Road
    Airport GBLHR to Airport AUMEL Air
    Delivery from airport AUMEL to place AUMRS Road
    """, places);   // GBLHR (Heathrow) resolves from the UN/LOCODE list

static void PrintMovement(string title, string legs, IReadOnlyDictionary<string, Coordinate> places, double tonnes = 20.0, double? teu = null)
{
    // CO2e per leg uses GLEC well-to-wheel defaults per mode. With a TEU count, sea legs are charged per
    // container (76 g per TEU-km) rather than per tonne, so a light box is not under-counted.
    var movement = SeaRouter.CalculateMovement(legs, places, new SeaRouteOptions { ReturnPassages = true }, cargoTonnes: tonnes, cargoTeu: teu);

    Console.WriteLine();
    Console.WriteLine(title);
    foreach (var line in legs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        Console.WriteLine("   " + line);
    Console.WriteLine();
    Console.WriteLine($"   {"Leg",-4}{"Kind",-10}{"Mode",-6}{"From",-7}{"To",-7}{"Distance",12}{"Transit",9}{"CO2e rate",16}{"CO2e per tonne",16}{"CO2e total",12}{"Basis",8}  Choke points");
    Console.WriteLine($"   {"",4}{"",10}{"",6}{"",7}{"",7}{"",12}{"hours",9}{"g per t-km",16}{"kg per t cargo",16}{"kg",12}{"",8}");
    foreach (var leg in movement.Legs)
    {
        Console.WriteLine(
            $"   {leg.Sequence,-4}{leg.Leg.Kind,-10}{leg.Leg.Mode,-6}{leg.From.Label,-7}{leg.To.Label,-7}" +
            $"{leg.Length,9:N0} km{leg.TransitHours,9:N1}{leg.Co2eGramsPerTonneKm,16:N1}{leg.Co2eKgPerTonne,16:N1}{leg.Co2eKg,12:N0}{leg.Co2eBasis,8}  {PassageNames(leg.Feature.Properties.TraversedPassages)}");
    }
    Console.WriteLine($"   {"Total",-34}{movement.TotalLength,9:N0} km{movement.TotalTransitHours,9:N1}{"",16}{movement.TotalCo2eKgPerTonne,16:N1}{movement.TotalCo2eKg,12:N0}{"",8}  for {movement.CargoTonnes:N0} t of cargo" + (movement.CargoTeu.HasValue ? $" in {movement.CargoTeu:N0} TEU" : ""));
    Console.WriteLine($"   Transit        = {movement.TotalDurationHours:N1} h travelling (sea at 16 knots, road 60, rail 80, air 800 km/h) + {movement.TotalPortHours:N0} h in port (24 h at each end of a sea leg) = {movement.TotalTransitHours / 24.0:N1} days");
    Console.WriteLine("   CO2e rate      = grams of CO2e emitted moving 1 tonne 1 km (GLEC well-to-wheel default for the mode)");
    Console.WriteLine("   CO2e per tonne = rate x leg distance: kg of CO2e for each tonne of cargo carried over the leg");
    Console.WriteLine("   CO2e total     = kg of CO2e for this shipment: per tonne x cargo weight, or per container (76 g per TEU-km) on sea legs when a TEU count is given");
}

static string PassageNames(IReadOnlyList<string>? tags)
{
    if (tags == null || tags.Count == 0)
        return "";
    return string.Join(", ", tags.Select(t => t switch
    {
        "babalmandab" => "Bab-el-Mandeb",
        "south_africa" => "Cape of Good Hope",
        "ormuz" => "Hormuz",
        "chili" => "Magellan Strait",
        "northwest" => "Northwest Passage",
        _ => char.ToUpperInvariant(t[0]) + t[1..]
    }));
}

static void Print(string title, string detail)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine("   " + detail);
}
