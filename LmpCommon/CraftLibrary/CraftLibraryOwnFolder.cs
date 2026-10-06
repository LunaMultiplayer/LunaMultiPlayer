using System;

namespace LmpCommon.CraftLibrary
{
    /// <summary>
    /// The craft folder of the local player is "{playerName} (me)". The suffix, not the player name, is what
    /// makes a folder ours, so a player who renames himself keeps his crafts. The server stores every player
    /// under his plain name, so a server player name may never end with the suffix.
    /// </summary>
    public static class CraftLibraryOwnFolder
    {
        public const string Suffix = "(me)";

        /// <summary>The folder of the player called <paramref name="playerName" />, or null without a name</summary>
        public static string NameFor(string playerName)
        {
            return string.IsNullOrEmpty(playerName) ? null : $"{playerName} {Suffix}";
        }

        /// <summary>True when the folder holds the crafts of the local player, the space before the suffix is optional</summary>
        public static bool IsOwnFolder(string folderName)
        {
            return !string.IsNullOrEmpty(folderName) && folderName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for a player name a server has to refuse, his craft folder would look like ours</summary>
        public static bool PlayerNameIsForbidden(string playerName)
        {
            return IsOwnFolder(playerName);
        }
    }
}