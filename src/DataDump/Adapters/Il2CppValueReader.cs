using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DataDump.Core;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DataDump.Adapters;

/// <summary>
/// Reads the game's objects for <see cref="ValueWriter"/>. A game object's members are the game's fields and
/// auto-properties as the interop assembly exposes them, base class first; a property with no backing field runs game
/// code when read, so it is left out. An object is read as its real class, not the type the member that holds it
/// declares, so a state held as its base class still shows its own members. Main thread only, like every Il2Cpp read.
/// </summary>
internal sealed class Il2CppValueReader : IValueReader
{
    private const string FieldPointerPrefix = "NativeFieldInfoPtr_";
    private const string BackingFieldSuffix = "_k__BackingField";
    private static readonly string[] IdNames = { "ID", "Id", "InstanceId" };

    private readonly Dictionary<Type, PropertyInfo[]> _members = new();
    private readonly Dictionary<Type, FieldInfo[]> _structFields = new();
    private readonly Dictionary<IntPtr, Func<IntPtr, object>?> _narrowers = new();
    private readonly Dictionary<PropertyInfo, Func<object, object?>> _readers = new();

    public ValueView Describe(object? value)
    {
        switch (value)
        {
            case null or string or bool or char or decimal:
                return new LeafView(value);
            case Enum named:
                return new EnumView(named.ToString());
            case IntPtr or UIntPtr or Delegate or Il2CppSystem.Delegate:
                return new LeafView(value.GetType().Name);
            case Il2CppSystem.Type gameType:
                return new LeafView(gameType.FullName);
            case UnityEngine.Object unity:
                return new LeafView(unity.GetType().Name + ":" + unity.name);
        }

        var type = value.GetType();

        if (type.IsPrimitive) return new LeafView(value);
        if (type == typeof(Il2CppSystem.Object)) return new LeafView(value.ToString());
        if (value is IDictionary map) return new PairsView(Pairs(map));
        if (value is IEnumerable items) return new ItemsView(items.Cast<object?>());
        if (Il2CppCollections.IsDictionary(type)) return new PairsView(Il2CppCollections.Pairs(value));
        if (Il2CppCollections.IsCollection(type)) return new ItemsView(Il2CppCollections.Items(value));
        if (value is Il2CppObjectBase game) return Record(Narrow(game));
        if (type.IsValueType) return Struct(value);

        return new LeafView(value.ToString());
    }

    /// <summary>The object as its real class: interop hands out the declared type, which hides a subclass's members.</summary>
    public object Narrow(Il2CppObjectBase value)
    {
        var classPointer = IL2CPP.il2cpp_object_get_class(value.Pointer);

        if (!_narrowers.TryGetValue(classPointer, out var narrower))
        {
            narrower = FindNarrower(value);
            _narrowers[classPointer] = narrower;
        }

        if (narrower is null) return value;

        var narrowed = narrower(value.Pointer);

        return value.GetType().IsInstanceOfType(narrowed) ? narrowed : value;
    }

    private RecordView Record(object value)
    {
        var properties = MembersOf(value.GetType());
        var members = properties.Select(property => new MemberView(property.Name, () => Read(property, value))).ToList();

        return new RecordView(value.GetType().Name, IdOf(value, properties), members);
    }

    private object? IdOf(object value, PropertyInfo[] properties)
    {
        foreach (var name in IdNames)
        {
            var property = properties.FirstOrDefault(member => member.Name == name);

            if (property is null) continue;

            var id = Read(property, value);

            if (id is not null && Describe(id) is LeafView) return id;
        }

        return null;
    }

    private object? Read(PropertyInfo property, object value)
    {
        if (!_readers.TryGetValue(property, out var read))
        {
            read = ReaderFor(property);
            _readers[property] = read;
        }

        return read(value);
    }

    /// <summary>
    /// How one member is read. Interop's getter for a value-type field of a generic class calls
    /// IL2CPP.PointerToValueGeneric with valueTypeWouldBeBoxed false, which boxes the field and then boxes that box
    /// again, so it returns the bits of the box's class pointer: every int of a ValueTuple read 1076244624. Those fields
    /// are read with the same interop call told the value is boxed once, from the field's own address.
    /// </summary>
    private static Func<object, object?> ReaderFor(PropertyInfo property)
    {
        var declaring = property.DeclaringType;

        if (declaring is null || !declaring.IsGenericType) return property.GetValue;

        var fieldClass = Il2CppClassPointerStore.GetNativeClassPointer(property.PropertyType);

        if (fieldClass == IntPtr.Zero)
        {
            return property.PropertyType.IsValueType ? _ => throw new InvalidOperationException($"no game class for {property.PropertyType.Name}") : property.GetValue;
        }

        if (!IL2CPP.il2cpp_class_is_valuetype(fieldClass)) return property.GetValue;

        RuntimeHelpers.RunClassConstructor(declaring.TypeHandle);

        var field = FieldPointer(declaring, property.Name);

        if (field == IntPtr.Zero) return _ => throw new InvalidOperationException($"no field pointer for {declaring.Name}.{property.Name}");

        var offset = (int)IL2CPP.il2cpp_field_get_offset(field);
        var read = typeof(IL2CPP).GetMethod(nameof(IL2CPP.PointerToValueGeneric))!.MakeGenericMethod(property.PropertyType);

        return value => read.Invoke(null, new object[] { ((Il2CppObjectBase)value).Pointer + offset, true, true });
    }

    private static IntPtr FieldPointer(Type declaring, string name)
    {
        const BindingFlags statics = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var pointer = declaring.GetField(FieldPointerPrefix + name, statics) ?? declaring.GetField(FieldPointerPrefix + "_" + name + BackingFieldSuffix, statics);

        return pointer?.GetValue(null) is IntPtr found ? found : IntPtr.Zero;
    }

    private RecordView Struct(object value)
    {
        var type = value.GetType();

        if (!_structFields.TryGetValue(type, out var fields))
        {
            fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            _structFields[type] = fields;
        }

        return new RecordView(type.Name, null, fields.Select(field => new MemberView(field.Name, () => field.GetValue(value))).ToList());
    }

    /// <summary>
    /// The game's fields and auto-properties, base class first, each name once. Interop gives every game field a
    /// NativeFieldInfoPtr_ pointer named after it, and an auto-property's backing field one named _Name_k__BackingField,
    /// so those pointers say which properties only read memory.
    /// </summary>
    private PropertyInfo[] MembersOf(Type type)
    {
        if (_members.TryGetValue(type, out var known)) return known;

        var levels = new List<Type>();

        for (var level = type; level is not null && level != typeof(Il2CppSystem.Object) && level != typeof(Il2CppObjectBase); level = level.BaseType) levels.Add(level);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new List<PropertyInfo>();

        foreach (var level in Enumerable.Reverse(levels))
        {
            foreach (var property in DeclaredReadable(level))
            {
                if (seen.Add(property.Name)) members.Add(property);
            }
        }

        var found = members.ToArray();

        _members[type] = found;

        return found;
    }

    private static IEnumerable<PropertyInfo> DeclaredReadable(Type level)
    {
        var pointers = new HashSet<string>(
            level.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(field => field.Name)
                .Where(name => name.StartsWith(FieldPointerPrefix, StringComparison.Ordinal)),
            StringComparer.Ordinal);

        return level.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
            .Where(property => !property.Name.EndsWith(BackingFieldSuffix, StringComparison.Ordinal))
            .Where(property => pointers.Contains(FieldPointerPrefix + property.Name) ||
                               pointers.Contains(FieldPointerPrefix + "_" + property.Name + BackingFieldSuffix))
            .OrderBy(property => property.MetadataToken);
    }

    private static IEnumerable<KeyValuePair<object?, object?>> Pairs(IDictionary map)
    {
        foreach (DictionaryEntry entry in map) yield return new KeyValuePair<object?, object?>(entry.Key, entry.Value);
    }

    /// <summary>
    /// The interop class of the object's real game class, built from its pointer; null when it is the declared type
    /// already, generic, or has no interop class. Interop keeps game namespaces and puts Il2Cpp in front of System ones.
    /// </summary>
    private static Func<IntPtr, object>? FindNarrower(Il2CppObjectBase value)
    {
        if (value is not Il2CppSystem.Object boxed) return null;

        var name = boxed.GetIl2CppType()?.FullName;

        if (string.IsNullOrEmpty(name) || name.Contains('[') || name.Contains('`')) return null;

        var managed = FindType(name) ?? FindType("Il2Cpp" + name);

        if (managed is null || managed == value.GetType() || !value.GetType().IsAssignableFrom(managed)) return null;

        var constructor = managed.GetConstructor(new[] { typeof(IntPtr) });

        return constructor is null ? null : pointer => constructor.Invoke(new object[] { pointer });
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var found = assembly.GetType(fullName, throwOnError: false);

            if (found is not null) return found;
        }

        return null;
    }
}
