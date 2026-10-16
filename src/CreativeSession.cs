using System;
using System.IO;
using Managers.Interface;
using MelonLoader;

namespace CreativeMode
{
    public enum WorldKind
    {
        Unset,
        Survival,
        Creative
    }

    public static class CreativeSession
    {
        public const string FileName = "creative.mode";

        public static WorldKind Pending = WorldKind.Unset;

        public static bool Committing;

        static string _cachedDirectory;
        static bool _cachedActive;

        public static bool IsActive
        {
            get
            {
                string directory = CurrentWorldDirectory();
                if (directory != _cachedDirectory)
                {
                    _cachedDirectory = directory;
                    _cachedActive = DirectoryIsCreative(directory);
                }

                return _cachedActive;
            }
        }

        public static string CurrentWorldDirectory()
        {
            string path = MainGameSceneLoadData.SaveFileFullName;
            if (string.IsNullOrEmpty(path))
                return null;
            return Path.GetDirectoryName(path);
        }

        public static bool DirectoryIsCreative(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory))
                    return false;

                string marker = Path.Combine(directory, FileName);
                if (!File.Exists(marker))
                    return false;

                string text = File.ReadAllText(marker);
                return text != null && text.Trim() == "creative";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Could not read the creative world marker. This world is survival. " + ex.Message);
                return false;
            }
        }

        public static void RecordMissingAsSurvival()
        {
            _cachedDirectory = null;
            string directory = CurrentWorldDirectory();
            if (string.IsNullOrEmpty(directory))
                return;

            string marker = Path.Combine(directory, FileName);
            if (File.Exists(marker))
                return;

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(marker, "survival");
                _cachedDirectory = null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Could not store the survival setting. This world still plays as survival. " + ex.Message);
            }
        }

        public static void MarkDirectory(string directory, bool creative)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, FileName), creative ? "creative" : "survival");
            _cachedDirectory = null;
        }

    }
}
