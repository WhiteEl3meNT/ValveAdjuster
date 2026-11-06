namespace ValveAdjuster.Core;

/// <summary>An error blocks calculation; a warning is shown but does not block it.</summary>
public sealed record ValidationIssue(bool IsError, string Message);

public static class Validation
{
    public static List<ValidationIssue> Config(EngineConfig config)
    {
        var issues = new List<ValidationIssue>();

        if (config.Cylinders < 1 || config.Cylinders > EngineConfig.MaxCylinders)
            issues.Add(Error($"Cylinders must be between 1 and {EngineConfig.MaxCylinders}."));

        if (config.IntakeValvesPerCylinder < 0 || config.IntakeValvesPerCylinder > EngineConfig.MaxValvesPerCylinder)
            issues.Add(Error($"Intake valves per cylinder must be between 0 and {EngineConfig.MaxValvesPerCylinder}."));

        if (config.ExhaustValvesPerCylinder < 0 || config.ExhaustValvesPerCylinder > EngineConfig.MaxValvesPerCylinder)
            issues.Add(Error($"Exhaust valves per cylinder must be between 0 and {EngineConfig.MaxValvesPerCylinder}."));

        if (config.IntakeValvesPerCylinder + config.ExhaustValvesPerCylinder == 0)
            issues.Add(Error("Define at least one intake or exhaust valve per cylinder."));

        if (config.IntakeValvesPerCylinder > 0)
            issues.AddRange(Spec(config.Intake, "Intake"));

        if (config.ExhaustValvesPerCylinder > 0)
            issues.AddRange(Spec(config.Exhaust, "Exhaust"));

        return issues;
    }

    public static List<ValidationIssue> Spec(ValveTypeSpec spec, string name)
    {
        var issues = new List<ValidationIssue>();

        if (spec.ClearanceMin <= 0)
            issues.Add(Error($"{name}: minimum clearance must be greater than 0."));
        if (spec.ClearanceMax <= spec.ClearanceMin)
            issues.Add(Error($"{name}: maximum clearance must be greater than the minimum."));
        if (spec.ShimIncrement <= 0)
            issues.Add(Error($"{name}: shim increment must be greater than 0."));
        if (spec.ShimMin <= 0)
            issues.Add(Error($"{name}: smallest shim must be greater than 0."));
        if (spec.ShimMax < spec.ShimMin + spec.ShimIncrement)
            issues.Add(Error($"{name}: the shim range must include at least two sizes."));

        return issues;
    }

    public static List<ValidationIssue> Input(ValveRecord valve, ValveTypeSpec spec)
    {
        var issues = new List<ValidationIssue>();

        if (valve.InitialGap is decimal gap && gap <= 0)
            issues.Add(Error("Measured clearance must be greater than 0."));

        if (valve.FinalGap is decimal final && final <= 0)
            issues.Add(Error("Final clearance must be greater than 0."));

        if (valve.ExistingShim is decimal shim)
        {
            if (shim <= 0)
                issues.Add(Error("Existing shim must be greater than 0."));
            else if (shim < spec.ShimMin || shim > spec.ShimMax)
                issues.Add(Warning($"Existing shim {shim:0.000} is outside the configured shim range."));
        }

        if (valve.InstalledShim is decimal installed)
        {
            if (installed < spec.ShimMin || installed > spec.ShimMax)
                issues.Add(Error("Installed shim is outside the available shim range."));
            else if ((installed - spec.ShimMin) % spec.ShimIncrement != 0)
                issues.Add(Error("Installed shim is not one of the available shim sizes."));
        }

        return issues;
    }

    private static ValidationIssue Error(string message) => new(true, message);
    private static ValidationIssue Warning(string message) => new(false, message);
}
