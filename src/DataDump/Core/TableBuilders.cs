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
    /// <summary>
    /// A static builder: the class's full name, the method, and the arguments to call it with. With <see cref="Into"/>
    /// set, the method only returns the table and the game stores it itself, so the dump stores the result in that
    /// static property exactly as the game's caller does.
    /// </summary>
    public sealed record Builder(string ClassName, string Method, params object[] Args)
    {
        public string? Into { get; init; }
    }

    /// <summary>A string argument read from the game's global setting <paramref name="Key"/> (its Params3), as the game's caller reads it.</summary>
    public sealed record Setting(string Key);

    /// <summary>An out argument of <paramref name="Type"/>: the builder is matched on it by reference and its result is dropped.</summary>
    public sealed record Out(Type Type);

    // An id argument only picks which entry is returned; the first call with any id above zero builds the whole table
    // from config. Each of these was read in the decomp for that: it fills only its own cache and reads only config.
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
        new("GameCore.HotUpdate.Battle.Logic.AE_ConfirmPlantSeed", "IsFlowerPlant", 1),
        new("GameCore.HotUpdate.Battle.Logic.AE_ItemAddBase5Attr", "IsPerfectDish", 1),
        new("GameCore.HotUpdate.Battle.Logic.DynamicEventManager", "GetTagDeleterRewardSets"),
        new("GameCore.HotUpdate.Battle.Logic.FoodPollutionManager", "IsMoldImmune", 1),
        new("GameCore.HotUpdate.Battle.Logic.GreenhouseProgress", "EnsureSampleMaps"),
        new("GameCore.HotUpdate.Battle.Logic.GreenhouseProgress", "EnsurePhaseLines"),
        new("GameCore.HotUpdate.Battle.Logic.ItemManager", "GetRobotModuleRecipes"),
        new("GameCore.HotUpdate.Battle.Logic.PreDisasterLootTheme", "IsThemedGroup", 1),
        new("GameCore.HotUpdate.Battle.Logic.StrangerSupportManager", "GetCureMap"),
        new("GameCore.HotUpdate.Battle.Logic.TowerCombatManager", "EnsureAttackRules"),
        new("GameCore.HotUpdate.Battle.Logic.TowerCombatManager", "GetDodgeChance", 1, 1),
        new("GameCore.HotUpdate.Battle.Logic.TowerRepairMaterial", "GetHpPerUnit", 1),
        new("GameCore.HotUpdate.Battle.Logic.TrapManager", "EnsurePreyDietCache"),
        new("GameCore.HotUpdate.GameKey", "TryGetShopFreshDecayMin", 1, new Out(typeof(float))),
        new("GameCore.HotUpdate.StrangerTrade.StrangerCommonShelfPool", "EnsureBuilt"),
        // FillGift stores Build(0) here on first use: the gift pool has no trade value cap.
        new("GameCore.HotUpdate.StrangerTrade.StrangerCommonShelfPool", "Build", 0) { Into = "_giftByDim" },
        // A device's InitWreckDamage stores this on first use; it parses the setting the same way for every device.
        new("GameCore.HotUpdate.Battle.Logic.TowerDeviceComponent", "ParseWreckRules", new Setting("TowerDeviceWreckDamage")) { Into = "wreckRuleTable" },
        new("GameCore.HotUpdate.StrangerTrade.SupplyGiftRules", "TierScores"),
        new("GameCore.HotUpdate.StrangerTrade.SupplyGiftRules", "TierCounts"),
        new("GameCore.HotUpdate.StrangerTrade.SupplyGiftRules", "SpecialByNpc"),
        new("GameCore.HotUpdate.WishPoolRules", "IsCookedDish", 1),
        // Thirteen screens each keep their own copy of the cooking quality table.
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_AidDepot", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_Brew", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_Compressor", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_Cooking", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_DroneHub", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_Dye", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_MaterialRack", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_RatCage", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_ShopUI", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_Shredder", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_ToolTable", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_TradeUI", "GetCookingQuality", 1),
        new("GameCore.HotUpdate.ReduxUI.Reducer_Web_TrapPanel", "GetCookingQuality", 1),
    };

    /// <summary>
    /// Calls every builder in <paramref name="builders"/> for <paramref name="type"/>, in order. Null when all ran (or
    /// there are none), else the first error, after which the rest are not called. <paramref name="setting"/> gives a
    /// global setting's Params3 by key, null when there is no such setting.
    /// </summary>
    public static string? Run(Type type, IReadOnlyList<Builder> builders, Func<string, string?>? setting = null)
    {
        var name = (type.FullName ?? type.Name).Replace('+', '.');

        foreach (var builder in builders.Where(b => b.ClassName == name))
        {
            const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var argTypes = builder.Args.Select(ArgType).ToArray();
            var method = type.GetMethod(builder.Method, statics, null, argTypes, null);

            if (method is null) return ValueWriter.ErrorPrefix + "no builder " + name + "." + builder.Method;

            var into = builder.Into is null ? null : type.GetProperty(builder.Into, statics);

            if (builder.Into is not null && into is null) return ValueWriter.ErrorPrefix + "no table " + name + "." + builder.Into;

            var args = new object?[builder.Args.Length];

            for (var i = 0; i < args.Length; i++)
            {
                switch (builder.Args[i])
                {
                    case Out o:
                        args[i] = Activator.CreateInstance(o.Type);
                        break;
                    case Setting s:
                        args[i] = setting?.Invoke(s.Key);
                        if (args[i] is null) return ValueWriter.ErrorPrefix + "no setting " + s.Key;
                        break;
                    default:
                        args[i] = builder.Args[i];
                        break;
                }
            }

            try
            {
                var result = method.Invoke(null, args);

                into?.SetValue(null, result);
            }
            catch (Exception ex)
            {
                return ValueWriter.ErrorText(ex);
            }
        }

        return null;
    }

    /// <summary>The parameter type a builder argument matches: by reference for <see cref="Out"/>, string for <see cref="Setting"/>.</summary>
    public static Type ArgType(object arg) => arg switch
    {
        Out o => o.Type.MakeByRefType(),
        Setting => typeof(string),
        _ => arg.GetType(),
    };
}
