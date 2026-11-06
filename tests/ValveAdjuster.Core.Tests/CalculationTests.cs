using System.Globalization;
using ValveAdjuster.Core;
using Xunit;

namespace ValveAdjuster.Core.Tests;

// Expected values come from recalculating the original Valve_Adjustment_form.xls
// (Sheet1) in LibreOffice. Exhaust spec 0.15-0.22 gives a target of 0.185;
// intake spec 0.15-0.20 gives a target of 0.175. Shim increments are 0.025.

public class CalculationTests
{
    private static decimal D(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    [Fact]
    public void Target_clearance_is_midpoint_of_range()
    {
        Assert.Equal(0.185m, AdjustmentCalculator.TargetClearance(0.15m, 0.22m));
        Assert.Equal(0.175m, AdjustmentCalculator.TargetClearance(0.15m, 0.20m));
    }

    [Theory]
    [InlineData("0.25", "2.50", "2.565")] // sheet B9 with measured 0.25, shim 2.50
    [InlineData("0.10", "2.40", "2.315")] // sheet B9 with measured 0.10, shim 2.40
    [InlineData("0.40", "2.80", "3.015")] // sheet B9 with measured 0.40, shim 2.80
    public void Exhaust_required_shim_matches_spreadsheet(string gap, string shim, string expected)
    {
        Assert.Equal(D(expected), AdjustmentCalculator.RequiredShim(D(shim), D(gap), 0.185m));
    }

    [Fact]
    public void Intake_required_shim_matches_spreadsheet()
    {
        Assert.Equal(2.595m, AdjustmentCalculator.RequiredShim(2.60m, 0.17m, 0.175m));
    }

    [Theory]
    [InlineData("0.25", "2.50", "2.55", "0.20")] // sheet B11 with plan 2.55
    [InlineData("0.10", "2.40", "2.40", "0.10")] // sheet B11 with plan 2.40
    [InlineData("0.40", "2.80", "3.05", "0.15")] // sheet B11 with plan 3.05
    public void Expected_clearance_matches_spreadsheet(string gap, string existing, string planned, string expected)
    {
        Assert.Equal(D(expected), AdjustmentCalculator.ExpectedClearance(D(gap), D(existing), D(planned)));
    }

    [Fact]
    public void Intake_expected_clearance_matches_spreadsheet()
    {
        Assert.Equal(0.12m, AdjustmentCalculator.ExpectedClearance(0.17m, 2.60m, 2.65m));
    }

    [Fact]
    public void Closest_available_rounds_to_nearest_step()
    {
        var spec = ExhaustSpec();
        Assert.Equal(2.575m, AdjustmentCalculator.ClosestAvailable(2.565m, spec)); // 2.565 is nearer 2.575
        Assert.Equal(2.550m, AdjustmentCalculator.ClosestAvailable(2.540m, spec));
    }

    [Fact]
    public void Closest_available_breaks_ties_toward_thinner_shim()
    {
        Assert.Equal(2.550m, AdjustmentCalculator.ClosestAvailable(2.5625m, ExhaustSpec()));
    }

    [Fact]
    public void Closest_available_clamps_to_range()
    {
        var spec = ExhaustSpec();
        Assert.Equal(2.000m, AdjustmentCalculator.ClosestAvailable(1.000m, spec));
        Assert.Equal(3.500m, AdjustmentCalculator.ClosestAvailable(9.000m, spec));
    }

    [Fact]
    public void Available_shims_lists_every_step()
    {
        var shims = AdjustmentCalculator.AvailableShims(ExhaustSpec());
        Assert.Equal(61, shims.Count); // 2.000 to 3.500 in 0.025 steps
        Assert.Equal(2.000m, shims[0]);
        Assert.Equal(3.500m, shims[^1]);
    }

    [Fact]
    public void Evaluate_reports_calculation_and_difference()
    {
        var valve = new ValveRecord { Type = ValveType.Exhaust, Cylinder = 1, Index = 1, InitialGap = 0.25m, ExistingShim = 2.50m };
        var r = AdjustmentCalculator.Evaluate(Engine(), valve);

        Assert.Equal(0.185m, r.TargetClearance);
        Assert.Equal(2.565m, r.CalculatedShim);
        Assert.Equal(2.575m, r.RecommendedShim);
        Assert.Equal(0.010m, r.ShimDifference);
        Assert.Equal(0.175m, r.ExpectedClearance); // 0.25 + 2.50 - 2.575 when the recommended shim is used
        Assert.Equal(ValveStatus.NeedsAdjustment, r.Status);
        Assert.Contains("2.565", r.Explanation);
    }

    [Fact]
    public void Status_follows_the_adjustment_workflow()
    {
        var cfg = Engine();

        var inSpec = new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.18m, ExistingShim = 2.50m };
        Assert.Equal(ValveStatus.InSpec, AdjustmentCalculator.Evaluate(cfg, inSpec).Status);

        var awaiting = new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.25m, ExistingShim = 2.50m, InstalledShim = 2.55m };
        Assert.Equal(ValveStatus.AwaitingVerification, AdjustmentCalculator.Evaluate(cfg, awaiting).Status);

        var pass = new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.25m, ExistingShim = 2.50m, InstalledShim = 2.55m, FinalGap = 0.18m };
        Assert.Equal(ValveStatus.PassVerified, AdjustmentCalculator.Evaluate(cfg, pass).Status);

        var fail = new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.25m, ExistingShim = 2.50m, InstalledShim = 2.55m, FinalGap = 0.25m };
        Assert.Equal(ValveStatus.FailVerified, AdjustmentCalculator.Evaluate(cfg, fail).Status);

        var empty = new ValveRecord { Type = ValveType.Exhaust };
        Assert.Equal(ValveStatus.NotMeasured, AdjustmentCalculator.Evaluate(cfg, empty).Status);
    }

    [Fact]
    public void Installed_shim_off_the_increment_is_an_error()
    {
        var valve = new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.25m, ExistingShim = 2.50m, InstalledShim = 2.51m };
        var r = AdjustmentCalculator.Evaluate(Engine(), valve);
        Assert.True(r.HasErrors);
        Assert.Null(r.CalculatedShim);
    }

    [Fact]
    public void Invalid_spec_blocks_calculation()
    {
        var cfg = Engine();
        cfg.Exhaust.ClearanceMax = 0.10m; // max below min
        var r = AdjustmentCalculator.Evaluate(cfg, new ValveRecord { Type = ValveType.Exhaust, InitialGap = 0.25m, ExistingShim = 2.50m });
        Assert.True(r.HasErrors);
        Assert.Null(r.RecommendedShim);
    }

    [Fact]
    public void Config_rejects_zero_valves()
    {
        var cfg = Engine();
        cfg.IntakeValvesPerCylinder = 0;
        cfg.ExhaustValvesPerCylinder = 0;
        Assert.Contains(Validation.Config(cfg), i => i.IsError);
    }

    [Fact]
    public void BuildValves_creates_expected_layout_and_keeps_measurements()
    {
        var cfg = Engine();
        cfg.Cylinders = 2;
        var first = cfg.BuildValves();
        Assert.Equal(4, first.Count); // 2 cylinders x (1 intake + 1 exhaust)

        first[1].InitialGap = 0.25m;   // cyl 1 EX 1
        cfg.Cylinders = 3;
        var second = cfg.BuildValves(first);
        Assert.Equal(6, second.Count);
        Assert.Equal(0.25m, second.Single(v => v.Key == "Exhaust-1-1").InitialGap);
    }

    [Fact]
    public void Summary_counts_each_category()
    {
        var cfg = Engine();
        var results = new[]
        {
            new ValveRecord { Type = ValveType.Exhaust, Cylinder = 1, InitialGap = 0.18m, ExistingShim = 2.5m },                   // in spec
            new ValveRecord { Type = ValveType.Exhaust, Cylinder = 2, InitialGap = 0.25m, ExistingShim = 2.5m },                   // needs adjustment
            new ValveRecord { Type = ValveType.Intake, Cylinder = 1, InitialGap = 0.25m, ExistingShim = 2.5m, InstalledShim = 2.55m }, // awaiting
            new ValveRecord { Type = ValveType.Intake, Cylinder = 2 },                                                           // not measured
        }.Select(v => AdjustmentCalculator.Evaluate(cfg, v));

        var s = AdjustmentSummary.From(results);
        Assert.Equal(4, s.Total);
        Assert.Equal(1, s.WithinSpec);
        Assert.Equal(1, s.NeedsAdjustment);
        Assert.Equal(1, s.Completed);
        Assert.Equal(1, s.PendingVerification);
        Assert.Equal(0, s.FailedVerification);
        Assert.Equal(1, s.NotMeasured);
    }

    [Fact]
    public void Session_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"valveadj-{Guid.NewGuid():N}.json");
        try
        {
            var cfg = Engine();
            var valves = cfg.BuildValves();
            valves[0].InitialGap = 0.25m;
            valves[0].ExistingShim = 2.50m;
            valves[0].Notes = "Cold, \"checked twice\"";

            SessionStore.Save(path, cfg, valves);
            var loaded = SessionStore.Load(path);

            Assert.Equal(cfg.Name, loaded.Engine.Name);
            Assert.Equal(valves.Count, loaded.Valves.Count);
            Assert.Equal(0.25m, loaded.Valves[0].InitialGap);
            Assert.Equal(2.50m, loaded.Valves[0].ExistingShim);
            Assert.Equal("Cold, \"checked twice\"", loaded.Valves[0].Notes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ValveTypeSpec ExhaustSpec() => new()
    {
        ClearanceMin = 0.15m,
        ClearanceMax = 0.22m,
        ShimMin = 2.000m,
        ShimMax = 3.500m,
        ShimIncrement = 0.025m
    };

    private static EngineConfig Engine() => new()
    {
        Name = "Test engine",
        Units = "mm",
        Cylinders = 1,
        IntakeValvesPerCylinder = 1,
        ExhaustValvesPerCylinder = 1,
        Intake = new ValveTypeSpec { ClearanceMin = 0.15m, ClearanceMax = 0.20m, ShimMin = 2.000m, ShimMax = 3.500m, ShimIncrement = 0.025m },
        Exhaust = ExhaustSpec()
    };
}
