using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DataDump.Core;

/// <summary>
/// Code tables the game builds only the first time something asks for them, so at the main menu they are still null.
/// Each entry names the static method that builds the table from config. The dump calls it before reading the class,
/// which is exactly what the game does on first use. Only methods that read config and fill their own cache belong
/// here: never one that changes game data (TruthRecorderManager.ApplyV1ClueLook swaps item icons) or depends on the
/// loaded map (EnvAreaHelper.CollectWarmableAreaIds reads the current map's boxes, which the map files already hold).
/// </summary>
public static class TableBuilders
{
    /// <summary>A static builder: the class's full name, the method, and the arguments to call it with.</summary>
    public sealed record Builder(string ClassName, string Method, params object[] Args);

    // A plant or device id only picks which entry is returned; the first call with any id above zero parses the whole
    // table from its global setting.
    public static readonly IReadOnlyList<Builder> Game = new Builder[]
    {
        new("GameCore.HotUpdate.Battle.Logic.DynamicEventManager", "GetTaskScopedTagOwners"),
        new("GameCore.HotUpdate.Battle.Logic.EnvAreaHelper", "GetParamIndex"),
        new("GameCore.HotUpdate.Battle.Logic.EnvAreaHelper", "GetAcConfigIds"),
        new("GameCore.HotUpdate.Battle.Logic.GreenhouseResearchPlant", "GetDailyFertPower", 1),
        new("GameCore.HotUpdate.Battle.Logic.GreenhouseResearchPlant", "GetRegrowInterval", 1),
        new("GameCore.HotUpdate.Battle.Logic.TowerDeviceComponent", "GetDisableThreshold", 1),
        new("GameCore.HotUpdate.Battle.Logic.TowerDeviceComponent", "GetRepairCostRatio", 1),
        new("GameCore.HotUpdate.Codex.DishCodexGrouping", "EnsureBuilt"),
        new("GameCore.HotUpdate.CookingProductIndex", "EnsureBuilt"),
        new("GameCore.HotUpdate.FurnitureTagRules", "EnsureCache"),
    };

    /// <summary>
    /// Calls every builder in <paramref name="builders"/> for <paramref name="type"/>, in order. Null when all ran (or
    /// there are none), else the first error, after which the rest are not called.
    /// </summary>
    public static string? Run(Type type, IReadOnlyList<Builder> builders)
    {
        var name = (type.FullName ?? type.Name).Replace('+', '.');

        foreach (var builder in builders.Where(b => b.ClassName == name))
        {
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var argTypes = builder.Args.Select(a => a.GetType()).ToArray();
            var method = type.GetMethod(builder.Method, statics, null, argTypes, null);

            if (method is null) return ValueWriter.ErrorPrefix + "no builder " + name + "." + builder.Method;

            try
            {
                method.Invoke(null, builder.Args);
            }
            catch (Exception ex)
            {
                return ValueWriter.ErrorText(ex);
            }
        }

        return null;
    }
}
