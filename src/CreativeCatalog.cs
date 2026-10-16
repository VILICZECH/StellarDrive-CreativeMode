using System.Collections.Generic;
using HarmonyLib;
using Players;
using Research;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Tools.Build;
using UI.BuildMenu;
using UnityEngine;

namespace CreativeMode
{
    static class CreativeCatalog
    {
        public const string TabName = "Mods";

        public static void IncludeModParts(BuildMenu menu)
        {
            if (!CreativeSession.IsActive || menu == null)
                return;

            Traverse fields = Traverse.Create(menu);
            BuildMenuTabConfig[] tabs = fields.Field("tabsConfig").GetValue<BuildMenuTabConfig[]>();
            if (tabs == null)
                tabs = new BuildMenuTabConfig[0];
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null && tabs[i].name == TabName)
                    return;
            }

            HashSet<ushort> listed = new HashSet<ushort>();
            for (int i = 0; i < tabs.Length; i++)
            {
                BuildMenuTabConfig tab = tabs[i];
                if (tab == null || tab.rows == null)
                    continue;
                for (int r = 0; r < tab.rows.Length; r++)
                {
                    PartSettings[] row = tab.rows[r] == null ? null : tab.rows[r].parts;
                    if (row == null)
                        continue;
                    for (int p = 0; p < row.Length; p++)
                    {
                        if (row[p] != null)
                            listed.Add(row[p].id);
                    }
                }
            }

            List<PartSettings> extra = new List<PartSettings>();
            try
            {
                IReadOnlyCollection<PartSettings> all = new PartSettingsProvider().GetAllPartSettings();
                if (all != null)
                {
                    foreach (PartSettings part in all)
                    {
                        if (part == null || listed.Contains(part.id))
                            continue;
                        if (part.part == null && string.IsNullOrEmpty(part.customCreationClass))
                            continue;
                        extra.Add(part);
                        listed.Add(part.id);
                    }
                }
            }
            catch (System.Exception)
            {
                return;
            }

            if (extra.Count == 0)
                return;

            const int rowSize = 6;
            int rowCount = (extra.Count + rowSize - 1) / rowSize;
            BuildMenuTabConfig.BuildMenuRow[] rows = new BuildMenuTabConfig.BuildMenuRow[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                int start = r * rowSize;
                int count = Mathf.Min(rowSize, extra.Count - start);
                PartSettings[] parts = new PartSettings[count];
                for (int i = 0; i < count; i++)
                    parts[i] = extra[start + i];
                BuildMenuTabConfig.BuildMenuRow row = new BuildMenuTabConfig.BuildMenuRow();
                row.parts = parts;
                rows[r] = row;
            }

            BuildMenuTabConfig mods = new BuildMenuTabConfig();
            mods.name = TabName;
            mods.rows = rows;
            if (tabs.Length > 0 && tabs[0] != null)
                mods.icon = tabs[0].icon;

            BuildMenuTabConfig[] next = new BuildMenuTabConfig[tabs.Length + 1];
            for (int i = 0; i < tabs.Length; i++)
                next[i] = tabs[i];
            next[tabs.Length] = mods;
            fields.Field("tabsConfig").SetValue(next);
        }
    }

    [HarmonyPatch(typeof(BuildMenu), "GenerateTabButtonsFromConfig")]
    static class ShowModParts
    {
        static void Prefix(BuildMenu __instance)
        {
            CreativeCatalog.IncludeModParts(__instance);
        }
    }

    [HarmonyPatch(typeof(ResearchClientProxy), "IsPartUnlocked")]
    static class UnlockEveryPart
    {
        static void Postfix(ref bool __result)
        {
            if (CreativeSession.IsActive)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(ResearchClientProxy), "IsFeatureUnlocked")]
    static class UnlockEveryFeature
    {
        static void Postfix(ref bool __result)
        {
            if (CreativeSession.IsActive)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(PartPreview), "CheckHasEnoughMaterial")]
    static class IgnoreBuildCost
    {
        static bool Prefix(ref bool __result)
        {
            if (!CreativeSession.IsActive)
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TrackedPlayerServer), "HasItemsInInventory")]
    static class IgnoreServerBuildCost
    {
        static void Postfix(ref bool __result)
        {
            if (CreativeSession.IsActive)
                __result = true;
        }
    }
}
