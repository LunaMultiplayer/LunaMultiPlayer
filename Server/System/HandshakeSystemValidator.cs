using LmpCommon.CraftLibrary;
using LmpCommon.Enums;
using Server.Client;
using Server.Command.Command;
using Server.Settings.Structures;
using System.Linq;
using System.Text.RegularExpressions;

namespace Server.System
{
    public partial class HandshakeSystem
    {
        public static bool PlayerNameIsValid(string playerName, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrEmpty(playerName))
            {
                reason = "Username too short. Min chars: 1";
                return false;
            }

            if (CraftLibraryOwnFolder.PlayerNameIsForbidden(playerName))
            {
                reason = "Player name forbidden";
                return false;
            }

            if (playerName.Length > GeneralSettings.SettingsStore.MaxUsernameLength)
            {
                reason = $"Username too long. Max chars: {GeneralSettings.SettingsStore.MaxUsernameLength}";
                return false;
            }

            var regex = new Regex(@"^[-_a-zA-Z0-9]+$"); // Regex to only allow alphanumeric, dashes and underscore
            if (!regex.IsMatch(playerName))
            {
                reason = "Invalid username characters (only A-Z, a-z, numbers, - and _)";
                return false;
            }

            return true;
        }

        private bool CheckUsernameLength(ClientStructure client, string username, out string reason)
        {
            reason = string.Empty;
            if (!PlayerNameIsValid(username, out var validationReason))
            {
                if (validationReason.Contains("long") || validationReason.Contains("short"))
                {
                    reason = validationReason;
                    HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.InvalidPlayername, reason);
                    return false;
                }
            }

            return true;
        }

        private bool CheckServerFull(ClientStructure client, out string reason)
        {
            reason = string.Empty;
            if (ClientRetriever.GetActiveClientCount() >= GeneralSettings.SettingsStore.MaxPlayers)
            {
                reason = "Server full";
                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.ServerFull, reason);
                return false;
            }
            return true;
        }

        private bool CheckPlayerIsBanned(ClientStructure client, string uniqueId, out string reason)
        {
            reason = string.Empty;
            if (BanPlayerCommand.GetBannedPlayers().Contains(uniqueId))
            {
                reason = "Banned";
                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.PlayerBanned, reason);
                return false;
            }
            return true;
        }

        private bool CheckUsernameIsReserved(ClientStructure client, string playerName, out string reason)
        {
            reason = string.Empty;
            if (playerName == "Initial" || playerName == GeneralSettings.SettingsStore.ConsoleIdentifier)
            {
                reason = "Using reserved name";
                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.InvalidPlayername, reason);
                return false;
            }
            return true;
        }

        private bool CheckPlayerIsAlreadyConnected(ClientStructure client, string playerName, out string reason)
        {
            reason = string.Empty;
            var existingClient = ClientRetriever.GetClientByName(playerName);
            if (existingClient != null)
            {
                reason = "Username already taken";
                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.InvalidPlayername, reason);
                return false;
            }
            return true;
        }

        private bool CheckUsernameCharacters(ClientStructure client, string playerName, out string reason)
        {
            reason = string.Empty;
            if (!PlayerNameIsValid(playerName, out var validationReason))
            {
                if (validationReason.Contains("characters"))
                {
                    reason = validationReason;
                    HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.InvalidPlayername, reason);
                    return false;
                }
            }
            return true;
        }

        private bool CheckUsernameIsNotAnOwnFolder(ClientStructure client, string playerName, out string reason)
        {
            reason = string.Empty;
            if (CraftLibraryOwnFolder.PlayerNameIsForbidden(playerName))
            {
                reason = "Player name forbidden";
                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.InvalidPlayername, reason);
                return false;
            }
            return true;
        }
    }
}
