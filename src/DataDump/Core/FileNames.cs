using System.Text;

namespace DataDump.Core;

public static class FileNames
{
    private const int MaxLength = 80;

    /// <summary>
    /// A game name (a table, a page key) as a file name: anything but ASCII letters, digits, dot, dash and underscore
    /// becomes an underscore, so no name can reach outside the dump folder.
    /// </summary>
    public static string Safe(string name)
    {
        var safe = new StringBuilder(name.Length);

        foreach (var c in name) safe.Append(IsKept(c, isFirst: safe.Length == 0) ? c : '_');

        if (safe.Length == 0) return "_";

        return safe.Length <= MaxLength ? safe.ToString() : safe.ToString(0, MaxLength);
    }

    /// <summary>A dot is kept only after the first character, so no name is "." or "..".</summary>
    private static bool IsKept(char c, bool isFirst) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '-' or '_' || (c == '.' && !isFirst);
}
