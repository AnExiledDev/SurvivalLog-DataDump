using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

/// <summary>A decrypted copy is reused only while it still matches the game's bundle; a game update must rewrite it.</summary>
public class BundleFreshnessTests
{
    private static readonly DateTime Exported = new(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Updated = new(2026, 11, 5, 12, 15, 0, DateTimeKind.Utc);

    [Fact]
    public void A_copy_written_after_the_game_s_bundle_at_the_same_size_is_reused()
    {
        Assert.True(BundleFreshness.IsCurrent(new FileStamp(4490489, Updated.AddMinutes(30)), new FileStamp(4490489, Updated)));
    }

    // 1.1.20719 changed 21 bundles; a same-size change would otherwise keep the old content forever.
    [Fact]
    public void A_game_update_that_keeps_the_size_still_rewrites_the_copy()
    {
        Assert.False(BundleFreshness.IsCurrent(new FileStamp(4490489, Exported), new FileStamp(4490489, Updated)));
    }

    [Fact]
    public void A_copy_of_another_size_is_rewritten()
    {
        Assert.False(BundleFreshness.IsCurrent(new FileStamp(4473959, Updated.AddMinutes(30)), new FileStamp(4490489, Updated)));
    }

    [Fact]
    public void A_missing_copy_is_written()
    {
        Assert.False(BundleFreshness.IsCurrent(null, new FileStamp(4490489, Updated)));
    }
}
