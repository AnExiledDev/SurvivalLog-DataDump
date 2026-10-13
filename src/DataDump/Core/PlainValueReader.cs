using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataDump.Core;

/// <summary>
/// Reads the mod's own report objects (records, dictionaries, lists) for <see cref="ValueWriter"/>. Members are
/// written in declaration order with camelCase names. Never gives an id, so a nested report object is always written
/// out in full.
/// </summary>
public sealed class PlainValueReader : IValueReader
{
    private readonly Dictionary<Type, PropertyInfo[]> _properties = new();

    public ValueView Describe(object? value) => value switch
    {
        null or string or bool or char => new LeafView(value),
        Enum named => new EnumView(named.ToString()),
        IFormattable => new LeafView(value),
        IDictionary map => new PairsView(Pairs(map)),
        IEnumerable items => new ItemsView(items.Cast<object?>()),
        _ => new RecordView(value.GetType().Name, null, Members(value)),
    };

    private static IEnumerable<KeyValuePair<object?, object?>> Pairs(IDictionary map)
    {
        foreach (DictionaryEntry entry in map) yield return new KeyValuePair<object?, object?>(entry.Key, entry.Value);
    }

    private IReadOnlyList<MemberView> Members(object value) =>
        PropertiesOf(value.GetType()).Select(property => new MemberView(CamelCase(property.Name), () => property.GetValue(value))).ToList();

    private PropertyInfo[] PropertiesOf(Type type)
    {
        if (_properties.TryGetValue(type, out var known)) return known;

        var found = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract")
            .OrderBy(property => property.MetadataToken)
            .ToArray();

        _properties[type] = found;

        return found;
    }

    private static string CamelCase(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
