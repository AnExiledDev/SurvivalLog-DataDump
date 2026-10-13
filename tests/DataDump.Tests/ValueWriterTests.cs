using System.Reflection;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class ValueWriterTests
{
    private static string Write(object? value, int maxDepth = 4) => new ValueWriter(new FakeValueReader(), maxDepth).Write(value);

    [Fact]
    public void Leaves_are_written_as_json_values()
    {
        var leaves = new List<object?> { null, "x", true, 'c', 3, 7L, 2.5, 0.1f, 1.5m };

        Assert.Equal("[null,\"x\",true,\"c\",3,7,2.5,0.1,1.5]", Write(leaves));
    }

    [Fact]
    public void Enums_are_written_as_their_names()
    {
        Assert.Equal("[\"Rain\",\"Sunny\"]", Write(new List<object?> { Weather.Rain, Weather.Sunny }));
    }

    // A row pointing at another config row stays one short line: the other row is written as its id.
    [Fact]
    public void A_nested_object_with_an_id_is_written_as_that_id()
    {
        var item = new FakeObject("Config_Item", 42).With("Name", "Apple");
        var row = new FakeObject("Config_Recipe").With("Output", item);

        Assert.Equal("{\"Output\":42}", Write(row));
    }

    [Fact]
    public void A_nested_object_without_an_id_is_written_out()
    {
        var position = new FakeObject("Vector3").With("x", 1).With("y", 2);
        var row = new FakeObject("Agent").With("Position", position);

        Assert.Equal("{\"Position\":{\"x\":1,\"y\":2}}", Write(row));
    }

    [Fact]
    public void Lists_past_the_depth_limit_are_marked_not_followed()
    {
        var nested = new List<object?> { new List<object?> { new List<object?> { 1 } } };

        Assert.Equal("[[\"(depth limit)\"]]", Write(nested, maxDepth: 2));
    }

    // Game objects point back at their owners; without the limit one row would never finish writing.
    [Fact]
    public void An_object_that_contains_itself_stops_at_the_depth_limit()
    {
        var loop = new FakeObject("Loop");

        loop.WithGetter("Self", () => loop);

        Assert.Equal("{\"Self\":{\"Self\":{\"Self\":\"Loop\"}}}", Write(loop, maxDepth: 3));
    }

    [Fact]
    public void A_getter_that_throws_costs_only_its_own_value()
    {
        var row = new FakeObject("Config_Buff")
            .With("A", 1)
            .WithGetter("B", () => throw new InvalidOperationException("gone"))
            .With("C", 3);

        Assert.Equal("{\"A\":1,\"B\":\"!error InvalidOperationException: gone\",\"C\":3}", Write(row));
    }

    [Fact]
    public void A_value_that_cannot_be_described_is_written_as_an_error()
    {
        Assert.Equal("[1,\"!error InvalidOperationException: unreadable\",2]", Write(new List<object?> { 1, new Unreadable(), 2 }));
    }

    [Fact]
    public void A_list_that_breaks_part_way_keeps_what_it_gave()
    {
        Assert.Equal("[1,2,\"!error InvalidOperationException: broken\"]", Write(BreaksAfterTwo()));
    }

    [Fact]
    public void Dictionary_keys_are_written_as_text()
    {
        var map = new FakePairs(
            new KeyValuePair<object?, object?>(Weather.Rain, 1),
            new KeyValuePair<object?, object?>(5, "five"),
            new KeyValuePair<object?, object?>(new FakeObject("Config_Item", 9), true));

        Assert.Equal("{\"Rain\":1,\"5\":\"five\",\"9\":true}", Write(map));
    }

    [Fact]
    public void Reflection_wrappers_are_taken_off_an_error()
    {
        var wrapped = new TargetInvocationException(new ArgumentException("bad"));

        Assert.Equal("!error ArgumentException: bad", ValueWriter.ErrorText(wrapped));
    }

    [Fact]
    public void Indented_output_puts_one_member_on_each_line()
    {
        var row = new FakeObject("Report")
            .With("a", 1)
            .With("list", new List<object?>())
            .With("b", new FakeObject("Inner").With("x", 2));
        var expected = string.Join("\n",
            "{",
            "  \"a\": 1,",
            "  \"list\": [],",
            "  \"b\": {",
            "    \"x\": 2",
            "  }",
            "}");

        Assert.Equal(expected, new ValueWriter(new FakeValueReader(), 4, isIndented: true).Write(row));
    }

    private static IEnumerable<object?> BreaksAfterTwo()
    {
        yield return 1;
        yield return 2;

        throw new InvalidOperationException("broken");
    }
}
