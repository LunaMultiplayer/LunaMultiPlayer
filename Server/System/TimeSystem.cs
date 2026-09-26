using LmpCommon.Time;
using Server.Context;
using Server.Log;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Server.Settings;
using Server.Settings.Structures;

namespace Server.System
{
    public static class TimeSystem
    {
        private static string TimeFile { get; } = Path.Combine(ServerContext.UniverseDirectory, "Time.txt");
        private static string OldStartTimeFile { get; } = Path.Combine(ServerContext.UniverseDirectory, "StartTime.txt");

        public static void Reset()
        {
            LoadSavedTimes();

            if (WarpSettings.SettingsStore.PauseTimeWhileShutdown)
            {
                var timeDiffrence = TimeContext.StartTime - TimeContext.EndTime;
                TimeContext.StartTime += timeDiffrence;
            }
        }

        public static void BackupStartTime()
        {
            var content = $"#Incorrectly editing this file will cause weirdness. If there is any errors, the universe time will be reset.{Environment.NewLine}";
            content += $"#This file can only be edited if the server is stopped.{Environment.NewLine}";
            content += $"#It must always contain 2 lines which will have the date and time when the server started and last stopped in UTC{Environment.NewLine}";

            content += $"StartTime:{TimeContext.StartTime:s}{Environment.NewLine}";
            content += $"EndTime:{LunaNetworkTime.UtcNow:s}";

            FileHandler.WriteToFile(TimeFile, content);
        }

        #region Private methods

        private static void LoadSavedTimes()
        {
            if (FileHandler.FileExists(TimeFile))
            {
                TimeContext.StartTime = GetStoredStartTimeFromFile();
                TimeContext.EndTime = GetStoredEndTimeFromFile();
            }
            else
            {
                if (FileHandler.FileExists(OldStartTimeFile))
                {
                    LunaLog.Debug("Creating new time file, replacement of start time file");
                    TimeContext.StartTime = GetStoredStartTimeFromFile();
                    TimeContext.EndTime = LunaNetworkTime.UtcNow;
                    File.Delete(OldStartTimeFile); // delete it since we will be using the new timefile in the future
                }
                else
                {
                    LunaLog.Debug("Creating new time file");
                    TimeContext.StartTime = LunaNetworkTime.UtcNow;
                    TimeContext.EndTime = LunaNetworkTime.UtcNow;
                }
            }
        }

        private static DateTime GetStoredStartTimeFromFile()
        {
            var startTimeLine = FileHandler.ReadFileLines(TimeFile)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("StartTime:"));

            if (startTimeLine != null)
            {
                startTimeLine = startTimeLine
                    .Substring("StartTime:".Length)
                    .Trim();
            }
            else
            { // fallback to old system
                startTimeLine = FileHandler.ReadFileLines(OldStartTimeFile)
                    .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#")).SingleOrDefault();
            }

            if (startTimeLine == null || !DateTime.TryParseExact(startTimeLine, "s", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var startTime))
            {
                LunaLog.Error("Incorrect Time.txt file!");
                return LunaNetworkTime.UtcNow;
            }

            return startTime;
        }

        private static DateTime GetStoredEndTimeFromFile()
        {
            var endTimeLine = FileHandler.ReadFileLines(TimeFile)
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("EndTime:"));

            if (endTimeLine != null)
                endTimeLine = endTimeLine.Substring("EndTime:".Length);

            if (endTimeLine == null || !DateTime.TryParseExact(endTimeLine, "s", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var endTime))
            {
                LunaLog.Error("Incorrect Time.txt file!");
                return LunaNetworkTime.UtcNow;
            }

            return endTime;
        }


        #endregion

    }
}
