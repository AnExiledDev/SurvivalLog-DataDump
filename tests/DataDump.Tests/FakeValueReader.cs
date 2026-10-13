using DataDump.Core;

namespace DataDump.Tests;

internal enum Weather
{
    Sunny,
    Rain,
}

/// <summary>A game object as the fake reader sees it: a type name, maybe an id, and members in order.</summary>
internal sealed class FakeObject
{
    public FakeObject(string typeName, object? id = null)
    {
        TypeName = typeName;
        Id = id;
    }

    public string TypeName { get; }

    public object? Id { get; }

    public List<MemberView> Members { get; } = new();

    public FakeObject With(string name, object? value) => WithGetter(name, () => value);

    public FakeObject WithGetter(string name, Func<object?> read)
    {
        Members.Add(new MemberView(name, read));

        return this;
    }
}

/// <summary>A dictionary in a fixed order.</summary>
internal sealed class FakePairs
{
    public FakePairs(params KeyValuePair<object?, object?>[] pairs) => Pairs = pairs;

    public IReadOnlyList<KeyValuePair<object?, object?>> Pairs { get; }
}

/// <summary>A value the reader cannot even describe, like a game object whose native side is gone.</summary>
internal sealed class Unreadable
{
}

/// <summary>Stands in for the Il2Cpp reader: describes the fakes above, lists, enums and leaves.</summary>
internal sealed class FakeValueReader : IValueReader
{
    public ValueView Describe(object? value) => value switch
    {
        Unreadable => throw new InvalidOperationException("unreadable"),
        FakeObject game => new RecordView(game.TypeName, game.Id, game.Members),
        FakePairs map => new PairsView(map.Pairs),
        Enum named => new EnumView(named.ToString()),
        string => new LeafView(value),
        IEnumerable<object?> items => new ItemsView(items),
        _ => new LeafView(value),
    };
}
