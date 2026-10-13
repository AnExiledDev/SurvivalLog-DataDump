using System.Globalization;
using DataDump.Core;
using Xunit;

namespace DataDump.Tests;

public class SnapshotFoldersTests
{
    private static readonly DateTime Taken = new(2026, 10, 13, 1, 26, 5);

    [Fact]
    public void A_snapshot_folder_is_named_by_its_local_time()
    {
        Assert.Equal("20261013-012605", SnapshotFolders.NameFor(Taken, _ => false));
    }

    // Two presses in one second must not write into the same folder.
    [Fact]
    public void A_second_snapshot_in_the_same_second_gets_a_number()
    {
        var taken = new HashSet<string> { "20261013-012605", "20261013-012605-2" };

        Assert.Equal("20261013-012605-3", SnapshotFolders.NameFor(Taken, taken.Contains));
    }

    [Fact]
    public void The_oldest_snapshots_past_the_cap_are_deleted_in_time_then_number_order()
    {
        var names = new[] { "20261013-012605-2", "20261013-012605", "20261013-012605-10", "20261012-235959" };

        Assert.Equal(new[] { "20261012-235959", "20261013-012605", "20261013-012605-2" }, SnapshotFolders.ToDelete(names, keep: 1));
    }

    // The folder is the user's too; anything not named like a snapshot is never deleted.
    [Fact]
    public void Folders_not_named_like_a_snapshot_are_never_deleted()
    {
        var names = new[] { "notes", "20261013-01260", "20261013-012605-x", "20261013-012605" };

        Assert.Empty(SnapshotFolders.ToDelete(names, keep: 1));
    }

    [Fact]
    public void Fifty_snapshots_are_kept()
    {
        var names = Enumerable.Range(0, 52).Select(second => Taken.AddSeconds(second).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)).ToList();

        Assert.Equal(names.Take(2), SnapshotFolders.ToDelete(names));
    }
}
