using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class LastResultTests
{
    private sealed class Counted
    {
        public Counted() => Last = new LastResult<int, string>(Work);

        public LastResult<int, string> Last { get; }

        public int Runs { get; private set; }

        private string Work(int key)
        {
            Runs++;

            return "view " + key;
        }
    }

    // The map view is worked out from the camera every second; while the camera stays put, nothing is worked out.
    [Fact]
    public void The_same_key_gives_the_same_answer_without_working_it_out_again()
    {
        var counted = new Counted();
        var first = counted.Last.Get(1);

        for (var i = 0; i < 10; i++) Assert.Same(first, counted.Last.Get(1));

        Assert.Equal(1, counted.Runs);
    }

    // A moved camera or a new floor must be seen: a changed key works the answer out again.
    [Fact]
    public void A_changed_key_works_the_answer_out_again()
    {
        var counted = new Counted();

        counted.Last.Get(1);

        Assert.Equal("view 2", counted.Last.Get(2));
        Assert.Equal("view 1", counted.Last.Get(1));
        Assert.Equal(3, counted.Runs);
    }

    // A new location drops the old answer, so nothing from the last location is kept or given back.
    [Fact]
    public void Clear_forgets_the_answer()
    {
        var counted = new Counted();

        counted.Last.Get(1);
        counted.Last.Clear();
        counted.Last.Get(1);

        Assert.Equal(2, counted.Runs);
    }

    // The first key may equal the default the cache starts with; it must still be worked out.
    [Fact]
    public void The_first_key_is_worked_out_even_when_it_is_the_default()
    {
        var counted = new Counted();

        Assert.Equal("view 0", counted.Last.Get(0));
        Assert.Equal(1, counted.Runs);
    }
}
