using System.Collections.Generic;
using Core.Services;
using HarmonyLib;
using Players;
using Players.Network;
using Players.Services;
using Research;
using UI.Guide;
using UI.Guide.GuideEntries;
using UnityEngine;

namespace CreativeMode
{
    static class CreativeJournal
    {
        public static bool TryUnlock()
        {
            if (!CreativeSession.IsActive)
                return true;

            GuideEntriesProvider guides = ServiceLocator.GetService<GuideEntriesProvider>();
            if (guides == null)
            {
                GuideEntriesProvider[] found = Object.FindObjectsByType<GuideEntriesProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (found.Length > 0)
                    guides = found[0];
            }

            PlayersClientTracker clients = ServiceLocator.GetService<PlayersClientTracker>();
            PlayersServerTracker servers = ServiceLocator.GetService<PlayersServerTracker>();
            if (guides == null || clients == null || clients.LocalPlayer == null || servers == null)
                return false;

            TrackedPlayerServer player = servers.GetPlayer(clients.LocalPlayer.Id) as TrackedPlayerServer;
            if (player == null)
                return false;

            PlayerNetworkState state = Traverse.Create(player).Property("NetworkState").GetValue<PlayerNetworkState>();
            if (state == null || !state.IsServerInitialized)
                return false;

            IReadOnlyCollection<GuideEntryContent> entries = guides.AllGuideEntries;
            if (entries == null)
                return false;

            foreach (GuideEntryContent entry in entries)
            {
                if (entry != null)
                    state.ServerUnlockGuideEntry(entry.EntryId);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(PlayersClientTracker), "HasLocalPlayerUnlockedGuideEntry")]
    static class ReadEveryJournalEntry
    {
        static void Postfix(ref bool __result)
        {
            if (CreativeSession.IsActive)
                __result = true;
        }
    }

    [HarmonyPatch(typeof(ResearchClientProxy), "GetResearchedGuideEntries")]
    static class ReadResearchedJournalEntries
    {
        static void Postfix(ResearchClientProxy __instance, ref IReadOnlyCollection<uint> __result)
        {
            if (!CreativeSession.IsActive || __instance == null)
                return;
            __result = __instance.GetAllGuideEntriesFoundThroughResearch();
        }
    }
}
