using System.Text.Json.Serialization;

namespace ValveAdjuster.Core;

public enum ValveType
{
    Intake,
    Exhaust
}

public enum ValveStatus
{
    NotMeasured,
    InSpec,
    NeedsAdjustment,
    AwaitingVerification,
    PassVerified,
    FailVerified
}

public static class ValveStatusExtensions
{
    public static string Display(this ValveStatus status) => status switch
    {
        ValveStatus.NotMeasured => "Not measured",
        ValveStatus.InSpec => "Within spec",
        ValveStatus.NeedsAdjustment => "Needs adjustment",
        ValveStatus.AwaitingVerification => "Pending verification",
        ValveStatus.PassVerified => "Pass",
        ValveStatus.FailVerified => "Fail",
        _ => status.ToString()
    };
}

/// <summary>Clearance window and available shim sizes for one valve type (intake or exhaust).</summary>
public sealed class ValveTypeSpec
{
    public decimal ClearanceMin { get; set; }
    public decimal ClearanceMax { get; set; }
    public decimal ShimMin { get; set; }
    public decimal ShimMax { get; set; }
    public decimal ShimIncrement { get; set; }
}

public sealed class EngineConfig
{
    public const int MaxCylinders = 16;
    public const int MaxValvesPerCylinder = 4;

    public string Name { get; set; } = "";
    public string Units { get; set; } = "mm";
    public int Cylinders { get; set; } = 1;
    public int IntakeValvesPerCylinder { get; set; } = 1;
    public int ExhaustValvesPerCylinder { get; set; } = 1;
    public ValveTypeSpec Intake { get; set; } = new();
    public ValveTypeSpec Exhaust { get; set; } = new();

    public ValveTypeSpec Spec(ValveType type) => type == ValveType.Intake ? Intake : Exhaust;

    public int ValvesPerCylinder(ValveType type) =>
        type == ValveType.Intake ? IntakeValvesPerCylinder : ExhaustValvesPerCylinder;

    /// <summary>
    /// Builds the valve list for this configuration. Existing records are reused by key
    /// (type, cylinder, index) so measurements survive a configuration change.
    /// </summary>
    public List<ValveRecord> BuildValves(IEnumerable<ValveRecord>? existing = null)
    {
        var previous = (existing ?? Enumerable.Empty<ValveRecord>())
            .GroupBy(v => v.Key)
            .ToDictionary(g => g.Key, g => g.First());

        var valves = new List<ValveRecord>();
        for (int cyl = 1; cyl <= Cylinders; cyl++)
        {
            foreach (var type in new[] { ValveType.Intake, ValveType.Exhaust })
            {
                for (int idx = 1; idx <= ValvesPerCylinder(type); idx++)
                {
                    var key = ValveRecord.MakeKey(type, cyl, idx);
                    valves.Add(previous.TryGetValue(key, out var old)
                        ? old
                        : new ValveRecord { Type = type, Cylinder = cyl, Index = idx });
                }
            }
        }
        return valves;
    }
}

/// <summary>One valve's measurements. Null means "not entered yet".</summary>
public sealed class ValveRecord
{
    public ValveType Type { get; set; }
    public int Cylinder { get; set; } = 1;
    public int Index { get; set; } = 1;

    /// <summary>Clearance measured by feeler gauge before adjustment.</summary>
    public decimal? InitialGap { get; set; }

    /// <summary>Shim currently installed, measured with a micrometer.</summary>
    public decimal? ExistingShim { get; set; }

    /// <summary>Shim chosen or installed as the replacement.</summary>
    public decimal? InstalledShim { get; set; }

    /// <summary>Clearance measured after the replacement shim was installed.</summary>
    public decimal? FinalGap { get; set; }

    public string Notes { get; set; } = "";

    [JsonIgnore]
    public string Key => MakeKey(Type, Cylinder, Index);

    [JsonIgnore]
    public string Label => $"Cyl {Cylinder} {(Type == ValveType.Intake ? "IN" : "EX")} {Index}";

    public static string MakeKey(ValveType type, int cylinder, int index) => $"{type}-{cylinder}-{index}";
}

/// <summary>Calculated output for one valve, plus any validation messages.</summary>
public sealed class ValveResult
{
    public ValveResult(ValveRecord valve) => Valve = valve;

    public ValveRecord Valve { get; }
    public decimal? TargetClearance { get; set; }

    /// <summary>Unrounded shim thickness the formula asks for.</summary>
    public decimal? CalculatedShim { get; set; }

    /// <summary>Closest shim that is actually available.</summary>
    public decimal? RecommendedShim { get; set; }

    /// <summary>RecommendedShim minus CalculatedShim.</summary>
    public decimal? ShimDifference { get; set; }

    /// <summary>Clearance expected after installing InstalledShim (or RecommendedShim if none is set).</summary>
    public decimal? ExpectedClearance { get; set; }

    public ValveStatus Status { get; set; } = ValveStatus.NotMeasured;

    /// <summary>Human-readable working for the calculation, so the result can be checked by hand.</summary>
    public string Explanation { get; set; } = "";

    public List<ValidationIssue> Issues { get; } = new();

    public bool HasErrors => Issues.Any(i => i.IsError);
}
