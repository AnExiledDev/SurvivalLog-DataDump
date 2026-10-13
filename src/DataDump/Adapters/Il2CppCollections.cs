using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataDump.Adapters;

/// <summary>
/// Walks the game's Il2CppSystem collections (List, Dictionary, HashSet, Queue, their key and value collections)
/// without knowing their element types: GetEnumerator, then MoveNext and Current on what it returns, the same calls a
/// typed foreach makes. A dictionary's enumerator gives KeyValuePair objects, read through Key and Value.
/// Main thread only, like every Il2Cpp read.
/// </summary>
internal static class Il2CppCollections
{
    private const string CollectionsNamespace = "Il2CppSystem.Collections";

    private static readonly Dictionary<Type, Walk?> Walks = new();
    private static readonly Dictionary<Type, (PropertyInfo Key, PropertyInfo Value)> PairMembers = new();

    /// <summary>How to walk one collection type.</summary>
    private sealed record Walk(MethodInfo GetEnumerator, MethodInfo MoveNext, PropertyInfo Current, bool IsPairs);

    public static bool IsCollection(Type type) => WalkOf(type) is not null;

    /// <summary>True when the collection's items are key-value pairs.</summary>
    public static bool IsDictionary(Type type) => WalkOf(type) is { IsPairs: true };

    /// <summary>The items, read lazily: each MoveNext is one step of the game's own enumerator.</summary>
    public static IEnumerable<object?> Items(object collection)
    {
        var walk = WalkOf(collection.GetType()) ?? throw new InvalidOperationException(collection.GetType().Name + " is not an Il2Cpp collection");
        var enumerator = walk.GetEnumerator.Invoke(collection, null) ?? throw new InvalidOperationException("GetEnumerator gave null");

        while (walk.MoveNext.Invoke(enumerator, null) is true) yield return walk.Current.GetValue(enumerator);
    }

    public static IEnumerable<KeyValuePair<object?, object?>> Pairs(object dictionary)
    {
        foreach (var pair in Items(dictionary))
        {
            if (pair is null) continue;

            var (key, value) = PairMembersOf(pair.GetType());

            yield return new KeyValuePair<object?, object?>(key.GetValue(pair), value.GetValue(pair));
        }
    }

    private static Walk? WalkOf(Type type)
    {
        if (Walks.TryGetValue(type, out var known)) return known;

        var walk = type.Namespace?.StartsWith(CollectionsNamespace, StringComparison.Ordinal) == true ? FindWalk(type) : null;

        Walks[type] = walk;

        return walk;
    }

    private static Walk? FindWalk(Type type)
    {
        var getEnumerator = ParameterlessMethods(type, "GetEnumerator").FirstOrDefault();
        var enumeratorType = getEnumerator?.ReturnType;
        var moveNext = enumeratorType is null ? null : ParameterlessMethods(enumeratorType, "MoveNext").FirstOrDefault(method => method.ReturnType == typeof(bool));
        var current = enumeratorType?.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(property => property.Name == "Current" && property.GetIndexParameters().Length == 0);

        if (getEnumerator is null || moveNext is null || current is null) return null;

        return new Walk(getEnumerator, moveNext, current, current.PropertyType.Name.StartsWith("KeyValuePair`2", StringComparison.Ordinal));
    }

    private static IEnumerable<MethodInfo> ParameterlessMethods(Type type, string name) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(method => method.Name == name && method.GetParameters().Length == 0);

    private static (PropertyInfo Key, PropertyInfo Value) PairMembersOf(Type pairType)
    {
        if (PairMembers.TryGetValue(pairType, out var known)) return known;

        var key = pairType.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance) ?? throw new MissingMemberException(pairType.Name, "Key");
        var value = pairType.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) ?? throw new MissingMemberException(pairType.Name, "Value");

        PairMembers[pairType] = (key, value);

        return (key, value);
    }
}
