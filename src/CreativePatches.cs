using System;
using Core.Services;
using HarmonyLib;
using Managers.Interface.Services;
using MelonLoader;
using Players;
using Players.Network;
using Players.Physics;
using Players.Services;
using Players.Utils;
using Players.Values;
using Saving.Interface.Model;
using Saving.Interface.Services;
using TMPro;
using UI.BuildCostTip;
using UI.BuildMenu;
using UI.Common;
using UI.MainMenu;
using UI.MainMenu.Common;
using UI.Status;
using UI.Warnings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CreativeMode
{
    [HarmonyPatch(typeof(CreateNewWorldButton), "CreateNewWorld")]
    static class OpenModeBeforeWorldOptions
    {
        static bool Prefix(CreateNewWorldButton __instance)
        {
            if (__instance.GetComponentInParent<CreateNewWorldHandler>() == null)
                return true;

            ModePicker.Open(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(CreateWorldModal), "OnDisable")]
    static class CloseModeStep
    {
        static void Postfix()
        {
            ModePicker.OnModalDisabled();
        }
    }

    [HarmonyPatch(typeof(CreateNewWorldHandler), "OnWorldCreate")]
    static class RememberWorldKind
    {
        static void Prefix()
        {
            CreativeSession.Committing = true;
        }

        static void Postfix(string worldName)
        {
            try
            {
                IGameSavesProvider saves = ServiceLocator.GetService<IGameSavesProvider>();
                IWorldDirectoryInfo world = FindWorld(saves, worldName);
                if (world == null || world.Directory == null)
                    return;

                CreativeSession.MarkDirectory(world.Directory.FullName, CreativeSession.Pending == WorldKind.Creative);
                CreativeSession.Pending = WorldKind.Unset;
            }
            catch (Exception ex)
            {
                Melon<CreativeModeMod>.Logger.Error("Could not mark the world as creative.", ex);
            }
            finally
            {
                CreativeSession.Committing = false;
            }
        }

        static IWorldDirectoryInfo FindWorld(IGameSavesProvider saves, string worldName)
        {
            if (saves == null || string.IsNullOrEmpty(worldName))
                return null;

            foreach (IWorldDirectoryInfo entry in saves.GetWorldsList())
            {
                if (entry != null && entry.WorldName == worldName)
                    return entry;
            }

            return null;
        }
    }

    [HarmonyPatch(typeof(WorldDirectorySelectButton), "Initialize")]
    static class MarkCreativeWorldInList
    {
        static void Postfix(WorldDirectorySelectButton __instance, IWorldDirectoryInfo worldDir)
        {
            if (worldDir == null || worldDir.Directory == null)
                return;
            if (!CreativeSession.DirectoryIsCreative(worldDir.Directory.FullName))
                return;

            TMP_Text label = Traverse.Create(__instance).Field("fileNameText").GetValue<TMP_Text>();
            if (label != null)
                label.text = worldDir.WorldName + "  ·  " + GameText.CreativeMark;
        }
    }

    [HarmonyPatch(typeof(StartGameDetails), "Initialize")]
    static class MarkCreativeWorldInDetails
    {
        static void Postfix(StartGameDetails __instance, MenuButtonElement selectedButton)
        {
            if (selectedButton == null)
                return;

            WorldDirectorySelectButton entry = selectedButton.GetComponent<WorldDirectorySelectButton>();
            if (entry == null || entry.WorldDirectory == null || entry.WorldDirectory.Directory == null)
                return;
            if (!CreativeSession.DirectoryIsCreative(entry.WorldDirectory.Directory.FullName))
                return;

            TMP_Text label = Traverse.Create(__instance).Field("fileName").GetValue<TMP_Text>();
            if (label != null)
                label.text = entry.FileName + "  ·  " + GameText.CreativeMark;
        }
    }

    [HarmonyPatch(typeof(PlayerPrefabLifecycle), "CheckLocalPlayerTutorial")]
    static class SkipCreativeTutorial
    {
        static bool Prefix()
        {
            if (!CreativeSession.IsActive)
                return true;

            try
            {
                IPlayerTutorial tutorial = ServiceLocator.GetService<IPlayerTutorial>();
                if (tutorial != null)
                    tutorial.DisableTutorial();
            }
            catch (Exception ex)
            {
                Melon<CreativeModeMod>.Logger.Error("Could not disable the tutorial.", ex);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Players.TrackedPlayerServer), "SetInventoryToTutorial")]
    static class GiveCreativeTools
    {
        static bool Prefix(Players.TrackedPlayerServer __instance)
        {
            if (!CreativeSession.IsActive)
                return true;

            __instance.MarkTutorialCompleted();
            __instance.SetInventoryToBasicGear();
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerPrefabLifecycle), "SpawnServerPlayer")]
    static class ApplyCreativeBody
    {
        static void Postfix(PlayerNetworkState networkState)
        {
            if (networkState == null)
                return;

            if (!CreativeSession.IsActive)
            {
                networkState.ServerSetUsingCheatFreeBuild(false);
                return;
            }

            networkState.ServerSetUsingCheatFreeBuild(true);
            networkState.ServerSetHasOxygen(true);
            networkState.ServerSetHealth(PlayerDefaultState.DefaultHealth);
            networkState.ServerSetOxygenTankLevel(PlayerDefaultState.DefaultOxygen);
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RefundBuildCost")]
    static class NoCreativeBuildRefund
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch("Managers.Server.Services.ShipInteractionService", "RefundItems")]
    static class NoCreativeContentsRefund
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkState), "ServerSetUsingCheatFreeBuild")]
    static class KeepFreeBuild
    {
        static void Prefix(ref bool value)
        {
            value = CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(TrackedPlayer), "get_IsUsingCheatFreeBuild")]
    static class SurvivalBuildOnPlayer
    {
        static void Postfix(ref bool __result)
        {
            __result = CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkState), "get_IsUsingCheatFreeBuild")]
    static class SurvivalBuildOnState
    {
        static void Postfix(ref bool __result)
        {
            __result = CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayersClientTracker), "IsLocalPlayerUsingFreeBuildCheat")]
    static class SurvivalBuildOnClient
    {
        static void Postfix(ref bool __result)
        {
            __result = CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkState), "ServerSetHealth")]
    static class KeepHealthFull
    {
        static void Prefix(ref float health)
        {
            if (CreativeSession.IsActive && health < PlayerDefaultState.DefaultHealth)
                health = PlayerDefaultState.DefaultHealth;
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkState), "ServerSetOxygenTankLevel")]
    static class KeepOxygenFull
    {
        static void Prefix(ref float oxygen)
        {
            if (CreativeSession.IsActive && oxygen < PlayerDefaultState.DefaultOxygen)
                oxygen = PlayerDefaultState.DefaultOxygen;
        }
    }

    [HarmonyPatch(typeof(PlayerNetworkState), "ServerSetHasOxygen")]
    static class KeepBreathing
    {
        static void Prefix(ref bool hasOxygen)
        {
            if (CreativeSession.IsActive)
                hasOxygen = true;
        }
    }

    [HarmonyPatch(typeof(PlayerCheatFlyPhysics), "CalculateVelocity")]
    static class SlowCreativeFly
    {
        static void Postfix(PlayerCheatFlyPhysics __instance, Vector3 moveInput, ref float __result)
        {
            if (!CreativeSession.IsActive || moveInput.sqrMagnitude < 0.0001f)
            {
                if (CreativeSession.IsActive)
                    __result = 0f;
                return;
            }

            object common = Traverse.Create(__instance).Field("_playerPhysicsCommon").GetValue();
            PlayerMovementSettings settings = Traverse.Create(common).Property("MovementSettings").GetValue<PlayerMovementSettings>();
            if (settings == null)
                return;

            bool sprint = Keyboard.current != null
                && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
            float speed = sprint ? settings.sprintingSpeed : settings.walkingSpeed;
            if (speed < 0.1f)
                speed = settings.walkingSpeed;
            __result = speed;
        }
    }

    [HarmonyPatch(typeof(PlayerStatusUpdater), "UpdateStatus")]
    static class StopStatusDrain
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(PlayerStatusUpdater), "OnCollisionDamage")]
    static class StopCollisionDamage
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(Research.ResearchNetworkedState), "LoadState")]
    static class UnlockResearchOnLoad
    {
        static void Postfix()
        {
            CreativeResearch.TryUnlock();
        }
    }

    [HarmonyPatch(typeof(HealthStatusBar), "Awake")]
    static class HideHealth
    {
        static void Postfix(HealthStatusBar __instance)
        {
            Hide(__instance);
        }

        internal static void Hide(MonoBehaviour widget)
        {
            if (!CreativeSession.IsActive || widget == null)
                return;

            Component bar = Traverse.Create(widget).Field("statusBar").GetValue<Component>();
            if (bar != null)
                bar.gameObject.SetActive(false);
            widget.enabled = false;
        }
    }

    [HarmonyPatch(typeof(OxygenStatusBar), "Awake")]
    static class HideOxygen
    {
        static void Postfix(OxygenStatusBar __instance)
        {
            HideHealth.Hide(__instance);
        }
    }

    [HarmonyPatch(typeof(OxygenWarningToggler), "UpdateOxygenChange")]
    static class StopOxygenWarning
    {
        static bool Prefix()
        {
            return !CreativeSession.IsActive;
        }
    }

    [HarmonyPatch(typeof(BuildMenuPartInfo), "Show")]
    static class HidePartCosts
    {
        static void Postfix(BuildMenuPartInfo __instance)
        {
            BuildCostHider.Strip(__instance, "elementsList");
        }
    }

    [HarmonyPatch(typeof(BuildTooltip), "Show")]
    static class HideTooltipCosts
    {
        static void Postfix(BuildTooltip __instance)
        {
            BuildCostHider.Strip(__instance, "costInfoContainer");
        }
    }

    [HarmonyPatch(typeof(BuildCostTooltipEnabler), "ShowTooltip")]
    static class HidePlacementCosts
    {
        static bool Prefix(BuildCostTooltipEnabler __instance)
        {
            if (!CreativeSession.IsActive)
                return true;

            __instance.gameObject.SetActive(false);
            return false;
        }
    }

    static class BuildCostHider
    {
        internal static void Strip(MonoBehaviour host, string containerField)
        {
            if (!CreativeSession.IsActive || host == null)
                return;

            ItemCostInfo[] costs = host.GetComponentsInChildren<ItemCostInfo>(true);
            Transform container = Traverse.Create(host).Field(containerField).GetValue<Transform>();
            for (int i = 0; i < costs.Length; i++)
            {
                if (costs[i] != null)
                    UnityEngine.Object.DestroyImmediate(costs[i].gameObject);
            }

            RectTransform rect = container as RectTransform;
            if (rect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
        }
    }
}
