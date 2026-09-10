# SeaRoute.Net 🚢

A high-performance, zero-external-dependency .NET 8 / .NET 10 maritime routing library based on the Eurostat maritime routing network (Marnet) and World Ports dataset.

Calculates the shortest sea route between any two coordinates or ports on Earth with support for dynamic passage restrictions (Suez, Panama, Gibraltar, etc.), vessel speeds, unit conversions, port queries, antimeridian normalization, and standard GeoJSON output.

---

## Features

- ⚡ **Ultra High Performance**: Sub-millisecond routing queries powered by in-memory bidirectional Dijkstra and A* algorithms.
- 📦 **Zero External Dependencies**: Core library relies strictly on the .NET BCL and `System.Text.Json`.
- 💾 **Self-Contained Embedded Data**: Compressed GZip datasets embedded directly in assembly (~360 KB total for 9,708 nodes, 31,940 maritime network edges, and 3,955 world ports).
- 🧭 **Dynamic Passage Restrictions**: Avoid canals or straits like Suez, Panama, Gibraltar, Malacca, Ormuz, Bab-el-Mandeb, etc.
- ⚓ **Port Resolution & Area Allocation**: Automatic closest port detection, container terminal filtering, country restrictions, and polygon-based preferred port matrix generation.
- 🌐 **Antimeridian Normalization**: Seamlessly handles trans-Pacific voyages crossing ±180° longitude without map wrapping artifacts.
- 📏 **Unit Conversions**: Supports kilometers, nautical miles, statute miles, meters, feet, inches, yards, radians, and degrees.
- 🗺️ **RFC 7946 GeoJSON Output**: Ready to feed into Leaflet, Mapbox, OpenLayers, Deck.gl, or GIS pipelines.
- 🧵 **Thread-Safe & Scalable**: Fully immutable graph and thread-safe engine designed for high-concurrency cloud workloads.

---

## Installation

```bash
dotnet add package SeaRoute.Net
```

Or via `<PackageReference Include="SeaRoute.Net" Version="1.0.0" />`.

---

## Quick Start

### 1. Basic Route Calculation

```csharp
using SeaRoute;
using SeaRoute.Common;

// Origin: Marseille (5.33° E, 43.33° N)
// Destination: Cape Town (18.37° E, -33.92° N)
var route = SeaRoute.Calculate(
    origin: new Coordinate(5.333333, 43.333333),
    destination: new Coordinate(18.366667, -33.916667),
    appendOrigDest: true
);

Console.WriteLine($"Distance: {route.Properties.Length:F1} {route.Properties.Units}");
Console.WriteLine($"Duration: {route.Properties.DurationHours:F1} hours @ 24 knots");
Console.WriteLine($"GeoJSON: {route.ToJson()}");
```

### 2. Avoiding Canals & Straits (Passage Restrictions)

You can specify passages to avoid. For example, routing around Africa by restricting the Suez canal:

```csharp
using SeaRoute;
using SeaRoute.Common;
using SeaRoute.Passages;

var route = SeaRoute.Calculate(
    origin: new Coordinate(52.99, 25.01),     // Persian Gulf
    destination: new Coordinate(-61.87, 17.15), // Caribbean
    restrictions: [Passage.Suez],
    returnPassages: true
);

// Returns traversed passages: ["ormuz", "south_africa"]
foreach (var passage in route.Properties.TraversedPassages!)
{
    Console.WriteLine($"Traversed: {passage}");
}
```

Recognized passages:
- `Passage.Babalmandab` (Bab-el-Mandeb)
- `Passage.Bosporus`
- `Passage.Gibraltar`
- `Passage.Suez` (Suez Canal)
- `Passage.Panama` (Panama Canal)
- `Passage.Ormuz` (Strait of Hormuz)
- `Passage.Northwest` (Northwest Passage - restricted by default)
- `Passage.Malacca` (Strait of Malacca)
- `Passage.Sunda`
- `Passage.Chili` (Magellan Strait)
- `Passage.SouthAfrica` (Cape of Good Hope)
- `Passage.Bering` (Bering Strait)

### 3. Routing Between Ports (UN/LOCODE or Port Codes)

```csharp
// Le Havre ("FRLEH") to Tianjin ("CNTSN")
var route = SeaRoute.Calculate("FRLEH", "CNTSN");

Console.WriteLine($"Distance: {route.Properties.Length:N0} km");
```

### 4. Automatic Port Resolution (`IncludePorts = true`)

Calculate sea routes between inland locations (e.g. Paris to Tokyo) by automatically finding the closest major container terminal:

```csharp
var route = SeaRoute.Calculate(
    origin: new Coordinate(2.333333, 48.866667),     // Paris
    destination: new Coordinate(139.679174, 35.778467), // Tokyo
    includePorts: true,
    appendOrigDest: true,
    portParams: new PortParameters
    {
        OnlyTerminals = true // Route only through major cargo terminals
    }
);

Console.WriteLine($"Departure Port: {route.Properties.PortOrigin?.Name} ({route.Properties.PortOrigin?.PortCode})");
Console.WriteLine($"Arrival Port: {route.Properties.PortDest?.Name} ({route.Properties.PortDest?.PortCode})");
```

### 5. Area Features with Preferred Port Weighting

If an origin area (such as Belgium) has multiple preferred export ports, you can specify polygon boundaries with assigned share weights:

```csharp
var areaBE = new AreaFeature(
    coordinates: belgiumBoundaryCoords,
    name: "BE",
    preferredPorts:
    [
        new PortProps("FRLEH", share: 200),
        new PortProps("BEANR", share: 250)
    ]
);

var options = new SeaRouteOptions
    {
    IncludePorts = true,
    PortParameters = new PortParameters
    {
        PortsInAreasFrom = [areaBE]
    }
};

// Returns 2 distinct GeoJSON route features (one via Antwerp, one via Le Havre)
IReadOnlyList<GeoJsonFeature> routes = SeaRoute.CalculateRoutes(brusselsPoint, tokyoPoint, options);
```

### 6. Dependency Injection Setup

Register `ISeaRouteEngine` in ASP.NET Core `Program.cs`:

```csharp
builder.Services.AddSingleton<ISeaRouteEngine, SeaRouteEngine>();
```

Inject and consume:

```csharp
public class ShippingController(ISeaRouteEngine seaRoute) : ControllerBase
{
    [HttpGet("route")]
    public IActionResult GetRoute(double fromLon, double fromLat, double toLon, double toLat)
    {
        var feature = seaRoute.CalculateRoute(fromLon, fromLat, toLon, toLat);
        return Content(feature.ToJson(), "application/geo+json");
    }
}
```

---

## Unit Conversions

Configure distance units and vessel speed:

```csharp
var route = SeaRoute.Calculate(
    origin,
    destination,
    units: DistanceUnit.NauticalMiles, // Or Miles, Km, Meters, Feet, etc.
    speedKnots: 20.0
);
```

| `DistanceUnit` | String Identifier | Description |
|---|---|---|
| `DistanceUnit.Km` | `"km"` | Kilometers (Default) |
| `DistanceUnit.NauticalMiles` | `"naut"` / `"nm"` | Nautical Miles |
| `DistanceUnit.Miles` | `"mi"` | Statute Miles |
| `DistanceUnit.Meters` | `"m"` | Meters |
| `DistanceUnit.Feet` | `"ft"` | Feet |
| `DistanceUnit.Inches` | `"in"` | Inches |
| `DistanceUnit.Yards` | `"yd"` | Yards |
| `DistanceUnit.Degrees` | `"deg"` | Degrees |
| `DistanceUnit.Radians` | `"rad"` | Radians |

---

## Algorithm Options

By default, routes are calculated using a **Bidirectional Dijkstra** search with custom passage avoidance weights. You can also select the **A\*** algorithm:

```csharp
var route = SeaRoute.Calculate(origin, destination, algorithm: "astar");
```

---

## Verification & Tests

The test suite in `tests/SeaRoute.Tests` covers:
- Port queries, terminal filters, and country restrictions.
- Exact route lengths and coordinate counts across major global shipping lanes (Marseille-Cape Town, Shanghai-Rotterdam, Yokohama-Los Angeles, New York-London).
- Blocked passage detection (e.g. Singapore to Piraeus with Suez + Gibraltar restricted returning empty coordinates and 0 length).
- Antimeridian crossing continuity without coordinate jumps.
- Multi-threaded concurrent execution test across 100 parallel queries.

---

## License

Licensed under the [Apache License, Version 2.0](https://www.apache.org/licenses/LICENSE-2.0).
Based on Eurostat marnet data.
