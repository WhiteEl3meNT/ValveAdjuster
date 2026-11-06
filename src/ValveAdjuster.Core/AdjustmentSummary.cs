namespace ValveAdjuster.Core;

public sealed record AdjustmentSummary(
    int Total,
    int WithinSpec,
    int NeedsAdjustment,
    int Completed,
    int PendingVerification,
    int FailedVerification,
    int NotMeasured)
{
    public static AdjustmentSummary From(IEnumerable<ValveResult> results)
    {
        var list = results.ToList();
        int Count(params ValveStatus[] statuses) => list.Count(r => statuses.Contains(r.Status));

        return new AdjustmentSummary(
            Total: list.Count,
            WithinSpec: Count(ValveStatus.InSpec, ValveStatus.PassVerified),
            NeedsAdjustment: Count(ValveStatus.NeedsAdjustment),
            Completed: Count(ValveStatus.AwaitingVerification, ValveStatus.PassVerified, ValveStatus.FailVerified),
            PendingVerification: Count(ValveStatus.AwaitingVerification),
            FailedVerification: Count(ValveStatus.FailVerified),
            NotMeasured: Count(ValveStatus.NotMeasured));
    }
}
