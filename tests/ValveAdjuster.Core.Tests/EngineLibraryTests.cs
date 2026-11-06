using ValveAdjuster.Core;
using Xunit;

namespace ValveAdjuster.Core.Tests;

public class EngineLibraryTests
{
    private static EngineConfig Engine(string name, int cylinders = 4) => new()
    {
        Name = name,
        Units = "mm",
        Cylinders = cylinders,
        IntakeValvesPerCylinder = 2,
        ExhaustValvesPerCylinder = 2,
        Intake = new ValveTypeSpec { ClearanceMin = 0.10m, ClearanceMax = 0.15m, ShimMin = 2.000m, ShimMax = 3.500m, ShimIncrement = 0.025m },
        Exhaust = new ValveTypeSpec { ClearanceMin = 0.20m, ClearanceMax = 0.25m, ShimMin = 2.000m, ShimMax = 3.500m, ShimIncrement = 0.025m }
    };

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"valveadj-types-{Guid.NewGuid():N}.json");

    [Fact]
    public void Upsert_replaces_existing_name_ignoring_case()
    {
        var library = new EngineLibrary(TempPath());
        library.Upsert("ZX6R", Engine("ZX6R", cylinders: 4));
        library.Upsert("zx6r", Engine("zx6r", cylinders: 2));

        Assert.Single(library.Profiles);
        Assert.Equal(2, library.Find("ZX6R")!.Config.Cylinders);
    }

    [Fact]
    public void Upsert_stores_a_copy_of_the_config()
    {
        var library = new EngineLibrary(TempPath());
        var config = Engine("Copy test");
        library.Upsert("Copy test", config);

        config.Intake.ClearanceMin = 9m;
        config.Cylinders = 16;

        var stored = library.Find("Copy test")!.Config;
        Assert.Equal(0.10m, stored.Intake.ClearanceMin);
        Assert.Equal(4, stored.Cylinders);
    }

    [Fact]
    public void Save_and_load_round_trips_every_spec()
    {
        var path = TempPath();
        try
        {
            var library = new EngineLibrary(path);
            library.Upsert("Engine A", Engine("Engine A"));
            library.Upsert("Engine B", Engine("Engine B", cylinders: 1));
            library.Save();

            var loaded = EngineLibrary.Load(path);
            Assert.Equal(2, loaded.Profiles.Count);

            var a = loaded.Find("Engine A")!.Config;
            Assert.Equal(4, a.Cylinders);
            Assert.Equal(0.025m, a.Exhaust.ShimIncrement);
            Assert.Equal(0.25m, a.Exhaust.ClearanceMax);
            Assert.Equal("mm", a.Units);
            Assert.Equal(1, loaded.Find("Engine B")!.Config.Cylinders);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_of_missing_file_returns_empty_library()
    {
        var library = EngineLibrary.Load(TempPath());
        Assert.True(library.IsEmpty);
    }

    [Fact]
    public void Remove_reports_whether_a_type_was_deleted()
    {
        var library = new EngineLibrary(TempPath());
        library.Upsert("Only one", Engine("Only one"));

        Assert.True(library.Remove("only ONE"));
        Assert.False(library.Remove("only ONE"));
        Assert.True(library.IsEmpty);
    }

    [Fact]
    public void Blank_name_is_rejected()
    {
        var library = new EngineLibrary(TempPath());
        Assert.Throws<ArgumentException>(() => library.Upsert("   ", Engine("x")));
    }

    [Fact]
    public void Profiles_are_listed_alphabetically()
    {
        var library = new EngineLibrary(TempPath());
        library.Upsert("Zed", Engine("Zed"));
        library.Upsert("alpha", Engine("alpha"));
        Assert.Equal(new[] { "alpha", "Zed" }, library.Profiles.Select(p => p.Name).ToArray());
    }
}
