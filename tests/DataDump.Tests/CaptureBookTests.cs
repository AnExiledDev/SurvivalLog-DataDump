using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class CaptureBookTests
{
    private static readonly DateTime Asked = new(2026, 10, 13, 1, 0, 0);
    private static readonly DateTime Deadline = Asked.AddSeconds(10);
    private static readonly CaptureResult Saved = new(CaptureState.Saved, "page-root.html");

    [Fact]
    public void The_book_is_due_as_soon_as_every_page_has_answered()
    {
        var book = new CaptureBook(new[] { "root", "bag" }, Deadline);

        book.Answer("root", Saved);

        Assert.False(book.IsDue(Asked.AddSeconds(1)));

        book.Answer("bag", Saved);

        Assert.True(book.IsDue(Asked.AddSeconds(1)));
    }

    // A page whose web view never answers must not hold the snapshot open forever.
    [Fact]
    public void Pages_still_waiting_at_the_deadline_are_marked_timed_out()
    {
        var book = new CaptureBook(new[] { "root", "bag" }, Deadline);

        book.Answer("root", Saved);

        Assert.True(book.IsDue(Deadline));

        var results = book.Close();

        Assert.Equal(CaptureState.Saved, results["root"].State);
        Assert.Equal(CaptureState.TimedOut, results["bag"].State);
        Assert.Equal(new[] { "bag", "root" }, results.Keys);
    }

    [Fact]
    public void An_answer_after_closing_is_refused()
    {
        var book = new CaptureBook(new[] { "root" }, Deadline);

        book.Close();

        Assert.False(book.Answer("root", Saved));
        Assert.False(book.IsDue(Deadline));
    }

    [Fact]
    public void Only_the_first_answer_for_an_asked_page_counts()
    {
        var book = new CaptureBook(new[] { "root" }, Deadline);

        Assert.False(book.Answer("never-asked", Saved));
        Assert.True(book.Answer("root", Saved));
        Assert.False(book.Answer("root", new CaptureResult(CaptureState.Failed, "late")));
        Assert.Equal(CaptureState.Saved, book.Close()["root"].State);
    }
}
