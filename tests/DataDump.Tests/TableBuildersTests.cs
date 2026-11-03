using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class TableBuildersTests
{
    // Shaped like a game class with a lazy table: private static builders that fill a static cache on first call.
#pragma warning disable IDE0051
    private static class LazyTable
    {
        public static List<int>? Cache;
        public static int Calls;

        private static void EnsureBuilt()
        {
            Calls++;
            Cache = new List<int> { 7, 9 };
        }

        private static float GetPower(int id)
        {
            Cache ??= new List<int>();
            Cache.Add(id * 10);
            return 0f;
        }

        private static void Broken() => throw new InvalidOperationException("config not loaded");
    }
#pragma warning restore IDE0051

    private static string Name => typeof(LazyTable).FullName!.Replace('+', '.');

    [Fact]
    public void Run_calls_the_private_builder_so_the_table_is_filled_before_it_is_read()
    {
        LazyTable.Cache = null;

        var error = TableBuilders.Run(typeof(LazyTable), new[] { new TableBuilders.Builder(Name, "EnsureBuilt") });

        Assert.Null(error);
        Assert.Equal(new[] { 7, 9 }, LazyTable.Cache);
    }

    [Fact]
    public void Run_passes_the_id_argument_to_a_builder_that_takes_one()
    {
        LazyTable.Cache = null;

        var error = TableBuilders.Run(typeof(LazyTable), new[] { new TableBuilders.Builder(Name, "GetPower", 3) });

        Assert.Null(error);
        Assert.Equal(new[] { 30 }, LazyTable.Cache);
    }

    [Fact]
    public void Run_skips_builders_of_other_classes()
    {
        LazyTable.Calls = 0;

        var error = TableBuilders.Run(typeof(LazyTable), new[] { new TableBuilders.Builder("GameCore.HotUpdate.Other", "EnsureBuilt") });

        Assert.Null(error);
        Assert.Equal(0, LazyTable.Calls);
    }

    [Fact]
    public void Run_reports_the_game_error_without_the_reflection_wrapper()
    {
        var error = TableBuilders.Run(typeof(LazyTable), new[] { new TableBuilders.Builder(Name, "Broken") });

        Assert.Equal(ValueWriter.ErrorPrefix + "InvalidOperationException: config not loaded", error);
    }

    [Fact]
    public void Run_reports_a_builder_the_class_does_not_have_instead_of_dumping_a_silent_null()
    {
        var error = TableBuilders.Run(typeof(LazyTable), new[] { new TableBuilders.Builder(Name, "EnsureCache") });

        Assert.Equal(ValueWriter.ErrorPrefix + "no builder " + Name + ".EnsureCache", error);
    }

    [Fact]
    public void The_game_list_never_calls_a_method_that_changes_game_data_or_reads_the_loaded_map()
    {
        var methods = TableBuilders.Game.Select(b => b.Method).ToList();

        Assert.DoesNotContain("ApplyV1ClueLook", methods);
        Assert.DoesNotContain("CollectWarmableAreaIds", methods);
    }
}
