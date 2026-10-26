using System.Text;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

/// <summary>
/// The map and snapshot files are written a part per frame. The wiki reads them, so the text a sliced write finishes
/// with must be exactly what the one-shot write gives, for every shape a value can take.
/// </summary>
public class ValueWriterStepsTests
{
    private static readonly Dictionary<string, Func<object?>> Values = new()
    {
        ["leaves"] = () => new List<object?> { null, "x", true, 'c', 3, 7L, 2.5, 0.1f, 1.5m, Weather.Rain },
        ["record"] = () => Location(),
        ["record with a getter that throws"] = () => new FakeObject("Config_Buff")
            .With("A", 1)
            .WithGetter("B", () => throw new InvalidOperationException("gone"))
            .With("C", new List<object?> { 3, 4 }),
        ["list with an unreadable value"] = () => new List<object?> { 1, new Unreadable(), new List<object?> { 2, 3 } },
        ["list that breaks part way"] = () => new List<object?> { new List<object?> { 0 }, BreaksAfterTwo() },
        ["pairs"] = () => new FakePairs(
            new KeyValuePair<object?, object?>(Weather.Sunny, new List<object?> { 1, 2 }),
            new KeyValuePair<object?, object?>(5, new FakeObject("Spot", 5).With("Name", "Well")),
            new KeyValuePair<object?, object?>("deep", new FakePairs(new KeyValuePair<object?, object?>("x", new List<object?> { new List<object?> { 1 } })))),
        ["object that contains itself"] = () => Loop(),
        ["empty containers"] = () => new FakeObject("Empty").With("list", new List<object?>()).With("map", new FakePairs()),
        ["unreadable at the top"] = () => new Unreadable(),
        ["leaf at the top"] = () => "just text",
    };

    public static TheoryData<string, bool, bool, int> Cases() => CasesOf(Values.Keys);

    /// <summary>A view is what the reader made of a value, so a value it cannot describe has no view.</summary>
    public static TheoryData<string, bool, bool, int> ViewCases() => CasesOf(Values.Keys.Where(name => name != "unreadable at the top"));

    private static TheoryData<string, bool, bool, int> CasesOf(IEnumerable<string> names)
    {
        var cases = new TheoryData<string, bool, bool, int>();

        foreach (var name in names)
        {
            foreach (var isIndented in new[] { false, true })
            {
                foreach (var isNestedIdOnly in new[] { true, false })
                {
                    foreach (var sliceDepth in new[] { 0, 1, 2, 3, 10 }) cases.Add(name, isIndented, isNestedIdOnly, sliceDepth);
                }
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_sliced_write_finishes_with_the_one_shot_text(string name, bool isIndented, bool isNestedIdOnly, int sliceDepth)
    {
        var writer = new ValueWriter(new FakeValueReader(), 4, isIndented, isNestedIdOnly);
        var expected = writer.Write(Values[name]());
        var text = new StringBuilder();

        foreach (var _ in writer.WriteSteps(text, Values[name](), sliceDepth))
        {
        }

        Assert.Equal(expected, text.ToString());
    }

    [Theory]
    [MemberData(nameof(ViewCases))]
    public void A_sliced_view_write_finishes_with_the_one_shot_view_text(string name, bool isIndented, bool isNestedIdOnly, int sliceDepth)
    {
        var writer = new ValueWriter(new FakeValueReader(), 4, isIndented, isNestedIdOnly);
        var expected = writer.WriteView(new FakeValueReader().Describe(Values[name]()));
        var text = new StringBuilder();

        foreach (var _ in writer.WriteViewSteps(text, new FakeValueReader().Describe(Values[name]()), sliceDepth))
        {
        }

        Assert.Equal(expected, text.ToString());
    }

    // Slicing is the point: a large location must spread over many frames, not run as one step.
    [Fact]
    public void A_record_is_written_in_more_than_one_step()
    {
        var writer = new ValueWriter(new FakeValueReader(), 4, isNestedIdOnly: false);
        var steps = writer.WriteViewSteps(new StringBuilder(), new FakeValueReader().Describe(Location()), 4).Count();

        Assert.True(steps > 3, $"only {steps} steps");
    }

    // A step only ends between parts; until the last step has run the text is not yet the whole value.
    [Fact]
    public void The_text_is_whole_only_after_the_last_step()
    {
        var writer = new ValueWriter(new FakeValueReader(), 4);
        var expected = writer.Write(Location());
        var text = new StringBuilder();
        using var steps = writer.WriteSteps(text, Location(), 2).GetEnumerator();

        Assert.True(steps.MoveNext());
        Assert.NotEqual(expected, text.ToString());

        while (steps.MoveNext())
        {
        }

        Assert.Equal(expected, text.ToString());
    }

    private static FakeObject Location() => new FakeObject("Config_Map", 7)
        .With("Name", "Old Mill")
        .With("Spots", new List<object?>
        {
            new FakeObject("Config_MapPoint", 1).With("x", 1).With("y", 2),
            new FakeObject("Config_MapPoint", 2).With("x", 3).With("y", 4),
            new FakeObject("Vector3").With("x", 5).With("tags", new List<object?> { "a", "b" }),
        })
        .With("Weather", Weather.Rain)
        .With("Areas", new FakePairs(new KeyValuePair<object?, object?>(1, new FakeObject("Area", 1).With("Size", 9))));

    private static FakeObject Loop()
    {
        var loop = new FakeObject("Loop");

        loop.WithGetter("Self", () => loop).With("n", 1);

        return loop;
    }

    private static IEnumerable<object?> BreaksAfterTwo()
    {
        yield return 1;
        yield return 2;

        throw new InvalidOperationException("broken");
    }
}
