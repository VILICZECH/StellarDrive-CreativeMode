using System.Collections;
using Research;
using Research.Interface.Values;
using UnityEngine;

namespace CreativeMode
{
    static class CreativeResearch
    {
        public static bool TryUnlock()
        {
            if (!CreativeSession.IsActive)
                return true;

            ResearchNetworkedState state = Object.FindAnyObjectByType<ResearchNetworkedState>();
            if (state == null)
                return false;
            if (!state.IsServerInitialized)
                return false;

            ResearchSettingsList settings = FindSettings();
            if (settings == null)
                return false;

            object raw = HarmonyLib.Traverse.Create(settings).Field("list").GetValue();
            IEnumerable entries = raw as IEnumerable;
            if (entries == null)
                return false;

            int count = 0;
            foreach (object entry in entries)
            {
                ResearchSettings research = entry as ResearchSettings;
                if (research == null)
                    continue;
                count++;
                if (!state.IsResearched(research.id))
                    state.ServerMarkResearchAsDone(research.id);
            }

            return count > 0;
        }

        static ResearchSettingsList FindSettings()
        {
            ResearchClientProxy[] proxies = Resources.FindObjectsOfTypeAll<ResearchClientProxy>();
            for (int i = 0; i < proxies.Length; i++)
            {
                ResearchClientProxy proxy = proxies[i];
                if (proxy == null || !proxy.gameObject.scene.IsValid())
                    continue;

                ResearchSettingsList settings = HarmonyLib.Traverse.Create(proxy)
                    .Field("researchSettingsList")
                    .GetValue<ResearchSettingsList>();
                if (settings != null)
                    return settings;
            }

            return null;
        }
    }
}
