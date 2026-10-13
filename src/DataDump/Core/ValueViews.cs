using System;
using System.Collections.Generic;

namespace DataDump.Core;

/// <summary>
/// Port: tells the writer what kind of value it holds. The game's objects are read through an Il2Cpp adapter, plain
/// .NET objects through <see cref="PlainValueReader"/>, so the writer never touches a framework type.
/// </summary>
public interface IValueReader
{
    ValueView Describe(object? value);
}

/// <summary>What a value is, as far as JSON is concerned.</summary>
public abstract record ValueView;

/// <summary>Null, a bool, a string, a char or a number; anything else is written as its invariant text.</summary>
public sealed record LeafView(object? Value) : ValueView;

public sealed record EnumView(string Name) : ValueView;

/// <summary>A list, array or set. Enumerated lazily, so a failure part way is caught by the writer.</summary>
public sealed record ItemsView(IEnumerable<object?> Items) : ValueView;

/// <summary>A dictionary. Keys are written as text.</summary>
public sealed record PairsView(IEnumerable<KeyValuePair<object?, object?>> Pairs) : ValueView;

/// <summary>
/// An object with members. <paramref name="Id"/> is a leaf value when the object has an id, and a nested record with
/// an id is written as that id, so a row pointing at another row stays one line.
/// </summary>
public sealed record RecordView(string TypeName, object? Id, IReadOnlyList<MemberView> Members) : ValueView;

/// <summary>One member, read only when it is written; a getter that throws costs only its own value.</summary>
public sealed record MemberView(string Name, Func<object?> Read);
