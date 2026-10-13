using System;
using System.Collections.Generic;

namespace DataDump.Core;

/// <summary>
/// One table row as one JSON line, with the row's id first so <c>grep '"ID":123,'</c> finds it. A row with no id
/// member gets an <c>id</c> from its table key. The game's <c>MainKey</c> repeats the table key under another name,
/// so it is left out.
/// </summary>
public sealed class RowWriter
{
    public const int MaxDepth = 4;

    private const string TableKeyMember = "MainKey";
    private static readonly string[] IdNames = { "ID", "Id", "id" };

    private readonly IValueReader _reader;
    private readonly ValueWriter _values;

    public RowWriter(IValueReader reader)
    {
        _reader = reader;
        _values = new ValueWriter(reader, MaxDepth);
    }

    public string Write(object key, object? row)
    {
        var members = new List<MemberView>();

        try
        {
            if (_reader.Describe(row) is RecordView record) AddInRowOrder(members, record, key);
            else AddPlainValue(members, key, row);
        }
        catch (Exception ex)
        {
            members.Clear();
            members.Add(new MemberView("id", () => key));
            members.Add(new MemberView("error", () => ValueWriter.ErrorText(ex)));
        }

        return _values.WriteView(new RecordView("row", null, members));
    }

    private static void AddInRowOrder(List<MemberView> members, RecordView record, object key)
    {
        var id = FindId(record);

        members.Add(id ?? new MemberView("id", () => key));

        foreach (var member in record.Members)
        {
            if (!ReferenceEquals(member, id) && member.Name != TableKeyMember) members.Add(member);
        }
    }

    /// <summary>A table whose rows are plain values (a number, a text) still gets one object per line.</summary>
    private static void AddPlainValue(List<MemberView> members, object key, object? row)
    {
        members.Add(new MemberView("id", () => key));
        members.Add(new MemberView("value", () => row));
    }

    private static MemberView? FindId(RecordView record)
    {
        foreach (var name in IdNames)
        {
            foreach (var member in record.Members)
            {
                if (member.Name == name) return member;
            }
        }

        return null;
    }
}
