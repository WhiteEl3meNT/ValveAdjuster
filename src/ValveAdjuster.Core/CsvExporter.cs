using System.Globalization;
using System.Text;

namespace ValveAdjuster.Core;

public static class CsvExporter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(string path, EngineConfig engine, IEnumerable<ValveResult> results)
    {
        var u = string.IsNullOrWhiteSpace(engine.Units) ? "" : $" ({engine.Units})";

        var headers = new[]
        {
            "Valve", "Type", "Cylinder", "Valve number",
            "Target clearance" + u, "Original clearance" + u, "Original shim" + u,
            "Calculated shim" + u, "Recommended shim" + u, "Difference (recommended - calculated)" + u,
            "Installed shim" + u, "Expected clearance" + u, "Final clearance" + u,
            "Status", "Notes"
        };

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(Escape)));

        foreach (var r in results)
        {
            var v = r.Valve;
            var fields = new[]
            {
                v.Label,
                v.Type.ToString(),
                v.Cylinder.ToString(Inv),
                v.Index.ToString(Inv),
                Fmt(r.TargetClearance),
                Fmt(v.InitialGap),
                Fmt(v.ExistingShim),
                Fmt(r.CalculatedShim),
                Fmt(r.RecommendedShim),
                Fmt(r.ShimDifference, signed: true),
                Fmt(v.InstalledShim),
                Fmt(r.ExpectedClearance),
                Fmt(v.FinalGap),
                r.Status.Display(),
                v.Notes
            };
            sb.AppendLine(string.Join(",", fields.Select(Escape)));
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Fmt(decimal? value, bool signed = false) =>
        value is decimal d ? d.ToString(signed ? "+0.000;-0.000;0.000" : "0.000", Inv) : "";

    private static string Escape(string s) =>
        s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
}
