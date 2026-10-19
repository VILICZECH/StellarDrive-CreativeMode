using MelonLoader;
using UI.Status;
using UnityEngine;

[assembly: MelonInfo(typeof(CreativeMode.CreativeModeMod), "Creative mode", "1.6.3", "VILIK")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace CreativeMode
{
    public class CreativeModeMod : MelonMod
    {
        bool _researchDone;
        bool _journalDone;
        float _researchDeadline;
        int _vitalPasses;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Creative mode is ready. Choose it before creating a world.");
            PickLookedAtPart.EnsureHooked();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _researchDone = false;
            _journalDone = false;
            _researchDeadline = Time.unscaledTime + 20f;
            InfiniteFuel.Clear();
            BuildUndo.ClearHistory();
            if (sceneName == "MainGame")
                CreativeSession.RecordMissingAsSurvival();
            _vitalPasses = sceneName == "MainGame" && CreativeSession.IsActive ? 30 : 0;
            if (sceneName == "MainGame" && CreativeSession.IsActive)
                LoggerInstance.Msg("Creative world. Double-tap space to toggle flight.");
            else if (sceneName == "MainGame")
                LoggerInstance.Msg("Survival world. A save without a creative setting plays as survival.");
        }

        public override void OnUpdate()
        {
            BuildUndoNet.Ensure();
            CreativeFlight.Tick();
            if (_vitalPasses > 0)
            {
                _vitalPasses--;
                HideVitals();
            }

            if (!CreativeSession.IsActive)
                return;

            if (!_researchDone)
            {
                _researchDone = CreativeResearch.TryUnlock();
                if (_researchDone)
                    LoggerInstance.Msg("Research is unlocked.");
            }

            if (!_journalDone)
            {
                _journalDone = CreativeJournal.TryUnlock();
                if (_journalDone)
                    LoggerInstance.Msg("Journal entries are unlocked.");
            }

            if ((!_researchDone || !_journalDone) && Time.unscaledTime > _researchDeadline)
            {
                if (!_researchDone)
                    LoggerInstance.Warning("Research could not be unlocked.");
                if (!_journalDone)
                    LoggerInstance.Warning("Journal entries could not be unlocked.");
                _researchDone = true;
                _journalDone = true;
            }
        }

        static void HideVitals()
        {
            HealthStatusBar[] health = Object.FindObjectsByType<HealthStatusBar>(FindObjectsSortMode.None);
            for (int i = 0; i < health.Length; i++)
                HideHealth.Hide(health[i]);

            OxygenStatusBar[] oxygen = Object.FindObjectsByType<OxygenStatusBar>(FindObjectsSortMode.None);
            for (int i = 0; i < oxygen.Length; i++)
                HideHealth.Hide(oxygen[i]);
        }
    }
}
