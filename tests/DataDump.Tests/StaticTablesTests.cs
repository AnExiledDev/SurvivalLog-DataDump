using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class StaticTablesTests
{
    // Shaped like an interop class: each game field is a static property plus a NativeFieldInfoPtr_ pointer.
#pragma warning disable CS0169, CS0649, IDE0051
    private sealed class GameManager
    {
        private static readonly IntPtr NativeFieldInfoPtr_ShelfPins;
        private static readonly IntPtr NativeFieldInfoPtr_GapItemIds;
        private static readonly IntPtr NativeFieldInfoPtr_DayCount;

        public static int[] ShelfPins => new[] { 911 };

        public static Dictionary<int, int[]> GapItemIds => new();

        public static int DayCount => 3;

        // No pointer: a computed property, which would run game code.
        public static List<int> Computed => new();

        public int[] InstanceIds => Array.Empty<int>();
    }

    private sealed class Generic<T>
    {
        private static readonly IntPtr NativeFieldInfoPtr_Items;

        public static List<T> Items => new();
    }
#pragma warning restore CS0169, CS0649, IDE0051

    private static bool IsTable(Type type) => type != typeof(int);

    [Fact]
    public void Only_static_game_fields_of_table_types_are_read_in_declaration_order()
    {
        var names = StaticTables.Of(typeof(GameManager), IsTable).Select(p => p.Name);

        Assert.Equal(new[] { "ShelfPins", "GapItemIds" }, names);
    }

    // An open generic class has no single value to read.
    [Fact]
    public void An_open_generic_class_gives_nothing()
    {
        Assert.Empty(StaticTables.Of(typeof(Generic<>), IsTable));
    }

    [Fact]
    public void A_table_is_keyed_by_its_class_and_field_with_nested_classes_dotted()
    {
        var field = typeof(GameManager).GetProperty("ShelfPins")!;

        Assert.Equal("DataDump.Tests.StaticTablesTests.GameManager.ShelfPins", StaticTables.Key(typeof(GameManager), field));
    }
}
