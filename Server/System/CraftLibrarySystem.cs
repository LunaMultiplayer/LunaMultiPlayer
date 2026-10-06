using ByteSizeLib;
using LmpCommon.CraftLibrary;
using LmpCommon.Enums;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Server.System
{
    public class CraftLibrarySystem
    {
        private const int WarnCraftsPerLibraryTransfer = 500;
        private static readonly object CraftLibraryLock = new object();
        public static string CraftPath => Path.Combine(ServerContext.UniverseDirectory, "Crafts");
        internal static Func<string, byte[]> ReadCraftFile = FileHandler.ReadFile;

        #region Public Methods

        /// <summary>Stores a received craft in the folder of its owner and shares it with every client</summary>
        public static void SaveCraft(ClientStructure client, CraftLibraryDataMsgData data)
        {
            var craft = data.Craft;

            if (CraftLibraryOwnFolder.IsOwnFolder(craft.FolderName))
            {
                LunaLog.Warning($"{client.PlayerName} tried to save a craft in a reserved craft folder ({craft.FolderName}). Denied.");
                return;
            }

            if (!string.Equals(client.PlayerName, craft.FolderName, StringComparison.Ordinal))
            {
                LunaLog.Warning($"{client.PlayerName} tried to save a craft in another player's folder ({craft.FolderName}). Denied.");
                return;
            }

            if (craft.NumBytes <= 0)
            {
                LunaLog.Warning($"{client.PlayerName} tried to save an empty craft ({craft.CraftName}). Denied.");
                return;
            }

            if (!CraftLibraryPath.IsValidCraftType(craft.CraftType))
            {
                LunaLog.Warning($"{client.PlayerName} tried to save a craft of the unknown type {(int)craft.CraftType}. Denied.");
                return;
            }

            if (!CraftLibraryPath.CraftNameIsValid(craft.CraftName, out var invalidReason))
            {
                LunaLog.Warning($"{client.PlayerName} tried to save a craft with an invalid name ({craft.CraftName}): {invalidReason}");
                return;
            }

            if (CraftLibraryPath.IsTransientCraftName(craft.CraftName))
                return;

            if (!CraftLibraryPath.CraftFolderIsValid(craft.CraftFolder, out invalidReason))
            {
                LunaLog.Warning($"{client.PlayerName} tried to save a craft in an invalid subfolder ({craft.CraftFolder}): {invalidReason}");
                return;
            }

            var craftFolder = CraftLibraryPath.NormalizeCraftFolder(craft.CraftFolder);
            var fullPath = CraftLibraryPath.GetCraftFilePath(CraftPath, craft.FolderName, craft.CraftType, craftFolder, craft.CraftName);

            lock (CraftLibraryLock)
            {
                FileHandler.FolderCreate(Path.GetDirectoryName(fullPath));

                LunaLog.Normal(FileHandler.FileExists(fullPath)
                    ? $"Overwriting craft {craftFolder}/{craft.CraftName} ({CraftSize(craft.NumBytes)}) from: {client.PlayerName}."
                    : $"Saving craft {craftFolder}/{craft.CraftName} ({CraftSize(craft.NumBytes)}) from: {client.PlayerName}.");

                FileHandler.WriteToFile(fullPath, craft.Data, craft.NumBytes);
                ShareCraft(craft.FolderName, craftFolder, craft.CraftName, craft.CraftType);
            }

            RemovePlayerOldestCrafts(client.PlayerName, craft.CraftType);
            CheckMaxFolders();
        }

        public static void DeleteCraft(ClientStructure client, CraftLibraryDeleteMsgData data)
        {
            if (CraftLibraryOwnFolder.IsOwnFolder(data.FolderName))
            {
                LunaLog.Warning($"{client.PlayerName} tried to delete a craft in a reserved craft folder ({data.FolderName}). Denied.");
                return;
            }

            if (!string.Equals(client.PlayerName, data.FolderName, StringComparison.Ordinal))
            {
                LunaLog.Warning($"{client.PlayerName} tried to delete a craft in another player's folder ({data.FolderName}). Denied.");
                return;
            }

            if (!CraftLibraryPath.IsValidCraftType(data.CraftType))
            {
                LunaLog.Warning($"{client.PlayerName} tried to delete a craft of the unknown type {(int)data.CraftType}. Denied.");
                return;
            }

            if (!CraftLibraryPath.CraftNameIsValid(data.CraftName, out var invalidReason))
            {
                LunaLog.Warning($"{client.PlayerName} tried to delete a craft with an invalid name ({data.CraftName}): {invalidReason}");
                return;
            }

            if (!CraftLibraryPath.CraftFolderIsValid(data.CraftFolder, out invalidReason))
            {
                LunaLog.Warning($"{client.PlayerName} tried to delete a craft in an invalid subfolder ({data.CraftFolder}): {invalidReason}");
                return;
            }

            var craftFolder = CraftLibraryPath.NormalizeCraftFolder(data.CraftFolder);
            var fullPath = CraftLibraryPath.GetCraftFilePath(CraftPath, data.FolderName, data.CraftType, craftFolder, data.CraftName);

            lock (CraftLibraryLock)
            {
                LunaLog.Normal(FileHandler.FileExists(fullPath)
                    ? $"Deleting craft {craftFolder}/{data.CraftName} from: {client.PlayerName}."
                    : $"Deleting (non existing) craft {craftFolder}/{data.CraftName} from: {client.PlayerName}.");

                FileHandler.FileDelete(fullPath);
                CraftLibraryPath.PruneEmptyDirectoriesUpTo(Path.GetDirectoryName(fullPath), CraftPath);
                BroadcastDeletion(data.FolderName, craftFolder, data.CraftName, data.CraftType);
            }
        }

        public static void SendFullLibrary(ClientStructure client)
        {
            if (Interlocked.CompareExchange(ref client.CraftLibraryTransferInProgress, 1, 0) != 0)
            {
                LunaLog.Debug($"Ignoring a craft library request of {client.PlayerName}, his library is still being sent");
                return;
            }

            try
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        var craftCount = FileHandler.FolderExists(CraftPath) ? SendLibraryTo(client) : 0;
                        if (craftCount > 0)
                            LunaLog.Normal($"Sent {craftCount} crafts of the library to: {client.PlayerName}");

                        var syncCompleteData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibrarySyncCompleteMsgData>();
                        MessageQueuer.SendToClient<CraftLibrarySrvMsg>(client, syncCompleteData);
                    }
                    catch (Exception ex)
                    {
                        LunaLog.Error($"Error sending the craft library to {client.PlayerName}: {ex}");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref client.CraftLibraryTransferInProgress, 0);
                    }
                });
            }
            catch (Exception ex)
            {
                LunaLog.Error($"Could not start the craft library transfer for {client.PlayerName}: {ex}");
                Interlocked.Exchange(ref client.CraftLibraryTransferInProgress, 0);
            }
        }

        public static void HandlePlayerDisconnected(ClientStructure client)
        {
            if (!client.Authenticated || !GeneralSettings.SettingsStore.StoreOnlyOnlinePlayersCrafts)
                return;

            RemovePlayerCraftFolder(client.PlayerName);
        }

        public static void RemovePlayerCraftFolder(string playerName)
        {
            var playerFolder = Path.Combine(CraftPath, playerName);
            if (!FileHandler.FolderExists(playerFolder)) return;

            LunaLog.Normal($"Removing the whole craft library of {playerName} (StoreOnlyOnlinePlayersCrafts)");
            lock (CraftLibraryLock)
                FileHandler.FolderDelete(playerFolder, true);
        }

        #endregion

        #region Private methods

        private static int SendLibraryTo(ClientStructure client)
        {
            var craftCount = 0;

            var playerFolders = Directory.GetDirectories(CraftPath)
                .OrderBy(d => string.Equals(new DirectoryInfo(d).Name, client.PlayerName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(d => new DirectoryInfo(d).Name, StringComparer.OrdinalIgnoreCase);

            foreach (var playerFolder in playerFolders)
            {
                var folderName = new DirectoryInfo(playerFolder).Name;
                foreach (var craftType in CraftLibraryPath.AllTypes)
                {
                    var craftTypeFolder = Path.Combine(playerFolder, craftType.ToString());
                    if (!Directory.Exists(craftTypeFolder)) continue;

                    foreach (var file in Directory.GetFiles(craftTypeFolder, "*.craft", SearchOption.AllDirectories))
                    {
                        var craftName = Path.GetFileNameWithoutExtension(file);

                        if (CraftLibraryPath.IsTransientCraftName(craftName)) continue;

                        if (SendCraftFile(client, folderName,
                                CraftLibraryPath.GetCraftFolderFromPath(file.Substring(craftTypeFolder.Length + 1)),
                                craftName, craftType, file))
                            craftCount++;
                    }
                }
            }

            if (craftCount > WarnCraftsPerLibraryTransfer)
                LunaLog.Warning($"The craft library of the server has {craftCount} crafts, sending it takes a while. " +
                                "A StoreOnlyOnlinePlayersCrafts server keeps it small");

            return craftCount;
        }

        private static void ShareCraft(string folderName, string craftFolder, string craftName, CraftType craftType)
        {
            var file = CraftLibraryPath.GetCraftFilePath(CraftPath, folderName, craftType, craftFolder, craftName);
            if (!FileHandler.FileExists(file)) return;

            var data = ReadCraftFile(file);

            if (data.Length == 0) return;

            LunaLog.Debug($"Sharing craft {craftFolder}/{craftName} ({CraftSize(data.Length)}) from {folderName} with all clients");
            MessageQueuer.SendToAllClients<CraftLibrarySrvMsg>(BuildCraftMessage(folderName, craftFolder, craftName, craftType, data));
        }

        private static bool SendCraftFile(ClientStructure client, string folderName, string craftFolder,
            string craftName, CraftType craftType, string file)
        {
            lock (CraftLibraryLock)
            {
                var data = ReadCraftFile(file);
                if (data.Length == 0)
                {
                    LunaLog.Debug($"Skipping {file}, it was deleted while the library was being sent");
                    return false;
                }

                MessageQueuer.SendToClient<CraftLibrarySrvMsg>(client, BuildCraftMessage(folderName, craftFolder, craftName, craftType, data));
                return true;
            }
        }

        private static CraftLibraryDataMsgData BuildCraftMessage(string folderName, string craftFolder,
            string craftName, CraftType craftType, byte[] data)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryDataMsgData>();
            msgData.Craft.FolderName = folderName;
            msgData.Craft.CraftFolder = craftFolder;
            msgData.Craft.CraftName = craftName;
            msgData.Craft.CraftType = craftType;
            msgData.Craft.Data = data;
            msgData.Craft.NumBytes = data.Length;
            return msgData;
        }

        private static void BroadcastDeletion(string folderName, string craftFolder, string craftName, CraftType craftType)
        {
            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<CraftLibraryDeleteMsgData>();
            msgData.FolderName = folderName;
            msgData.CraftFolder = craftFolder;
            msgData.CraftName = craftName;
            msgData.CraftType = craftType;

            MessageQueuer.SendToAllClients<CraftLibrarySrvMsg>(msgData);
        }

        private static void CheckMaxFolders()
        {
            var maxFolders = CraftSettings.SettingsStore.MaxCraftFolders;
            if (maxFolders <= 0 || !Directory.Exists(CraftPath)) return;

            while (Directory.GetDirectories(CraftPath).Length > maxFolders)
            {
                var oldestFolder = Directory.GetDirectories(CraftPath).Select(d => new DirectoryInfo(d))
                    .OrderBy(d => d.LastWriteTime)
                    .FirstOrDefault(d => ClientRetriever.GetClientByName(d.Name) == null);

                if (oldestFolder == null)
                {
                    LunaLog.Warning($"The craft library has more than the configured {maxFolders} folders and the oldest " +
                                    "ones belong to connected players, so none of them was removed");
                    return;
                }

                LunaLog.Normal($"Removing the craft folder of {oldestFolder.Name}, the server keeps at most {maxFolders} craft folders");
                lock (CraftLibraryLock)
                    FileHandler.FolderDelete(oldestFolder.FullName, true);
            }
        }

        private static void RemovePlayerOldestCrafts(string playerName, CraftType craftType)
        {
            var maxCrafts = CraftSettings.SettingsStore.MaxCraftsPerUser;
            if (maxCrafts <= 0) return;

            var craftTypeFolder = Path.Combine(CraftPath, playerName, craftType.ToString());
            if (!Directory.Exists(craftTypeFolder)) return;

            var playerFiles = new DirectoryInfo(craftTypeFolder).GetFiles("*.craft", SearchOption.AllDirectories);
            while (playerFiles.Length > maxCrafts)
            {
                var oldestCraft = playerFiles.OrderBy(f => f.LastWriteTime).First();

                lock (CraftLibraryLock)
                {
                    FileHandler.FileDelete(oldestCraft.FullName);
                    CraftLibraryPath.PruneEmptyDirectoriesUpTo(oldestCraft.DirectoryName, CraftPath);
                    BroadcastDeletion(playerName,
                        CraftLibraryPath.GetCraftFolderFromPath(oldestCraft.FullName.Substring(craftTypeFolder.Length + 1)),
                        Path.GetFileNameWithoutExtension(oldestCraft.FullName), craftType);
                }

                if (!Directory.Exists(craftTypeFolder)) return;

                playerFiles = new DirectoryInfo(craftTypeFolder).GetFiles("*.craft", SearchOption.AllDirectories);
            }
        }

        private static string CraftSize(long numBytes)
        {
            return $"{ByteSize.FromBytes(numBytes).KiloBytes}{ByteSize.KiloByteSymbol}";
        }

        #endregion
    }
}
