using LmpCommon.Time;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Settings.Structures;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Server.System
{
    /// <summary>
    /// Pauses the universe time while nobody is playing on the server.
    /// The universe time is calculated against the server start time, so we cannot simply "stop" it.
    /// Instead, when the last player disconnects we remember that moment and as soon as somebody joins
    /// again we move the server start time forward by the paused duration.
    /// </summary>
    public static class PauseWhenEmptySystem
    {
        private static string PausedAtFile { get; } = Path.Combine(ServerContext.UniverseDirectory, "PausedAt.txt");
        private static readonly object PauseLock = new object();
        public static DateTime? PausedAt { get; private set; }

        /// <summary>
        /// Called once on server startup, after the start time and subspaces were loaded.
        /// If the server was stopped while the universe was paused, the same correction that
        /// would be applied on a player join is applied here for the downtime and the universe
        /// stays paused until somebody joins.
        /// </summary>
        public static void Reset()
        {
            lock (PauseLock)
            {
                PausedAt = null;

                if (!GeneralSettings.SettingsStore.PauseWhenEmpty)
                {
                    FileHandler.FileDelete(PausedAtFile);
                    return;
                }

                var storedPausedAt = GetStoredPausedAtFromFile();
                if (storedPausedAt == null)
                    return;

                LunaLog.Normal("The server was stopped while the universe was paused, applying the pause correction for the downtime");

                Resume(storedPausedAt.Value);

                //There are still no players, so keep the universe paused from this moment on
                Pause();
            }
        }

        public static void HandlePlayerDisconnected(ClientStructure client)
        {
            lock (PauseLock)
            {
                if (!GeneralSettings.SettingsStore.PauseWhenEmpty || !client.Authenticated || PausedAt != null)
                    return;

                if (ClientRetriever.GetActiveClientCount() > 0)
                    return;

                Pause();
            }
        }

        public static void HandlePlayerAuthenticated()
        {
            lock (PauseLock)
            {
                if (!GeneralSettings.SettingsStore.PauseWhenEmpty || PausedAt == null)
                    return;

                Resume(PausedAt.Value);
            }
        }

        #region Private methods

        private static void Pause()
        {
            PausedAt = LunaNetworkTime.UtcNow;
            WritePausedAtToFile(PausedAt.Value);
            LunaLog.Normal("Universe paused: nobody is playing on the server");
        }

        private static void Resume(DateTime pausedAt)
        {
            var pausedDuration = LunaNetworkTime.UtcNow - pausedAt;
            if (pausedDuration > TimeSpan.Zero)
            {
                TimeSystem.ShiftStartTime(pausedDuration);
                LunaLog.Normal($"Universe resumed after {pausedDuration}: universe time adjusted back to the moment it was left");
            }
            else
            {
                LunaLog.Normal("Universe resumed");
            }

            PausedAt = null;
            FileHandler.FileDelete(PausedAtFile);
        }

        private static void WritePausedAtToFile(DateTime pausedAt)
        {
            var content = $"#This file stores the UTC date and time when the universe was paused.{Environment.NewLine}";

            content += pausedAt.ToString("O", CultureInfo.InvariantCulture);

            FileHandler.WriteToFile(PausedAtFile, content);
        }

        private static DateTime? GetStoredPausedAtFromFile()
        {
            if (!FileHandler.FileExists(PausedAtFile))
                return null;

            var pausedAtLine = FileHandler.ReadFileLines(PausedAtFile)
                .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#")).FirstOrDefault();

            if (pausedAtLine == null || !DateTime.TryParseExact(pausedAtLine, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var pausedAt))
            {
                LunaLog.Error("Incorrect PausedAt.txt file! Ignoring the stored universe pause");
                FileHandler.FileDelete(PausedAtFile);
                return null;
            }

            return pausedAt;
        }

        #endregion
    }
}