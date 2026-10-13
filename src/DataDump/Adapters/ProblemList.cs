using System;
using System.Collections.Generic;
using DataDump.Core;

namespace DataDump.Adapters;

/// <summary>
/// Reads the game one value at a time for a snapshot document. A read that throws gives null and a line naming what
/// could not be read, so one broken value never costs the rest of the document.
/// </summary>
internal sealed class ProblemList
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;

    public void Add(string line) => _lines.Add(line);

    public T? Object<T>(string what, Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Add(what + ": " + Reason(ex));

            return null;
        }
    }

    public T? Value<T>(string what, Func<T> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Add(what + ": " + Reason(ex));

            return null;
        }
    }

    public T? MaybeValue<T>(string what, Func<T?> read) where T : struct
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Add(what + ": " + Reason(ex));

            return null;
        }
    }

    /// <summary>The exception's type and message, without the error marker a dump value carries.</summary>
    public static string Reason(Exception ex) => ValueWriter.ErrorText(ex)[ValueWriter.ErrorPrefix.Length..];
}
