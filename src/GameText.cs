using UnityEngine.Localization.Settings;

namespace CreativeMode
{
    static class GameText
    {
        public static string GameMode => Pick("Game mode", "Mode de jeu");

        public static string Survival => Pick("Survival", "Survie");

        public static string Creative => Pick("Creative", "Créatif");

        public static string CreativeMark => Pick("Creative", "Créatif");

        static string Pick(string english, string french)
        {
            try
            {
                string code = null;
                if (LocalizationSettings.SelectedLocale != null)
                    code = LocalizationSettings.SelectedLocale.Identifier.Code;
                if (code != null && code.StartsWith("fr"))
                    return french;
            }
            catch (System.Exception)
            {
            }

            return english;
        }
    }
}
