using System.Globalization;
using FluentAssertions;
using SeaRoute.Movements;
using Xunit;

namespace SeaRoute.Tests;

public class MovementTextTests
{
    [Fact]
    public void ToText_ReportsTheCompleteMovementWithoutCallerFormatting()
    {
        var plan = MovementPlan
            .From(Waypoint.Place("GBLGW"))
            .PickupTo(Waypoint.Port("GBFXT"), TransportMode.Road)
            .ThenTo(Waypoint.Port("SGSIN"), TransportMode.Sea)
            .ThenTo(Waypoint.Port("AUMEL"), TransportMode.Sea)
            .DeliverTo(Waypoint.Place("AUMRS"), TransportMode.Road);
        var result = SeaRouter.CalculateMovement(
            plan,
            seaOptions: new SeaRouteOptions { ReturnPassages = true },
            cargoTonnes: 12.0,
            cargoTeu: 2.0);

        string text = result.ToText();

        text.Should().StartWith("Leg Kind");
        text.Should().Contain("Pickup    Road  GBLGW  GBFXT");
        text.Should().Contain("Main      Sea   GBFXT  SGSIN");
        text.Should().Contain("Delivery  Road  AUMEL  AUMRS");
        text.Should().Contain("Gibraltar, Suez, Bab-el-Mandeb, Malacca");
        text.Should().Contain("Sunda");
        text.Should().Contain(result.TotalLength.ToString("N0", CultureInfo.InvariantCulture));
        text.Should().Contain(result.TotalTransitHours.ToString("N1", CultureInfo.InvariantCulture));
        text.Should().Contain(result.TotalCo2eKg!.Value.ToString("N0", CultureInfo.InvariantCulture));
        text.Should().Contain("for 12 t of cargo in 2 TEU");
        text.Should().Contain("76 g per TEU-km on sea legs");
    }

    [Fact]
    public void ToText_CanOmitColumnExplanations()
    {
        var result = SeaRouter.CalculateMovement("Port GBFXT to Port SGSIN Sea");

        string text = result.ToText(includeExplanations: false);

        text.Should().Contain("GBFXT  SGSIN");
        text.Should().Contain("Transit        =");
        text.Should().NotContain("grams of CO2e emitted");
    }
}
