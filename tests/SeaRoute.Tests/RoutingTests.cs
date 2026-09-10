using System.Text.Json;
using FluentAssertions;
using SeaRoute.Common;
using SeaRoute.GeoJson;
using Xunit;

namespace SeaRoute.Tests;

public class RoutingTests
{
    [Fact]
    public void MarseilleToCapeTown_NoAppend_ShouldMatchExpectedLength()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = SeaRouter.Calculate(origin, dest, appendOrigDest: false);

        route.Properties.Units.Should().Be("km");
        route.Properties.Length.Should().BeApproximately(10986.505, 1.0);
        route.Geometry.Coordinates.Count.Should().Be(59);

        // A* algorithm should yield identical route length
        var routeAStar = SeaRouter.Calculate(origin, dest, appendOrigDest: false, algorithm: "astar");
        routeAStar.Properties.Length.Should().BeApproximately(route.Properties.Length, 1e-3);
    }

    [Fact]
    public void MarseilleToCapeTown_AppendOrigDest_ShouldMatchExpectedLength()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = SeaRouter.Calculate(origin, dest, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(10996.763, 1.0);
        route.Geometry.Coordinates.Count.Should().Be(61);

        // First coord must be origin and last must be dest
        route.Geometry.Coordinates[0][0].Should().BeApproximately(origin.Longitude, 1e-5);
        route.Geometry.Coordinates[0][1].Should().BeApproximately(origin.Latitude, 1e-5);
        route.Geometry.Coordinates[^1][0].Should().BeApproximately(dest.Longitude, 1e-5);
        route.Geometry.Coordinates[^1][1].Should().BeApproximately(dest.Latitude, 1e-5);
    }

    [Fact]
    public void MarseilleToCapeTown_UnitConversions_ShouldMatchExpectedOutputs()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var routeMiles = SeaRouter.Calculate(origin, dest, units: DistanceUnit.Miles, appendOrigDest: false);
        routeMiles.Properties.Units.Should().Be("mi");
        routeMiles.Properties.Length.Should().BeApproximately(6826.70, 1.0);
        routeMiles.Properties.DurationHours.Should().BeApproximately(370.77, 0.5);

        var routeNaut = SeaRouter.Calculate(origin, dest, units: DistanceUnit.NauticalMiles, appendOrigDest: false);
        routeNaut.Properties.Units.Should().Be("naut");
        routeNaut.Properties.Length.Should().BeApproximately(5932.24, 1.0);
        routeNaut.Properties.DurationHours.Should().BeApproximately(370.77, 0.5);
    }

    [Fact]
    public void ShanghaiToRotterdam_MajorCommercialRoute_ShouldMatchExpectedLength()
    {
        var shanghai = new Coordinate(121.47, 31.23);
        var rotterdam = new Coordinate(4.48, 51.92);

        var route = SeaRouter.Calculate(shanghai, rotterdam, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(19646.929, 2.0);
        route.Geometry.Coordinates.Count.Should().Be(159);
    }

    [Fact]
    public void TransPacific_YokohamaToLosAngeles_CrossesAntimeridianCorrectly()
    {
        var yokohama = new Coordinate(139.64, 35.44);
        var losAngeles = new Coordinate(-118.24, 33.74);

        var route = SeaRouter.Calculate(yokohama, losAngeles, appendOrigDest: true);

        route.Properties.Length.Should().BeApproximately(9126.579, 2.0);
        route.Geometry.Coordinates.Count.Should().Be(55);

        // Verify antimeridian continuity: longitudes should not have sudden ~360 degree jumps
        for (int i = 0; i < route.Geometry.Coordinates.Count - 1; i++)
        {
            double diff = Math.Abs(route.Geometry.Coordinates[i + 1][0] - route.Geometry.Coordinates[i][0]);
            diff.Should().BeLessThan(180.0, "Route coordinates must be continuous across the antimeridian");
        }
    }

    [Fact]
    public void GeoJsonFeature_SerializationAndDeserialization_IsValidGeoJson()
    {
        var origin = new Coordinate(5.333333, 43.333333);
        var dest = new Coordinate(18.366667, -33.916667);

        var route = SeaRouter.Calculate(origin, dest, appendOrigDest: true);
        string json = route.ToJson();

        json.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("type").GetString().Should().Be("Feature");
        root.GetProperty("geometry").GetProperty("type").GetString().Should().Be("LineString");
        root.GetProperty("geometry").GetProperty("coordinates").GetArrayLength().Should().Be(61);
        root.GetProperty("properties").GetProperty("units").GetString().Should().Be("km");
        root.GetProperty("properties").GetProperty("length").GetDouble().Should().BeApproximately(10996.763, 1.0);
    }
}
