using System.Globalization;

namespace ValveAdjuster.Core;

/// <summary>
/// Calculation logic ported from the original spreadsheet (Sheet1):
///   Perfect gap     B8  = (Max - Min) / 2 + Min
///   Shim needed     B9  = Shim - (PerfectGap - MeasuredGap)
///   Expected gap    B11 = ShimNeeded + PerfectGap - PlanToInstall
/// Algebraically, expected gap = MeasuredGap + ExistingShim - NewShim.
/// </summary>
public static class AdjustmentCalculator
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static decimal TargetClearance(decimal min, decimal max) => min + (max - min) / 2m;

    public static decimal RequiredShim(decimal existingShim, decimal measuredGap, decimal targetClearance) =>
        existingShim - (targetClearance - measuredGap);

    public static decimal ExpectedClearance(decimal measuredGap, decimal existingShim, decimal newShim) =>
        measuredGap + existingShim - newShim;

    /// <summary>
    /// Every shim size the spec allows, from ShimMin to ShimMax in ShimIncrement steps.
    /// </summary>
    public static IReadOnlyList<decimal> AvailableShims(ValveTypeSpec spec)
    {
        var shims = new List<decimal>();
        var steps = (int)Math.Floor((spec.ShimMax - spec.ShimMin) / spec.ShimIncrement);
        for (int i = 0; i <= steps; i++)
        {
            shims.Add(spec.ShimMin + i * spec.ShimIncrement);
        }
        return shims;
    }

    /// <summary>
    /// Nearest available shim to the required thickness. Ties go to the thinner shim,
    /// which leaves more clearance; the original sheet advises favoring maximum clearance.
    /// Results are clamped to the available range.
    /// </summary>
    public static decimal ClosestAvailable(decimal required, ValveTypeSpec spec)
    {
        var inc = spec.ShimIncrement;
        var maxSteps = Math.Floor((spec.ShimMax - spec.ShimMin) / inc);
        var steps = (required - spec.ShimMin) / inc;
        var floor = Math.Floor(steps);
        var n = (steps - floor) > 0.5m ? floor + 1m : floor;
        n = Math.Clamp(n, 0m, maxSteps);
        return spec.ShimMin + n * inc;
    }

    public static ValveResult Evaluate(EngineConfig config, ValveRecord valve)
    {
        var result = new ValveResult(valve);
        var spec = config.Spec(valve.Type);

        result.Issues.AddRange(Validation.Spec(spec, valve.Type.ToString()));
        if (result.HasErrors) return result;

        result.Issues.AddRange(Validation.Input(valve, spec));
        if (result.HasErrors) return result;

        var target = TargetClearance(spec.ClearanceMin, spec.ClearanceMax);
        result.TargetClearance = target;

        if (valve.InitialGap is decimal gap && valve.ExistingShim is decimal shim)
        {
            var required = RequiredShim(shim, gap, target);
            var recommended = ClosestAvailable(required, spec);

            result.CalculatedShim = required;
            result.RecommendedShim = recommended;
            result.ShimDifference = recommended - required;
            result.Explanation =
                $"Required shim = {Fmt(shim)} - ({Fmt(target)} - {Fmt(gap)}) = {Fmt(required)}";

            if (required < spec.ShimMin || required > spec.ShimMax)
            {
                result.Issues.Add(new ValidationIssue(false,
                    "The required shim is outside the available range. The nearest available shim is shown."));
            }
        }

        var replacement = valve.InstalledShim ?? result.RecommendedShim;
        if (replacement is decimal newShim
            && valve.InitialGap is decimal g
            && valve.ExistingShim is decimal s)
        {
            result.ExpectedClearance = ExpectedClearance(g, s, newShim);
        }

        result.Status = DetermineStatus(valve, spec);
        return result;
    }

    public static List<ValveResult> EvaluateAll(EngineConfig config, IEnumerable<ValveRecord> valves) =>
        valves.Select(v => Evaluate(config, v)).ToList();

    private static ValveStatus DetermineStatus(ValveRecord valve, ValveTypeSpec spec)
    {
        if (valve.FinalGap is decimal final)
            return InRange(final, spec) ? ValveStatus.PassVerified : ValveStatus.FailVerified;

        if (valve.InstalledShim is not null)
            return ValveStatus.AwaitingVerification;

        if (valve.InitialGap is decimal initial)
            return InRange(initial, spec) ? ValveStatus.InSpec : ValveStatus.NeedsAdjustment;

        return ValveStatus.NotMeasured;
    }

    private static bool InRange(decimal gap, ValveTypeSpec spec) =>
        gap >= spec.ClearanceMin && gap <= spec.ClearanceMax;

    private static string Fmt(decimal value) => value.ToString("0.000", Inv);
}
