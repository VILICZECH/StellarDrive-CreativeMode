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
        static bool _remoteKnown;
        static bool _remoteCreative;

        public static bool RemoteKnown
        {
            get { return _remoteKnown; }
        }

        public static bool IsActive
        {
            get
            {
                // A joined client has no copy of the host's save. The host says which world this is.
                if (_remoteKnown && !RunsTheWorld())
                    return _remoteCreative;

                string directory = CurrentWorldDirectory();
                if (directory != _cachedDirectory)
                {
                    _cachedDirectory = directory;
                    _cachedActive = DirectoryIsCreative(directory);
                }

                return _cachedActive;
            }
        }

        public static bool IsJoinedClient()
        {
            MainGameSceneLoadData.GameMode mode = MainGameSceneLoadData.Mode;
            return mode == MainGameSceneLoadData.GameMode.JoinGameSteam
                || mode == MainGameSceneLoadData.GameMode.JoinGameIp;
        }

        static bool RunsTheWorld()
        {
            try
            {
                return FishNet.InstanceFinder.IsServerStarted;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void ApplyRemote(bool creative)
        {
            _remoteKnown = true;
            _remoteCreative = creative;
            _cachedDirectory = null;
        }

        public static void ClearRemote()
        {
            _remoteKnown = false;
            _remoteCreative = false;
            _cachedDirectory = null;
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
            if (IsJoinedClient())
                return;
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
