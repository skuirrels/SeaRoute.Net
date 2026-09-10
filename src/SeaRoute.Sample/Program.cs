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
var route = SeaRoute.SeaRoute.Calculate(marseille, capeTown, appendOrigDest: true);
stopwatch.Stop();

Print("1. Marseille to Cape Town",
    $"{route.Properties.Length:N1} {route.Properties.Units}, " +
    $"{route.Properties.DurationHours:N1} h at 24 kn, " +
    $"{route.Geometry.Coordinates.Count} points, cold start {stopwatch.ElapsedMilliseconds} ms");

// 2. Passage restrictions: avoid Suez, so the route goes round the Cape of Good Hope.
var gulf = new Coordinate(52.99, 25.01);
var caribbean = new Coordinate(-61.87, 17.15);
var viaCape = SeaRoute.SeaRoute.Calculate(gulf, caribbean, restrictions: [Passage.Suez], returnPassages: true);
Print("2. Persian Gulf to Caribbean avoiding Suez",
    $"{viaCape.Properties.Length:N0} km via {string.Join(", ", viaCape.Properties.TraversedPassages!)}");

// 3. Port codes (UN/LOCODE) in.
var portToPort = SeaRoute.SeaRoute.Calculate("FRLEH", "CNTSN");
Print("3. FRLEH to CNTSN by port code",
    $"{portToPort.Properties.PortOrigin!.Name} to {portToPort.Properties.PortDest!.Name}, {portToPort.Properties.Length:N0} km");

// 4. Inland points resolved to the nearest container terminals.
var paris = new Coordinate(2.333333, 48.866667);
var tokyo = new Coordinate(139.679174, 35.778467);
var viaPorts = SeaRoute.SeaRoute.Calculate(
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
var transPacific = SeaRoute.SeaRoute.Calculate(yokohama, losAngeles, units: DistanceUnit.NauticalMiles, algorithm: "astar");
Print("5. Yokohama to Los Angeles, A*, nautical miles",
    $"{transPacific.Properties.Length:N0} {transPacific.Properties.Units}, crosses the antimeridian without a longitude jump");

// 6. Unreachable when every passage is closed: empty geometry, zero length.
var singapore = new Coordinate(103.85457, 1.25760);
var piraeus = new Coordinate(23.62904, 37.94056);
var blocked = SeaRoute.SeaRoute.Calculate(singapore, piraeus, restrictions: [Passage.Suez, Passage.Gibraltar]);
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
var routes = SeaRoute.SeaRoute.CalculateRoutes(brussels, tokyo, new SeaRouteOptions
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

static void Print(string title, string detail)
{
    Console.WriteLine();
    Console.WriteLine(title);
    Console.WriteLine("   " + detail);
}
