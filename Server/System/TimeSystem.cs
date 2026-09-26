using LmpCommon.Time;
using Server.Context;
using Server.Log;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Server.System
{
    public static class TimeSystem
    {
        private static string StartTimeFile { get; } = Path.Combine(ServerContext.UniverseDirectory, "StartTime.txt");

        public static void Reset()
        {
            LoadSavedStartTime();
        }

        public static void BackupStartTime()
        {
            var content = $"#Incorrectly editing this file will cause weirdness. If there is any errors, the universe time will be reset.{Environment.NewLine}";
            content += $"#This file can only be edited if the server is stopped.{Environment.NewLine}";
            content += $"#It must always contain ONLY 1 line which will have the date and time when the server started in UTC{Environment.NewLine}";

            content += $"{TimeContext.StartTime:s}";

            FileHandler.WriteToFile(StartTimeFile, content);
        }

        public static void ShiftStartTime(TimeSpan duration)
        {
            TimeContext.StartTime += duration;
            BackupStartTime();
        }

        #region Private methods

        private static void LoadSavedStartTime()
        {
            if (FileHandler.FileExists(StartTimeFile))
            {
                TimeContext.StartTime = GetStoredStartTimeFromFile();
            }
            else
            {
                LunaLog.Debug("Creating new start time file");
                TimeContext.StartTime = LunaNetworkTime.UtcNow;
            }
        }

        private static DateTime GetStoredStartTimeFromFile()
        {
            var startTimeLine = FileHandler.ReadFileLines(StartTimeFile)
                .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#")).SingleOrDefault();

            if (startTimeLine == null || !DateTime.TryParseExact(startTimeLine, "s", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var startTime))
            {
                LunaLog.Error("Incorrect StartTime.txt file!");
                return LunaNetworkTime.UtcNow;
            }

            return startTime;
        }

        #endregion

    }
}
