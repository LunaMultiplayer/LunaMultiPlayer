using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LmpCommon.CraftLibrary
{
    /// <summary>
    ///     Name and path rules of the craft library, shared by the client and the server. KSP runs on
    ///     Windows, so the names and the characters Windows refuses are rejected even when the server runs
    ///     on Linux: a craft the server accepted but the client cannot store desynchronizes both libraries.
    /// </summary>
    public static class CraftLibraryPath
    {
        public const int MaxCraftFolderLength = 200;
        public const string TransientCraftName = "Auto-Saved Ship";

        private static readonly char[] InvalidFileNameChars =
            { '"', '<', '>', '|', ':', '*', '?', '\\', '/', '\0', (char)127 };

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static IReadOnlyList<CraftType> AllTypes { get; } = (CraftType[])Enum.GetValues(typeof(CraftType));

        public static bool CraftNameIsValid(string craftName)
        {
            return !string.IsNullOrWhiteSpace(craftName) && IsValidSegment(craftName);
        }

        public static bool IsTransientCraftName(string craftName)
        {
            return string.Equals(craftName, TransientCraftName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CraftNameIsValid(string craftName, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(craftName))
            {
                reason = "empty name";
                return false;
            }

            if (!IsValidSegment(craftName))
            {
                reason = "not a valid Windows file name";
                return false;
            }

            return true;
        }

        public static bool CraftFolderIsValid(string craftFolder)
        {
            return CraftFolderIsValid(craftFolder, out _);
        }

        public static bool CraftFolderIsValid(string craftFolder, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(craftFolder))
                return true;

            if (craftFolder.Length > MaxCraftFolderLength)
            {
                reason = $"the subfolder path is longer than {MaxCraftFolderLength} characters";
                return false;
            }

            if (!craftFolder.Split('/', '\\').All(IsValidSegment))
            {
                reason = "contains a folder name that is not a valid Windows folder name";
                return false;
            }

            return true;
        }

        public static bool PlayerFolderIsValid(string folderName)
        {
            return IsValidSegment(folderName);
        }

        public static bool IsValidCraftType(CraftType craftType)
        {
            return AllTypes.Contains(craftType);
        }

        public static string NormalizeCraftFolder(string craftFolder)
        {
            return string.IsNullOrEmpty(craftFolder) ? string.Empty : string.Join("/", craftFolder.Split('\\', '/'));
        }

        public static string GetCraftFolderFromPath(string relativeCraftPath)
        {
            var directory = Path.GetDirectoryName(relativeCraftPath);
            return string.IsNullOrEmpty(directory) ? string.Empty : directory.Replace('\\', '/');
        }

        public static string GetCraftFilePath(string root, string folderName, CraftType craftType, string craftFolder, string craftName)
        {
            var craftTypeFolder = Path.Combine(Path.Combine(root, folderName), craftType.ToString());
            if (string.IsNullOrEmpty(craftFolder))
                return Path.Combine(craftTypeFolder, craftName + ".craft");

            return Path.Combine(new[] { craftTypeFolder }.Concat(craftFolder.Split('/'))
                .Concat(new[] { craftName + ".craft" }).ToArray());
        }

        public static void PruneEmptyDirectoriesUpTo(string startDirectory, string boundaryFolder)
        {
            var current = startDirectory;
            while (IsBelow(current, boundaryFolder))
            {
                if (Directory.EnumerateFileSystemEntries(current).Any())
                    return;

                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
            }
        }

        private static bool IsBelow(string directory, string boundaryFolder)
        {
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(boundaryFolder))
                return false;

            return !string.Equals(directory, boundaryFolder, StringComparison.OrdinalIgnoreCase)
                   && directory.StartsWith(boundaryFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return false;

            if (segment[segment.Length - 1] == ' ' || segment[segment.Length - 1] == '.')
                return false;

            if (segment.IndexOfAny(InvalidFileNameChars) >= 0 || segment.Any(c => c < 32))
                return false;

            return !ReservedNames.Contains(segment.Split('.')[0]);
        }
    }
}