using System;

namespace DataDump.Core;

/// <summary>A file's size and when it was last written, in UTC.</summary>
public readonly record struct FileStamp(long Length, DateTime WrittenUtc);

/// <summary>
/// Whether a decrypted bundle already written still matches the game's bundle. A game update can change a bundle
/// without changing its size, so the size alone is not enough: a copy written before the game's file was is stale.
/// </summary>
public static class BundleFreshness
{
    /// <param name="copy">The decrypted copy in the dump, or null when there is none.</param>
    /// <param name="game">The game's encrypted bundle.</param>
    public static bool IsCurrent(FileStamp? copy, FileStamp game) =>
        copy is { } written && written.Length == game.Length && written.WrittenUtc >= game.WrittenUtc;
}
