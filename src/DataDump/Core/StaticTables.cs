using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataDump.Core;

/// <summary>
/// The game's tables written in code instead of config: static fields holding a list, array, set or dictionary, such as
/// StrangerTradeActiveManager.RobotModuleShelfPins. Interop gives each game field a static property of the same name and
/// a NativeFieldInfoPtr_ pointer named after it; a static property without that pointer runs game code when read, so
/// it is left out. Plain reflection, no game read.
/// </summary>
public static class StaticTables
{
    public const string FieldPointerPrefix = "NativeFieldInfoPtr_";

    /// <summary>A type's static game fields whose declared type <paramref name="isTable"/> accepts, in declaration order.</summary>
    public static IReadOnlyList<PropertyInfo> Of(Type type, Func<Type, bool> isTable)
    {
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters) return Array.Empty<PropertyInfo>();

        const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var pointers = new HashSet<string>(
            type.GetFields(statics).Select(field => field.Name).Where(name => name.StartsWith(FieldPointerPrefix, StringComparison.Ordinal)),
            StringComparer.Ordinal);

        return type.GetProperties(statics)
            .Where(property => property.GetIndexParameters().Length == 0 && property.GetMethod is not null)
            .Where(property => pointers.Contains(FieldPointerPrefix + property.Name))
            .Where(property => isTable(property.PropertyType))
            .OrderBy(property => property.MetadataToken)
            .ToList();
    }

    /// <summary>The key a table is written under: the game's own type name and field name.</summary>
    public static string Key(Type type, PropertyInfo field) => (type.FullName ?? type.Name).Replace('+', '.') + "." + field.Name;
}
