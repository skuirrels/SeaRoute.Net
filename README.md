# SeaRoute.Net

[![.NET 8 | 10](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License: Apache-2.0](https://img.shields.io/badge/License-Apache--2.0-blue.svg)](LICENSE)
[![Dependencies: none](https://img.shields.io/badge/dependencies-none-brightgreen)](src/SeaRoute/SeaRoute.csproj)

Shortest sea route between ports identified by UN/LOCODE, as a single self-contained .NET library.

Give it two UN/LOCODE port codes and it returns RFC 7946 GeoJSON with the distance, voyage duration, ports used and canals and straits passed through. Port codes are the native input; raw coordinates remain available as a fallback for custom or unresolved locations. Ordinary routes are `LineString`; antimeridian crossings are split into `MultiLineString`. The maritime network and world ports database are compressed and embedded in the assembly, so there is nothing to download, configure or host.

```csharp
using SeaRoute;

var route = SeaRouter.Calculate(
    "FRMRS", // Marseille
    "ZACPT", // Cape Town
    new SeaRouteOptions { AppendOriginDestination = true });

Console.WriteLine($"{route.Properties.Length:N0} {route.Properties.Units}, {route.Properties.DurationHours:N0} h");
// 10,997 km, 371 h
```

### Complete multi-leg movement

This end-to-end example routes pickup, two sea legs and final delivery using UN/LOCODEs throughout. It calculates travelling and port time, applies the built-in well-to-wheel emission factors to a 12-tonne load in one 40-foot container, reports each leg and writes the complete GeoJSON `FeatureCollection`.

```csharp
using System;
using System.IO;
using SeaRoute;

const string legs = """
    Pickup GBLGW to Port GBFXT Road
    Port GBFXT to Port SGSIN Sea
    Port SGSIN to Port AUMEL Sea
    Delivery from port AUMEL to place AUMRS Road
    """;

var movement = SeaRouter.CalculateMovement(
    legs,
    seaOptions: new SeaRouteOptions { ReturnPassages = true },
    cargoTonnes: 12.0,
    cargoTeu: 2.0); // One 40-foot container

foreach (var leg in movement.Legs)
{
    var chokePoints = string.Join(
        ", ",
        leg.Feature.Properties.TraversedPassages ?? Array.Empty<string>());

    Console.WriteLine(
        $"{leg.Sequence}. {leg.Leg.Kind} {leg.Leg.Mode}: " +
        $"{leg.From.Label} to {leg.To.Label}, " +
        $"{leg.Length:N0} {movement.Units}, " +
        $"{leg.TransitHours:N1} h, " +
        $"{leg.Co2eKg:N0} kg CO2e" +
        (chokePoints.Length == 0 ? "" : $", via {chokePoints}"));
}

Console.WriteLine(
    $"Total: {movement.TotalLength:N0} {movement.Units}, " +
    $"{movement.TotalTransitHours:N1} h " +
    $"({movement.TotalTransitHours / 24.0:N1} days), " +
    $"{movement.TotalCo2eKg:N0} kg CO2e");

File.WriteAllText("movement.geojson", movement.ToJson(writeIndented: true));
```

This produces four joined leg features totalling about 23,670 km and 36.6 days with the default speeds and port dwell. The sea legs report their traversed choke points, and `movement.geojson` contains the full result for mapping or downstream processing.

---

## Contents

- [How it works](#how-it-works)
- [What you get back](#what-you-get-back)
- [Installation](#installation)
- [Usage](#usage)
- [Options reference](#options-reference)
- [Performance](#performance)
- [Repository layout](#repository-layout)
- [Building, testing and trying it out](#building-testing-and-trying-it-out)
- [Data](#data)
- [Licence](#licence)

---

## How it works

Every request goes through the same six steps, in order. The datasets are decompressed and indexed once on first use, then shared read-only by every thread.

<p align="center">
  <img src="docs/diagrams/routing-pipeline.svg" alt="SeaRoute.Net routing pipeline in six numbered steps: take the request, optionally resolve ports, snap each end to the nearest shipping-lane point, find the shortest path along the lanes avoiding closed passages, add the real endpoints and measure length and time, return a GeoJSON feature. A strip below follows Shanghai (CNSHG) to London (GBLON) through each step." width="100%">
</p>

Source: [docs/diagrams/routing-pipeline.svg](docs/diagrams/routing-pipeline.svg) (vector) and [routing-pipeline.html](docs/diagrams/routing-pipeline.html).

1. **Take the request.** Normally an origin and destination as UN/LOCODE port codes, plus options such as units, vessel speed and closed passages. Coordinates are accepted for custom or unresolved endpoints.
2. **Resolve ports.** The port-code overload resolves each code directly. Coordinate requests only resolve to nearby ports when `IncludePorts` is set; those ports can be limited by terminal or country.
3. **Snap to the lane network.** Each end is matched to the geographically nearest point on Marnet using a spherical KD-tree, including across the date line and near the poles. Shanghai's port position is 18 km from its lane point, London's 23 km.
4. **Find the shortest path** along the lanes with bidirectional Dijkstra, or A* on request. Lane links through a closed canal or strait are skipped; the Northwest Passage is closed by default. Shanghai to London gives 154 lane points over 19,397 km, through Malacca, Bab-el-Mandeb, Suez and Gibraltar.
5. **Finish the route.** With `AppendOriginDestination` the real endpoints are added, and length and duration are measured: 156 points, 19,438 km, 656 hours at 16 knots.
6. **Return a GeoJSON Feature**: a LineString, an antimeridian-split MultiLineString, or null geometry when no path exists, plus distance, units, duration, ports and passages.

### Terms

These apply to single routes and to multi-leg movements, which are described [below](#multi-leg-movements).

- **Waypoint**: a place you name in a leg by its UN/LOCODE. Pickup places, ports and delivery places are all waypoints. The first and last waypoints of a movement are the pickup and delivery places.
- **Leg**: the journey between two consecutive waypoints, by one transport mode.
- **Lane point**: a fixed dot on the sea map, one of 9,708, all on water. The router inserts them between the two waypoints of a sea leg; you never name one.
- **Lane**: a straight link between two neighbouring lane points. Sea legs travel only along lanes.
- **Snapping**: moving a waypoint's position to its nearest lane point so a sea leg can start or end on the map, then joining the two with a straight line so the leg still begins and ends at the waypoint.
- **Choke point**: a lane that runs through a canal or strait, tagged with its name. Thirteen exist. Closing one makes the router route round it.
- **Straight leg**: a road, rail or air leg. One straight line between its two waypoints, no lane points involved.

Implementation notes:

- **Graph storage.** Nodes and edges are held in a compressed sparse row layout; edge weights are great-circle kilometres and edges through canals and straits carry a passage tag.
- **Search.** Bidirectional Dijkstra by default, A* on request. Both use per-thread, node-indexed scratch arrays with generation stamps, so a query allocates only its result.
- **No route.** A single route with no surviving path returns RFC 7946 `null` geometry and zero length. A movement leg with no path throws, so totals are never silently short.
- **Antimeridian.** Trans-Pacific routes are split at ±180° into a `MultiLineString`, keeping every emitted longitude in the RFC 7946 range.
- **Areas.** Polygons can name several preferred ports with share weights, in which case one route per port is returned.

## What you get back

`CalculateRoute` returns a `GeoJsonFeature`. `ToJson()` serialises it to standard GeoJSON that Leaflet, Mapbox GL, OpenLayers, deck.gl, QGIS and PostGIS all consume directly.

<p align="center">
  <img src="docs/diagrams/output-model.svg" alt="SeaRoute.Net output model: a GeoJsonFeature holds nullable GeoJsonGeometry represented by LineString or MultiLineString and SeaRouteProperties; a movement is a GeoJsonFeatureCollection of leg features with MovementProperties totals" width="100%">
</p>

Source: [docs/diagrams/output-model.svg](docs/diagrams/output-model.svg) (vector) and [output-model.html](docs/diagrams/output-model.html).

Example output for Jebel Ali (AEJEA) to St John's, Antigua (AGSJO) with Suez closed, trimmed for length:

```json
{
  "type": "Feature",
  "geometry": {
    "type": "LineString",
    "coordinates": [[55.05, 25.02], [56.4, 26.6], [57.2, 24.4], "...", [-61.85, 17.12]]
  },
  "properties": {
    "length": 19258.0,
    "units": "km",
    "duration_hours": 650.0,
    "traversed_passages": ["ormuz", "south_africa"]
  }
}
```

| Property | Meaning |
|---|---|
| `length` | Total route length in the requested unit. |
| `units` | Unit identifier, for example `km`, `naut`, `mi`. |
| `duration_hours` | Length divided by vessel speed, default 16 knots. |
| `port_origin`, `port_dest` | Present when routing by port code or with `IncludePorts`. |
| `traversed_passages` | Present when `ReturnPassages` is set. Lower-case identifiers listed below. |

## Installation

The current version is 2.0.0. It is not yet on nuget.org, so either reference the project directly or build the package locally (see [Building, testing and trying it out](#building-testing-and-trying-it-out)) and add it from that folder:

```bash
dotnet add package SeaRoute.Net --source ./artifacts
```

Targets `net8.0` and `net10.0`. The package has no dependencies beyond the base class library and `System.Text.Json`.

Breaking changes in 2.0.0: `GeoJsonFeature.Geometry` is now nullable `GeoJsonGeometry`; antimeridian routes use `GeoJsonMultiLineString`, and no-route features use null geometry. `Geometry.Coordinates` remains a flattened convenience view; use `Geometry.Positions` for explicit intent or cast a multi-line geometry to access its segments. Indexed custom graphs are immutable, and invalid algorithms, restrictions and physical values now throw instead of being ignored or producing invalid output.

## Usage

### Routing by UN/LOCODE

UN/LOCODE port codes are the primary input. The code must identify one record in the embedded port list; the selected port records are returned in `port_origin` and `port_dest`.

```csharp
var route = SeaRouter.Calculate("FRLEH", "CNTSN"); // Le Havre to Tianjin

Console.WriteLine($"{route.Properties.PortOrigin!.Name} to {route.Properties.PortDest!.Name}");
Console.WriteLine($"{route.Properties.Length:N0} {route.Properties.Units}");
```

Some upstream port codes occur more than once. A code-only route throws when a code is ambiguous instead of choosing an arbitrary record; use `SeaRouteEngine.Default.Ports.GetByCodeCandidates(code)` to inspect those records.

### Engine or static facade

`SeaRouteEngine.Default` is a lazily initialised singleton that owns the graph and port index. Register it for dependency injection, or use it directly:

```csharp
builder.Services.AddSingleton<ISeaRouteEngine>(SeaRouteEngine.Default);
```

```csharp
public sealed class ShippingController(ISeaRouteEngine seaRoute) : ControllerBase
{
    [HttpGet("route")]
    public IActionResult GetRoute(string from, string to)
    {
        var feature = seaRoute.CalculateRoute(from, to);
        return Content(feature.ToJson(), "application/geo+json");
    }
}
```

The static `SeaRouter` class wraps the same engine. Use the `SeaRouteOptions` overload when routing by port code, or the named-parameter overload for coordinate fallback routing.

### Avoiding canals and straits

```csharp
using SeaRoute.Passages;

var route = SeaRouter.Calculate(
    "AEJEA", // Jebel Ali
    "AGSJS", // St John's, Antigua
    new SeaRouteOptions
    {
        Restrictions = [Passage.Suez],
        ReturnPassages = true
    });

// route.Properties.TraversedPassages == ["ormuz", "south_africa"]
```

Recognised passages: `Babalmandab`, `Bering`, `Bosporus`, `Chili` (Magellan Strait), `Dardanelles`, `Gibraltar`, `Malacca`, `Northwest` (restricted by default), `Ormuz`, `Panama`, `SouthAfrica` (Cape of Good Hope), `Suez`, `Sunda`. Each one, and every kind of waypoint a route can contain, is described with measured detour distances in [docs/waypoints-and-choke-points.md](docs/waypoints-and-choke-points.md).

### Routing with coordinates

Coordinates are the fallback when an endpoint has no usable port code, when your authoritative position differs from the embedded data, or when routing to an offshore/custom point. Coordinate order is longitude, latitude.

```csharp
using SeaRoute.Common;

var route = SeaRouter.Calculate(
    new Coordinate(5.333333, 43.333333),    // custom position near Marseille
    new Coordinate(18.366667, -33.916667),  // custom position near Cape Town
    appendOrigDest: true);
```

When a broader UN/LOCODE entry is known but is not a uniquely routable port record, `SeaRouter.Locate(code)` can resolve its published position for use with this coordinate overload.

### Inland points resolved to the nearest terminal

```csharp
using SeaRoute.Ports;

var route = SeaRouter.Calculate(
    SeaRouter.Locate("FRPAR").Coordinate,    // Paris, inland
    SeaRouter.Locate("JPTYO").Coordinate,    // Tokyo
    includePorts: true,
    appendOrigDest: true,
    portParams: new PortParameters { OnlyTerminals = true });

// route.Properties.PortOrigin.PortCode == "FRURO" (Rouen), PortDest == "JPTYO"
```

### Weighted preferred ports per area

```csharp
var belgium = new AreaFeature(
    coordinates: belgiumBoundary,
    name: "BE",
    preferredPorts: [new PortProps("BEANR", share: 250), new PortProps("FRLEH", share: 200)]);

var routes = SeaRouteEngine.Default.CalculateRoutes(SeaRouter.Locate("BEBRU").Coordinate, SeaRouter.Locate("JPTYO").Coordinate, new SeaRouteOptions
{
    IncludePorts = true,
    PortParameters = new PortParameters { PortsInAreasFrom = [belgium] }
});

// Two features: one via Antwerp (share 0.56), one via Le Havre (share 0.44)
```

### Multi-leg movements

A movement is a list of legs, one per line, in the form `[Pickup|Delivery] [port|place|airport|station|terminal|depot] CODE to [...] CODE MODE`, where MODE is Sea, Road, Rail or Air. Declared waypoint types and sea, rail and air modes are checked against known UN/LOCODE functions; caller-only coordinates remain unclassified. Pickup and delivery ordering and continuity between consecutive legs are also validated. Sea legs are routed on the lane network. Road, rail and air legs are straight great-circle lines between their two waypoints, never touching lane points or choke points, with a configurable speed per mode: 60, 80 and 800 km/h by default.

<p align="center">
  <img src="docs/diagrams/movement-flow.png" alt="SeaRoute.Net movement flow: leg lines are parsed, each leg's locations resolved, sea legs routed on Marnet and road, rail or air legs measured as straight great-circle lines with no lane points or choke points, producing one feature per leg and a FeatureCollection with totals; worked examples show a UK to Melbourne movement with its transit time split into travelling and port hours, and a movement with an air leg from Heathrow to Melbourne" width="70%">
</p>

Source: [docs/diagrams/movement-flow.svg](docs/diagrams/movement-flow.svg) (vector) and [movement-flow.html](docs/diagrams/movement-flow.html).

```csharp
using SeaRoute.Movements;

const string legs = """
    Pickup GBLGW to Port GBFXT Road
    Port GBFXT to Port SGSIN Sea
    Port SGSIN to Port AUMEL Sea
    Delivery from port AUMEL to place AUMRS Road
    """;

// Every code resolves from the embedded port list or UN/LOCODE list. For a code neither list can place,
// pass a dictionary of coordinates as the second argument.
var movement = SeaRouter.CalculateMovement(legs);

foreach (var leg in movement.Legs)
    Console.WriteLine($"{leg.Sequence} {leg.Leg.Mode} {leg.From.Label} to {leg.To.Label}: {leg.Length:N0} km");

Console.WriteLine($"{movement.TotalLength:N0} km, {movement.TotalDurationHours:N0} h");
string geoJson = movement.ToJson();   // FeatureCollection, one feature per leg
```

Each leg feature carries `leg`, `mode`, `kind`, `from` and `to` in its properties, and the collection carries `total_length`, `units`, `total_duration_hours` and `legs`. Every leg starts and ends at its resolved locations, so consecutive legs join end to end; `AppendOriginDestination` is always on for movement legs. A sea leg with no route under the given restrictions throws an `InvalidOperationException` naming the leg rather than contributing zero. Build a `MovementRequest` directly to set sea options, per-mode speeds or a resolver.

Codes resolve in this order:

1. A coordinate supplied by the caller in `MovementRequest.Coordinates`.
2. The embedded port list, when the UN/LOCODE list agrees on the place name. Port list positions are tuned to the lane network.
3. The embedded UN/LOCODE list, when UNECE publishes coordinates for the code. This covers airports, rail terminals and inland places, and it wins over the port list when the two disagree: the port list holds `CNSHG` as Sanshan, an inland Yangtze port, while UN/LOCODE holds it as Shanghai Pt.
4. The port list anyway, for codes UN/LOCODE lacks coordinates for.
5. An `ILocationResolver`, if one is set.

Each resolved location reports its `Source`. The embedded UN/LOCODE data has no coordinates for about a fifth of its entries. A small supplement file, [unlocode-supplement.json](src/SeaRoute/Data/unlocode-supplement.json), fills a few of those from cited sources and records the source on the entry; it never overrides UNECE. Codes that neither list can place still need a caller coordinate, and the error for one names the place and its functions. An unknown code throws an `ArgumentException` naming the code rather than guessing. Some port codes occur more than once in the upstream list: code-only lookup throws when ambiguous, `GetByCodeCandidates` returns every record, and `GetByCode(code, near)` disambiguates geographically.

### Time

`duration_hours` on every route and leg is travelling time: distance divided by an assumed average speed.

| Mode | Default speed | Where to change it |
|---|---|---|
| Sea | 16 knots, about 30 km/h | `SeaRouteOptions.SpeedKnots` |
| Road | 60 km/h | `MovementRequest.SpeedsKmh[TransportMode.Road]` |
| Rail | 80 km/h | `MovementRequest.SpeedsKmh[TransportMode.Rail]` |
| Air | 800 km/h | `MovementRequest.SpeedsKmh[TransportMode.Air]` |

The sea default is a slow-steaming service speed rather than a design speed: Clarksons measured the container fleet averaging 13.7 knots in 2023 ([Splash247](https://splash247.com/containerships-moving-at-all-time-low-speeds/)), and Asia to Europe services run at 16 to 20 knots ([Wikipedia, slow steaming](https://en.wikipedia.org/wiki/Slow_steaming)). Earlier versions used 24 knots and under-estimated transit by about half.

Movement legs also carry `port_hours` and `transit_hours`. Every sea leg is charged `MovementRequest.PortDwellHours` at each end, 24 hours by default, covering loading, discharge and transhipment, so a transhipment between two sea legs costs 48 hours. `transit_hours` is travelling plus port time, and the collection carries `total_port_hours` and `total_transit_hours`. Set `PortDwellHours` to 0 for pure steaming time. Customs, waiting for a sailing and schedule effects are still not included, so treat transit as a lower bound: UK to Melbourne via Singapore comes out at about 36 days against the 38 to 50 quoted by forwarders ([Shipa Freight](https://www.shipafreight.com/tradelane/uk-to-australia/)), and Felixstowe to Singapore at about 24 days against a scheduled 29 with intermediate port calls ([Fluent Cargo](https://www.fluentcargo.com/routes/singapore/united-kingdom)).

### Emissions

Every movement leg carries a well-to-wheel CO2e estimate, and the totals add them up. The figures are intensity-based: grams of CO2e per tonne of cargo per kilometre, from the GLEC Framework defaults that ISO 14083 builds on. Pass `cargoTonnes` to get absolute kilograms as well.

```csharp
var movement = SeaRouter.CalculateMovement(legs, cargoTonnes: 20.0);

foreach (var leg in movement.Legs)
    Console.WriteLine($"{leg.Leg.Mode}: {leg.Co2eGramsPerTonneKm} g/t-km, {leg.Co2eKgPerTonne:N1} kg/t, {leg.Co2eKg:N0} kg");

Console.WriteLine($"{movement.TotalCo2eKgPerTonne:N1} kg CO2e per tonne, {movement.TotalCo2eKg:N0} kg for {movement.CargoTonnes} t");
```

Each leg feature gains `co2e_g_per_tonne_km`, `co2e_kg_per_tonne` and, with a cargo weight or TEU count, `co2e_kg` and `co2e_basis`; the collection gains `total_co2e_kg_per_tonne`, `cargo_tonnes`, `cargo_teu` and `total_co2e_kg`.

Pass `cargoTeu` as well for containerised sea freight. Sea legs are then charged per container at 76 g CO2e per TEU-km, because a light box still occupies a whole slot; a 40-foot container counts as 2 TEU and a 40-foot high cube as 2.25. Road, rail and air legs keep using the gross weight, and if only a TEU count is given they assume the GLEC average of 10 t per TEU. Weights are gross physical weight, not chargeable weight, as GLEC and ISO 14083 require.

| Mode | Default, g CO2e per tonne-km, well-to-wheel | GLEC source |
|---|---|---|
| Sea, per tonne | 7.6 | Table 46, industry-average dry container, 76 g per TEU-km at the GLEC average of 10 t per TEU |
| Sea, per container | 76 per TEU-km | Table 46, industry-average dry container, used when a TEU count is given |
| Road | 92 | Europe starting value for an HGV over 20 t gross vehicle weight |
| Rail | 28 | Table 38, European diesel traction, average mixed load |
| Air, under 1,000 km | 1,130 | Table 35, ICAO/IATA RP1678 basis, aircraft type unknown |
| Air, 1,000 to 3,700 km | 700 | Table 35, as above |
| Air, over 3,700 km | 630 | Table 35, as above |

Source: Smart Freight Centre, [GLEC Framework, July 2022 edition](https://smart-freight-centre-media.s3.amazonaws.com/documents/2019_GLEC_Framework_July_2022.pdf), Module 2. These are defaults for when carrier data is unavailable; the sea figure assumes an average dry container on an unknown trade lane, and reefer or trade-lane-specific values differ. Set `MovementRequest.Emissions` to your own `EmissionFactors` to override any of them.

## Known limitations and judgement calls

Everything here is deliberate and documented, but each is a simplification you should know about.

- **Port list versus UN/LOCODE tie-break.** When both lists know a code, the port list position is used only if the two names match or one is a prefix of the other after stripping accents and punctuation. If they disagree, UN/LOCODE's position is used and no port record is attached. Check `Source` on the resolved location when it matters.
- **Supplemented coordinates.** Four codes have coordinates researched by hand rather than published by UNECE; the supplement file names each source.
- **Duplicate port codes.** The tagged upstream list contains 38 codes with multiple records. Code-only lookup never silently chooses one; provide a nearby coordinate or inspect the candidates.
- **UN/LOCODE edition.** The embedded import did not preserve its UNECE publication edition. Its hash and record counts are documented, but it is not claimed to be the latest release.
- **Single routes with several area matches** return the first feature from `CalculateRoute`; use `CalculateRoutes` to see them all.
- **Single routes with no path** return null geometry and zero length rather than throwing; movement legs throw.
- **Untagged lane links.** Three internal tags in the lane data, `segment`, `segment2` and `pacific_ocean`, stitch the antimeridian and are never reported or restrictable.
- **Straight legs.** Road, rail and air legs are great-circle lines, not routed on any network.
- **Time and emissions are estimates** from the defaults in the Time and Emissions sections, with no customs, waiting or schedule effects.
- **Per-thread search buffers** hold about 300 KB for the lifetime of each thread that routes.

## Options reference

| `SeaRouteOptions` | Default | Description |
|---|---|---|
| `Units` | `Km` | `Km`, `Meters`, `Miles`, `Feet`, `Inches`, `Yards`, `NauticalMiles`, `Degrees`, `Radians`, `Centimeters`. |
| `SpeedKnots` | `16` | Vessel speed used for `duration_hours`. A typical slow-steaming service speed; the fleet averaged under 14 knots in 2023. |
| `AppendOriginDestination` | `false` | Prepend the exact origin and append the exact destination to the line. |
| `Restrictions` | `[Northwest]` | Passages whose edges are excluded from the search. |
| `IncludePorts` | `false` | Route from and to the nearest ports instead of the raw points. |
| `PortParameters` | `null` | Terminal-only, country filters, area polygons. `Strict` is true by default: a filter that matches no port yields no port rather than silently widening. |
| `ReturnPassages` | `false` | Populate `traversed_passages`. |
| `Algorithm` | `"dijkstra"` | `"dijkstra"` or `"astar"`. Both return an optimal cost; equal-cost route geometry can differ. |

## Performance

BenchmarkDotNet, Release, Apple M4 Pro, .NET 10, measured at 1.1.0. Results include port resolution, search, normalisation and GeoJSON object construction.

| Scenario | Mean | Allocated |
|---|---|---|
| Marseille to Cape Town, bidirectional Dijkstra | 86 µs | 7.7 KB |
| Marseille to Cape Town, A* | 67 µs | 7.7 KB |
| Shanghai to Rotterdam, bidirectional Dijkstra | 338 µs | 18.7 KB |
| Shanghai to Rotterdam, A* | 298 µs | 18.7 KB |
| Paris to Tokyo with port resolution | 441 µs | 17.0 KB |
| Nearest-port lookup | 31 ns | 0 B |

Cold start, including decompressing and indexing the embedded data, is about 75 ms and happens once per process.

Run the benchmarks yourself:

```bash
dotnet run -c Release --project benchmarks/SeaRoute.Benchmarks
```

## Repository layout

```
SeaRoute.Net.slnx
Directory.Build.props
LICENSE                     Apache-2.0
CLAUDE.md                   contributor rules for AI-assisted changes
src/
  SeaRoute/                 the library, packed as SeaRoute.Net
    Common/                 Coordinate, Haversine, DistanceUnit, antimeridian normaliser, point-in-polygon
    Data/                   marnet.json.gz, ports.json.gz, unlocode.json.gz and their loader
    GeoJson/                Feature, FeatureCollection, LineString/MultiLineString and serializer
    Graph/                  MaritimeGraph, BidirectionalDijkstra, AStar, per-thread search buffers
    Locations/              UN/LOCODE entry, functions and lookup
    Movements/              multi-leg movements: legs, parser, request, result, location resolution
    Passages/               passage identifiers
    Ports/                  Port, PortDatabase, PortParameters, AreaFeature, PortProps
    Spatial/                spherical 3D KD-tree
    ISeaRouteEngine.cs      engine interface
    SeaRouteEngine.cs       ISeaRouteEngine implementation
    SeaRouteOptions.cs      request options
    SeaRouter.cs            static facade
  SeaRoute.Sample/          console app exercising every entry point
tests/
  SeaRoute.Tests/           multi-target xunit suite: routing, passages, ports, spatial, graph and movements
benchmarks/
  SeaRoute.Benchmarks/      BenchmarkDotNet routing benchmarks
docs/
  waypoints-and-choke-points.md   every waypoint type and all 13 passages with measured detours
  diagrams/                 editable HTML diagrams with SVG and selected PNG exports
```

## Building, testing and trying it out

```bash
dotnet build SeaRoute.Net.slnx -c Release -m:1 -nr:false
```

```bash
dotnet test tests/SeaRoute.Tests -c Release -m:1 -nr:false
```

```bash
dotnet run --project src/SeaRoute.Sample
```

The sample prints twelve worked examples covering coordinates, port codes, restrictions, terminal resolution, area weighting, A*, blocked routes, GeoJSON output, the Shanghai to London walkthrough and three multi-leg movements printed as tables, one with an air leg. Add `--geojson` to print a full feature.

To produce the NuGet package locally:

```bash
dotnet pack src/SeaRoute/SeaRoute.csproj -c Release -m:1 -nr:false -o ./artifacts
```

## Data

- **Marnet and antimeridian segments**, transformed from searoute-py 1.6.0: 9,708 nodes and 31,950 directed edges with distance and passage tags.
- **World ports**, transformed from searoute-py 1.6.0: 3,962 records with code, name, country, terminal flag and permitted destination countries.
- **UN/LOCODE**, the UNECE code list for trade and transport locations: 106,588 codes with name and function flags, of which 84,516 carry coordinates to one minute of arc. Used to resolve movement legs that name airports, terminals and inland places. Loaded only when a movement needs it.
- **UN/LOCODE supplement**: a hand-maintained JSON file of coordinates for codes UNECE publishes without any, each with its source. Currently four entries: Gatwick, Shanghai Railway Station, Shanghai Hongqiao and Melrose. Applied only where UNECE has no coordinate.

All datasets are embedded as gzip-compressed JSON, about 1.7 MB in total, and loaded lazily on first use. Exact input and output hashes, transformations and the known UN/LOCODE edition gap are in [DATA_PROVENANCE.md](DATA_PROVENANCE.md); licensing and attribution are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence

SeaRoute.Net code is licensed under the [Apache License, Version 2.0](LICENSE). Embedded data retains its own terms; see [third-party notices](THIRD-PARTY-NOTICES.md).
