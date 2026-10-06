using LmpClient.Base;
using LmpCommon;
using LmpClient.Localization;
using LmpClient.Systems.SettingsSys;
using LmpClient.Utilities;
using LmpCommon.CraftLibrary;
using LmpCommon.Enums;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LmpClient.Systems.CraftLibrary
{
    /// <summary>
    /// Keeps the craft library of every player in sync, without any user interaction. Ours lives in a folder
    /// of its own, "playerName (me)", which the server never sees: it stores every player under his plain name,
    /// so a player name may never end with "(me)" or his folder would look like ours. See
    /// <see cref="CraftLibraryPath" /> and <see cref="CraftLibraryOwnFolder" /> for the name and path rules.
    /// </summary>
    public class CraftLibrarySystem : MessageSystem<CraftLibrarySystem, CraftLibraryMessageSender, CraftLibraryMessageHandler>
    {
        #region Fields

        private const long CraftCheckIntervalMs = 1000;
        private const int EditorRefreshIntervalMs = 2000;

        private static readonly TimeSpan LibrarySyncTimeout = TimeSpan.FromSeconds(60);
        private static readonly string SaveFolder = CommonUtil.CombinePaths(MainSystem.KspPath, "saves", "LunaMultiplayer");

        /// <summary>Guards the craft folders and the state guarded below</summary>
        private readonly SemaphoreSlim CraftIoSemaphore = new SemaphoreSlim(1, 1);

        private readonly ConcurrentQueue<CraftEntry> _uploadQueue = new ConcurrentQueue<CraftEntry>();
        private readonly ConcurrentQueue<CraftEntry> _deleteQueue = new ConcurrentQueue<CraftEntry>();
        private readonly ConcurrentQueue<string> _craftNotifications = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _deletedCraftsNotification = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _warnings = new ConcurrentQueue<string>();
        private int _craftsReceived;

        /// <summary>Content of the craft files we last wrote or uploaded, to know what the server has</summary>
        private readonly ConcurrentDictionary<string, byte[]> _lastLocalContent
            = new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Our own craft files we already saw, to only process the changed ones and to detect deletions</summary>
        private readonly ConcurrentDictionary<string, CraftFile> _ownCraftFiles
            = new ConcurrentDictionary<string, CraftFile>(StringComparer.OrdinalIgnoreCase);

        private int _scanning;
        private int _resetSessionRequested;
        private DateTime _syncRequestTimeUtc = DateTime.MinValue;
        private bool _librarySyncRequested;
        private bool _librarySynced;
        private bool _syncInProgress;
        private bool _syncTimedOut;

        //Everything below is only read or written while holding CraftIoSemaphore

        /// <summary>The folders and the crafts outside the player folders that existed when we connected</summary>
        private readonly HashSet<string> _rootFoldersAtStart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _preExistingOutsideFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The players whose folder we saw, so we never remove a folder of a real player</summary>
        private readonly HashSet<string> _knownPlayerFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Paths already warned about, so the player isn't spammed every second</summary>
        private readonly HashSet<string> _warnedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The crafts the server sent us during the current library transfer</summary>
        private readonly HashSet<LibraryCraft> _serverLibrary = new HashSet<LibraryCraft>();

        /// <summary>
        /// The crafts of ours that we have on disk this session. A craft the server still has that is not in
        /// here and whose type folder existed at connect was deleted while we were offline, so its deletion
        /// is sent to the server instead of restoring it
        /// </summary>
        private readonly HashSet<LibraryCraft> _ownCraftsSeen = new HashSet<LibraryCraft>();
        private readonly HashSet<CraftType> _ownFolderExisted = new HashSet<CraftType>();

        private bool _rootLayoutCaptured;
        private bool _ownCraftsCaptured;
        private bool _offlineDeletionsPropagated;
        private int _libraryCraftsReceived;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(CraftLibrarySystem);

        /// <summary>Storing a craft does a lot of disk IO, so it must not run on the Unity thread</summary>
        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();

            //Ask for the whole library on the first tick: the marker tells us what the server is missing
            _librarySyncRequested = true;
            _librarySynced = false;

            SetupRoutine(new RoutineDefinition((int)CraftCheckIntervalMs, RoutineExecution.Update, ProcessCraftFolderChanges));
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, NotifyCraftChanges));
            SetupRoutine(new RoutineDefinition(EditorRefreshIntervalMs, RoutineExecution.Update, RefreshEditor));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            while (_uploadQueue.TryDequeue(out _)) { }
            while (_deleteQueue.TryDequeue(out _)) { }
            while (_craftNotifications.TryDequeue(out _)) { }
            while (_deletedCraftsNotification.TryDequeue(out _)) { }
            while (_warnings.TryDequeue(out _)) { }
            CraftLibraryEditorRefresh.ClearPending();
            _lastLocalContent.Clear();
            _ownCraftFiles.Clear();
            Interlocked.Exchange(ref _craftsReceived, 0);
            _librarySyncRequested = false;
            _librarySynced = false;
            _syncRequestTimeUtc = DateTime.MinValue;

            //The state guarded by the semaphore is NOT cleared here: this runs on the thread that
            //disconnected us and waiting for the semaphore would freeze the game for the duration of a
            //library transfer. The next scan clears it instead. _scanning is not reset either, the
            //in-flight task resets it when it finishes
            Interlocked.Exchange(ref _resetSessionRequested, 1);
        }

        #endregion
        #region Message handling

        /// <summary>
        /// Stores a craft received from the server in the folder of its owner. Crafts of other players are
        /// always written (unless we already have identical content); ours only when we don't have them
        /// </summary>
        public void StoreCraft(CraftEntry craft)
        {
            if (!Enabled || !IsValidLibraryCraft(craft))
                return;
            if (CraftLibraryPath.IsTransientCraftName(craft.CraftName))
                return;

            var localFolderName = LocalFolderNameOf(craft.FolderName);
            var ownCraft = IsOwnFolder(localFolderName);
            var directory = GetCraftDirectory(localFolderName, craft.CraftType, craft.CraftFolder);
            if (directory == null)
            {
                LunaLog.LogError($"[LMP]: Ignoring craft {craft.FolderName}/{craft.CraftName}, it has an invalid subfolder ({craft.CraftFolder})");
                return;
            }

            var path = CommonUtil.CombinePaths(directory, $"{craft.CraftName}.craft");
            var content = craft.CraftData;

            CraftIoSemaphore.Wait();
            try
            {
                _knownPlayerFolders.Add(localFolderName);

                if (_syncInProgress)
                    _serverLibrary.Add(new LibraryCraft(craft.FolderName, craft.CraftType, craft.CraftFolder, craft.CraftName));

                if (File.Exists(path))
                {
                    if (ContentEquals(ReadCraft(path), content, craft.CraftNumBytes))
                    {
                        if (ownCraft) _lastLocalContent[path] = CopyOfCraft(content, craft.CraftNumBytes);
                        return;
                    }

                    //Our own local version wins over the copy the server has, and the library pass uploads
                    //it because the recorded content no longer matches the file
                    if (ownCraft) return;
                }

                var folderExisted = Directory.Exists(directory);
                Directory.CreateDirectory(directory);
                WriteCraft(path, content, craft.CraftNumBytes);
                CraftLibraryEditorRefresh.QueueRefresh(craft.CraftType, !folderExisted);

                if (ownCraft)
                {
                    _lastLocalContent[path] = CopyOfCraft(content, craft.CraftNumBytes);

                    //The folder scan skips a craft whose stamp is unchanged. Recording it keeps the scan from
                    //adding a craft that is just arriving to _ownCraftsSeen, which would make the library
                    //pass believe the player has a craft that was actually deleted while he was offline
                    var written = new FileInfo(path);
                    _ownCraftFiles[path] = new CraftFile
                    {
                        LastWriteTimeUtc = written.LastWriteTimeUtc, Length = written.Length,
                        Craft = new LibraryCraft(craft.FolderName, craft.CraftType, craft.CraftFolder, craft.CraftName)
                    };
                }

                if (_syncInProgress)
                    _libraryCraftsReceived++;
                else
                    Interlocked.Increment(ref _craftsReceived);
            }
            finally
            {
                CraftIoSemaphore.Release();
            }
        }

        /// <summary>Removes a deleted craft. Ours only when the local content is what we uploaded</summary>
        public void DeleteLocalCraft(CraftEntry craft)
        {
            if (!Enabled || !IsValidLibraryCraft(craft))
                return;

            var localFolderName = LocalFolderNameOf(craft.FolderName);
            var ownCraft = IsOwnFolder(localFolderName);
            var directory = GetCraftDirectory(localFolderName, craft.CraftType, craft.CraftFolder);
            if (directory == null)
                return;

            var path = CommonUtil.CombinePaths(directory, $"{craft.CraftName}.craft");

            CraftIoSemaphore.Wait();
            try
            {
                if (ownCraft && File.Exists(path))
                {
                    var localContent = ReadCraft(path);
                    if (!_lastLocalContent.TryGetValue(path, out var uploadedContent)
                        || !ContentEquals(localContent, uploadedContent, uploadedContent.Length))
                        return;
                }

                ForgetCraftFile(path);
                File.Delete(path);
                TryDeleteSidecar(path);

                //A folder deletion has to propagate
                CraftLibraryPath.PruneEmptyDirectoriesUpTo(directory, GetRootFolder(craft.CraftType));
                CraftLibraryEditorRefresh.QueueRefresh(craft.CraftType, !Directory.Exists(directory));
                _knownPlayerFolders.Add(localFolderName);
                _deletedCraftsNotification.Enqueue($"{craft.FolderName}/{craft.CraftName}");
            }
            finally
            {
                CraftIoSemaphore.Release();
            }
        }

        /// <summary>The server finished sending the library: the local list is mirrored with it</summary>
        public void OnLibrarySyncComplete()
        {
            if (!Enabled)
                return;

            CraftIoSemaphore.Wait();
            try
            {
                //A transfer that timed out still gets its pass, so the late marker corrects our state
                if (!_syncInProgress && !_syncTimedOut) return;

                _syncInProgress = false;
                _syncTimedOut = false;
                _librarySynced = true;
                RunLibraryMirrorPass();
                NotifyLibraryCraftsReceived();
            }
            finally
            {
                _serverLibrary.Clear();
                CraftIoSemaphore.Release();
            }
        }

        #endregion

        #region Folder watching

        /// <summary>Uploads the local changes, enforces the layout rules and requests the library</summary>
        private void ProcessCraftFolderChanges()
        {
            //Skip if the previous check is still running
            if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0) return;

            _ = Task.Run(() =>
            {
                try
                {
                    CraftIoSemaphore.Wait();
                    try
                    {
                        //State of a previous session
                        if (Interlocked.Exchange(ref _resetSessionRequested, 0) == 1)
                            ResetSessionState();

                        RenameOwnFolder();
                        CaptureRootLayoutIfNeeded();
                        MoveRootCraftsIntoPlayerFolder();
                        CaptureOwnCraftsIfNeeded();
                        EnforceRootLayout();
                        ScanAndQueueOwnCraftChanges();
                        CheckLibrarySyncTimeout();
                        AnnouncePendingUploads();
                        DrainQueues();
                        RequestLibrarySyncIfNeeded();
                    }
                    finally
                    {
                        CraftIoSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    LunaLog.LogError($"[LMP]: Error processing craft folder changes: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _scanning, 0);
                }
            });
        }

        /// <summary>
        /// Renames our own folder to the one of the player name we are connected with, so a player who renamed
        /// himself keeps his crafts. A folder ending in "(me)" is ours, and a folder named like the player is
        /// the own folder of every LMP before the suffix existed
        /// </summary>
        private void RenameOwnFolder()
        {
            var playerName = OwnPlayerName();
            var ownFolderName = OwnFolderName();
            if (playerName == null) return;

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var rootFolder = GetRootFolder(craftType);

                foreach (var dir in SubFolders(rootFolder))
                {
                    var folderName = new DirectoryInfo(dir).Name;
                    if (string.Equals(folderName, ownFolderName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!IsOwnFolder(folderName) && !string.Equals(folderName, playerName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = CommonUtil.CombinePaths(rootFolder, ownFolderName);

                    //Both folders are ours when we renamed back and forth, the one we are using stays
                    if (Directory.Exists(target))
                    {
                        LunaLog.Log($"[LMP]: Kept the craft folder {folderName}, the folder {ownFolderName} is already there");
                        continue;
                    }

                    Directory.Move(dir, target);
                    CraftLibraryEditorRefresh.QueueRefresh(craftType, true);
                    LunaLog.Log($"[LMP]: Renamed the craft folder {folderName} to {ownFolderName} as the player name changed");
                }
            }
        }

        /// <summary>
        /// Remembers the folders and crafts outside the player folders that existed when we connected, so
        /// only new violations are reverted while the library arrives
        /// </summary>
        private void CaptureRootLayoutIfNeeded()
        {
            if (_rootLayoutCaptured || OwnFolderName() == null) return;

            _rootLayoutCaptured = true;

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var rootFolder = GetRootFolder(craftType);
                foreach (var dir in SubFolders(rootFolder))
                    _rootFoldersAtStart.Add(new DirectoryInfo(dir).Name);

                foreach (var file in CraftFiles(rootFolder))
                    if (IsOutsidePlayerFolders(file, rootFolder))
                        _preExistingOutsideFiles.Add(file);
            }
        }

        /// <summary>Moves the crafts directly in the root of the ships folders into our own player folder</summary>
        private void MoveRootCraftsIntoPlayerFolder()
        {
            var ownFolderName = OwnFolderName();
            if (ownFolderName == null) return;

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var rootFolder = GetRootFolder(craftType);
                var playerFolder = GetPlayerFolder(ownFolderName, craftType);
                var playerFolderExisted = Directory.Exists(playerFolder);

                foreach (var file in CraftFiles(rootFolder, false))
                {
                    var craftName = Path.GetFileNameWithoutExtension(file);
                    var targetPath = CommonUtil.CombinePaths(playerFolder, $"{craftName}.craft");

                    try
                    {
                        Directory.CreateDirectory(playerFolder);
                        File.Copy(file, targetPath, true);
                        File.Delete(file);
                        MoveSidecar(file, targetPath);

                        LunaLog.Log($"[LMP]: Catalogued craft {craftName} in the {ownFolderName} folder");
                    }
                    catch (Exception ex)
                    {
                        LunaLog.LogError($"[LMP]: Error moving craft {file} into the player folder: {ex.Message}");
                    }

                    CraftLibraryEditorRefresh.QueueRefresh(craftType, !playerFolderExisted);
                }
            }
        }

        private static void MoveSidecar(string sourceCraftPath, string targetCraftPath)
        {
            var loadMeta = $"{sourceCraftPath}.loadmeta";
            if (!File.Exists(loadMeta)) return;

            var targetMeta = $"{targetCraftPath}.loadmeta";
            if (File.Exists(targetMeta))
                File.Delete(targetMeta);

            File.Move(loadMeta, targetMeta);
        }
        /// <summary>
        /// Remembers the own crafts that existed right after connecting. A craft the server still has that we
        /// never saw this session was deleted offline, a craft type whose folder did not exist yet means a
        /// fresh install and its server crafts are restored
        /// </summary>
        private void CaptureOwnCraftsIfNeeded()
        {
            var playerName = OwnPlayerName();
            var ownFolderName = OwnFolderName();
            if (_ownCraftsCaptured || playerName == null) return;

            _ownCraftsCaptured = true;

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var playerFolder = GetPlayerFolder(ownFolderName, craftType);
                if (!Directory.Exists(playerFolder)) continue;

                _ownFolderExisted.Add(craftType);
                foreach (var file in CraftFiles(playerFolder))
                    _ownCraftsSeen.Add(LibraryCraftFromPath(playerName, craftType, playerFolder, file));
            }
        }

        /// <summary>Removes the folders and vehicles that appeared outside the player folders</summary>
        private void EnforceRootLayout()
        {
            if (OwnPlayerName() == null) return;

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var rootFolder = GetRootFolder(craftType);

                foreach (var dir in SubFolders(rootFolder))
                {
                    var folderName = new DirectoryInfo(dir).Name;
                    if (IsOwnFolder(folderName) || _rootFoldersAtStart.Contains(folderName)
                        || _knownPlayerFolders.Contains(folderName))
                        continue;

                    try
                    {
                        Directory.Delete(dir, true);
                        CraftLibraryEditorRefresh.QueueRefresh(craftType, true);
                        WarnOnce(dir, LocalizationContainer.ScreenText.CraftOutsideFolderWarning);
                        LunaLog.Log($"[LMP]: Removed craft folder '{dir}' as crafts may only be saved inside the player folder");
                    }
                    catch (Exception ex)
                    {
                        LunaLog.LogError($"[LMP]: Error removing craft folder {dir}: {ex.Message}");
                    }
                }

                //Crafts in the root itself are handled by the migration instead
                foreach (var file in CraftFiles(rootFolder))
                {
                    if (!IsNestedBelowRootFolder(file, rootFolder)) continue;
                    if (!IsOutsidePlayerFolders(file, rootFolder)) continue;
                    if (_preExistingOutsideFiles.Contains(file)) continue;

                    File.Delete(file);
                    TryDeleteSidecar(file);
                    CraftLibraryEditorRefresh.QueueRefresh(craftType, false);
                    WarnOnce(file, LocalizationContainer.ScreenText.CraftOutsideFolderWarning);
                }
            }
        }

        /// <summary>True when a craft file is inside a subfolder of the craft type root</summary>
        private static bool IsNestedBelowRootFolder(string file, string rootFolder)
        {
            var relative = file.Substring(rootFolder.Length + 1);
            return relative.IndexOf('\\') >= 0 || relative.IndexOf('/') >= 0;
        }

        /// <summary>
        /// True when a craft file is in neither our own folder nor the folder of a player we know (the root
        /// of the ships folder, or a folder that belongs to nobody)
        /// </summary>
        private bool IsOutsidePlayerFolders(string file, string rootFolder)
        {
            var relative = file.Substring(rootFolder.Length + 1);
            var separatorIndex = relative.IndexOf('\\');
            if (separatorIndex < 0)
                separatorIndex = relative.IndexOf('/');

            var topLevel = separatorIndex < 0 ? relative : relative.Substring(0, separatorIndex);
            return !IsOwnFolder(topLevel) && !_knownPlayerFolders.Contains(topLevel);
        }

        /// <summary>Scans our own craft folders and queues every new or changed craft</summary>
        private void ScanAndQueueOwnCraftChanges()
        {
            var playerName = OwnPlayerName();
            var ownFolderName = OwnFolderName();
            if (playerName == null) return;

            var seenCrafts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var playerFolder = GetPlayerFolder(ownFolderName, craftType);

                foreach (var file in CraftFiles(playerFolder))
                {
                    seenCrafts.Add(file);

                    if (CraftLibraryPath.IsTransientCraftName(Path.GetFileNameWithoutExtension(file)))
                        continue;

                    var craft = LibraryCraftFromPath(playerName, craftType, playerFolder, file);
                    var info = new FileInfo(file);

                    //A craft the server would reject would be queued on every scan forever
                    if (!IsValidLibraryCraft(craft))
                    {
                        WarnOnce(file, string.Format(LocalizationContainer.ScreenText.CraftNotShared, InvalidCraftReason(craft)));
                        continue;
                    }

                    if (_ownCraftFiles.TryGetValue(file, out var known)
                        && known.LastWriteTimeUtc == info.LastWriteTimeUtc && known.Length == info.Length)
                        continue;

                    _ownCraftFiles[file] = new CraftFile
                    {
                        LastWriteTimeUtc = info.LastWriteTimeUtc, Length = info.Length, Craft = craft
                    };
                    _ownCraftsSeen.Add(craft);

                    var content = ReadCraft(file);

                    //We already know this content, eg: it was just restored from the server
                    if (_lastLocalContent.TryGetValue(file, out var lastContent) && ContentEquals(lastContent, content, content.Length))
                        continue;

                    //The library pass uploads what the server is missing and what it has an older version of
                    if (!_librarySynced) continue;

                    QueueUpload(craft, content);
                    _lastLocalContent[file] = content;
                }
            }

            //Crafts deleted locally
            foreach (var trackedCraft in _ownCraftFiles.Keys.ToList())
            {
                if (seenCrafts.Contains(trackedCraft)) continue;

                if (_ownCraftFiles.TryRemove(trackedCraft, out var deleted))
                {
                    _lastLocalContent.TryRemove(trackedCraft, out _);
                    _ownCraftsSeen.Remove(deleted.Craft);
                    _deleteQueue.Enqueue(deleted.Craft.ToEntry());
                }

                TryDeleteSidecar(trackedCraft);
            }
        }

        private void QueueUpload(LibraryCraft craft, byte[] content)
        {
            _uploadQueue.Enqueue(new CraftEntry
            {
                FolderName = craft.FolderName,
                CraftType = craft.CraftType,
                CraftFolder = craft.CraftFolder,
                CraftName = craft.CraftName,
                CraftNumBytes = content.Length,
                CraftData = content
            });
        }

        /// <summary>Sends the queued uploads and deletions, in order, before the sync request</summary>
        private void DrainQueues()
        {
            var uploaded = 0;
            while (_uploadQueue.TryDequeue(out var craft))
            {
                MessageSender.SendCraftMsg(craft);
                uploaded++;
            }

            if (uploaded > 0)
                _craftNotifications.Enqueue(string.Format(LocalizationContainer.ScreenText.CraftsUploaded, uploaded));

            while (_deleteQueue.TryDequeue(out var craft))
                MessageSender.SendCraftDeleteMsg(craft);
        }

        private void AnnouncePendingUploads()
        {
            var pending = _uploadQueue.Count;
            if (pending <= 0) return;

            _craftNotifications.Enqueue(string.Format(LocalizationContainer.ScreenText.CraftsUploading, pending));
        }

        /// <summary>Asks for the whole library once all our local changes were sent, and waits for its marker</summary>
        private void RequestLibrarySyncIfNeeded()
        {
            if (!_librarySyncRequested || _syncInProgress || !_uploadQueue.IsEmpty || !_deleteQueue.IsEmpty) return;

            _librarySyncRequested = false;
            _syncInProgress = true;
            _syncTimedOut = false;
            _syncRequestTimeUtc = DateTime.UtcNow;
            MessageSender.SendSyncRequestMsg();
        }

        /// <summary>
        /// A transfer that takes longer than the timeout is not a failed one: we start uploading the local
        /// changes so we don't sit idle, and the marker that arrives later still runs the library pass
        /// </summary>
        private void CheckLibrarySyncTimeout()
        {
            if (!_syncInProgress || DateTime.UtcNow - _syncRequestTimeUtc <= LibrarySyncTimeout) return;

            _syncTimedOut = true;
            _librarySynced = true;
            _ownCraftFiles.Clear();

            LunaLog.LogWarning("[LMP]: The craft library transfer is taking longer than a minute, uploading the local " +
                               "changes meanwhile and still waiting for the library");
        }
        /// <summary>
        /// Removes what the server doesn't have and uploads what it is missing, offline deletions included.
        /// Our own crafts are uploaded when the server has no copy or when the file no longer matches the
        /// content we last received or sent, so an edit made while the library was coming in is not lost
        /// </summary>
        private void RunLibraryMirrorPass()
        {
            var playerName = OwnPlayerName();
            if (playerName == null) return;

            var ownFolderName = OwnFolderName();

            //Propagated only once per session, a later pass must not delete crafts created after we connected
            if (!_offlineDeletionsPropagated)
            {
                _offlineDeletionsPropagated = true;

                foreach (var craft in _serverLibrary.Where(c => IsOwnFolder(LocalFolderNameOf(c.FolderName))))
                {
                    if (!_ownFolderExisted.Contains(craft.CraftType)) continue;
                    if (_ownCraftsSeen.Contains(craft)) continue;

                    //The transfer restored the deleted craft, remove it again
                    var directory = GetCraftDirectory(LocalFolderNameOf(craft.FolderName), craft.CraftType, craft.CraftFolder);
                    if (directory != null)
                    {
                        var restoredPath = CommonUtil.CombinePaths(directory, $"{craft.CraftName}.craft");
                        File.Delete(restoredPath);
                        TryDeleteSidecar(restoredPath);
                        CraftLibraryPath.PruneEmptyDirectoriesUpTo(directory, GetRootFolder(craft.CraftType));
                        CraftLibraryEditorRefresh.QueueRefresh(craft.CraftType, !Directory.Exists(directory));
                        ForgetCraftFile(restoredPath);
                    }

                    _deleteQueue.Enqueue(craft.ToEntry());

                    LunaLog.Log($"[LMP]: Craft {craft.CraftFolder}/{craft.CraftName} was deleted while we were offline, removing it from the server");
                }
            }

            var libraryPlayers = new HashSet<string>(_serverLibrary.Select(c => LocalFolderNameOf(c.FolderName)), StringComparer.OrdinalIgnoreCase);

            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                var rootFolder = GetRootFolder(craftType);

                foreach (var dir in SubFolders(rootFolder))
                {
                    var folderName = new DirectoryInfo(dir).Name;
                    if (IsOwnFolder(folderName)) continue;

                    //No crafts from this player anymore
                    if (!libraryPlayers.Contains(folderName))
                    {
                        RemoveStaleCraftFolder(dir, rootFolder);
                        CraftLibraryEditorRefresh.QueueRefresh(craftType, true);
                        continue;
                    }

                    //Known player folder
                    foreach (var file in CraftFiles(dir))
                    {
                        if (_serverLibrary.Contains(LibraryCraftFromPath(folderName, craftType, dir, file))) continue;

                        File.Delete(file);
                        TryDeleteSidecar(file);
                        CraftLibraryPath.PruneEmptyDirectoriesUpTo(Path.GetDirectoryName(file), rootFolder);
                        CraftLibraryEditorRefresh.QueueRefresh(craftType, !Directory.Exists(Path.GetDirectoryName(file)));
                        ForgetCraftFile(file);
                        LunaLog.Log($"[LMP]: Removed stale craft '{file}' as the server doesn't have it");
                    }
                }

                //Our own crafts
                var playerFolder = GetPlayerFolder(ownFolderName, craftType);

                foreach (var file in CraftFiles(playerFolder))
                {
                    if (CraftLibraryPath.IsTransientCraftName(Path.GetFileNameWithoutExtension(file)))
                        continue;

                    var craft = LibraryCraftFromPath(playerName, craftType, playerFolder, file);
                    var content = ReadCraft(file);

                    //A craft the server would reject, or it would be retried on every pass
                    if (!IsValidLibraryCraft(craft))
                    {
                        WarnOnce(file, string.Format(LocalizationContainer.ScreenText.CraftNotShared, InvalidCraftReason(craft)));
                        continue;
                    }

                    //The server has this craft and the file is what it sent us, nothing to do
                    if (_serverLibrary.Contains(craft) && _lastLocalContent.TryGetValue(file, out var lastContent)
                        && ContentEquals(lastContent, content, content.Length))
                        continue;

                    QueueUpload(craft, content);
                    _lastLocalContent[file] = content;
                }
            }

            //The leftovers are gone, so anything showing up later is a new violation
            _rootFoldersAtStart.Clear();
            _preExistingOutsideFiles.Clear();
        }

        private void ResetSessionState()
        {
            _syncInProgress = false;
            _syncTimedOut = false;
            _libraryCraftsReceived = 0;
            _rootLayoutCaptured = false;
            _rootFoldersAtStart.Clear();
            _preExistingOutsideFiles.Clear();
            _knownPlayerFolders.Clear();
            _warnedPaths.Clear();
            _serverLibrary.Clear();
            _ownCraftsSeen.Clear();
            _ownFolderExisted.Clear();
            _ownCraftsCaptured = false;
            _offlineDeletionsPropagated = false;
        }

        #endregion

        #region Filesystem helpers

        private static string[] CraftFiles(string directory, bool recursive = true)
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "*.craft", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
        }

        private static string[] SubFolders(string directory)
        {
            return Directory.Exists(directory) ? Directory.GetDirectories(directory) : Array.Empty<string>();
        }

        private static void TryDeleteSidecar(string craftPath)
        {
            var sidecar = $"{craftPath}.loadmeta";
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }

        /// <summary>Removes the craft files of a stale folder, and the folder itself when it ends up empty</summary>
        private void RemoveStaleCraftFolder(string directory, string rootFolder)
        {
            foreach (var file in CraftFiles(directory))
            {
                File.Delete(file);
                TryDeleteSidecar(file);
                ForgetCraftFile(file);
                LunaLog.Log($"[LMP]: Removed stale craft '{file}' as the server doesn't have it");
            }

            CraftLibraryPath.PruneEmptyDirectoriesUpTo(directory, rootFolder);
        }

        #endregion

        #region Notifications

        private void NotifyCraftChanges()
        {
            //Only shown in the KSC and the editors, the sync itself runs in every scene
            var showNotifications = HighLogic.LoadedScene == GameScenes.SPACECENTER || HighLogic.LoadedSceneIsEditor;

            while (_craftNotifications.TryDequeue(out var notification))
                if (showNotifications)
                    LunaScreenMsg.PostScreenMessage(notification, 5f, ScreenMessageStyle.UPPER_CENTER);

            //Everything that arrived since the last message, so a whole transfer is one line
            var received = Interlocked.Exchange(ref _craftsReceived, 0);
            if (received > 0 && showNotifications)
                LunaScreenMsg.PostScreenMessage(string.Format(LocalizationContainer.ScreenText.CraftsReceived, received),
                    5f, ScreenMessageStyle.UPPER_CENTER);

            while (_deletedCraftsNotification.TryDequeue(out var deletedCraft))
                if (showNotifications)
                    LunaScreenMsg.PostScreenMessage($"({deletedCraft}) {LocalizationContainer.ScreenText.CraftDeleted}", 5f, ScreenMessageStyle.UPPER_CENTER);

            while (_warnings.TryDequeue(out var warning))
                LunaScreenMsg.PostScreenMessage(warning, 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        private void RefreshEditor()
        {
            CraftLibraryEditorRefresh.FlushPending();
        }

        /// <summary>Shows a warning on the screen once per path, naming the craft</summary>
        private void WarnOnce(string path, string warning)
        {
            if (!_warnedPaths.Add(path)) return;

            _warnings.Enqueue($"{warning} ({Path.GetFileNameWithoutExtension(path)})");
            LunaLog.LogWarning($"[LMP]: {warning} ({path})");
        }

        /// <summary>One line with the count, instead of a screen message per craft</summary>
        private void NotifyLibraryCraftsReceived()
        {
            if (_libraryCraftsReceived <= 0) return;

            LunaLog.Log($"[LMP]: Craft library synchronized, {_libraryCraftsReceived} crafts received");
            _warnings.Enqueue(string.Format(LocalizationContainer.ScreenText.CraftLibrarySynced, _libraryCraftsReceived));
            _libraryCraftsReceived = 0;
        }

        #endregion

        #region Layout helpers

        /// <summary>Our own player name, or null when we are not connected (nothing is synced then)</summary>
        private static string OwnPlayerName()
        {
            var playerName = SettingsSystem.CurrentSettings.PlayerName;
            return string.IsNullOrEmpty(playerName) ? null : playerName;
        }

        /// <summary>The folder of the connected player, it ends in "(me)" so it is ours</summary>
        private static string OwnFolderName()
        {
            var playerName = OwnPlayerName();
            return playerName == null ? null : CraftLibraryOwnFolder.NameFor(playerName);
        }

        /// <summary>True when a folder on disk holds the crafts of the local player</summary>
        private static bool IsOwnFolder(string folderName)
        {
            return CraftLibraryOwnFolder.IsOwnFolder(folderName);
        }

        /// <summary>
        /// The local name of the folder the server keeps in the given name: ours carries the suffix, every
        /// other player is named after him. The only place the player name is compared
        /// </summary>
        private static string LocalFolderNameOf(string protocolFolderName)
        {
            var playerName = OwnPlayerName();
            if (playerName == null) return protocolFolderName;

            return string.Equals(protocolFolderName, playerName, StringComparison.OrdinalIgnoreCase)
                ? OwnFolderName()
                : protocolFolderName;
        }

        private static string GetRootFolder(CraftType craftType)
        {
            switch (craftType)
            {
                case CraftType.Vab:
                    return CommonUtil.CombinePaths(SaveFolder, "Ships", "VAB");
                case CraftType.Sph:
                    return CommonUtil.CombinePaths(SaveFolder, "Ships", "SPH");
                case CraftType.Subassembly:
                    return CommonUtil.CombinePaths(SaveFolder, "Subassemblies");
                default:
                    throw new ArgumentOutOfRangeException(nameof(craftType));
            }
        }

        /// <summary>Folder holding the crafts of a player inside the LMP save, shown as a subdirectory in the craft dialogs</summary>
        private static string GetPlayerFolder(string folderName, CraftType craftType)
        {
            return CommonUtil.CombinePaths(GetRootFolder(craftType), folderName);
        }

        /// <summary>The full directory of a craft, or null when the subfolder is not a safe relative path</summary>
        private static string GetCraftDirectory(string folderName, CraftType craftType, string craftFolder)
        {
            var playerFolder = GetPlayerFolder(folderName, craftType);
            if (string.IsNullOrEmpty(craftFolder)) return playerFolder;

            return CraftLibraryPath.CraftFolderIsValid(craftFolder)
                ? CommonUtil.CombinePaths(new[] { playerFolder }.Concat(craftFolder.Split('/', '\\')).ToArray())
                : null;
        }

        /// <summary>A craft we can actually store. The player folder is a path too, so it is validated as well</summary>
        private static bool IsValidLibraryCraft(CraftEntry craft)
        {
            return CraftLibraryPath.CraftNameIsValid(craft.CraftName)
                   && CraftLibraryPath.CraftFolderIsValid(craft.CraftFolder)
                   && CraftLibraryPath.IsValidCraftType(craft.CraftType)
                   && CraftLibraryPath.PlayerFolderIsValid(craft.FolderName)
                   && !IsOwnFolder(craft.FolderName);
        }

        private static bool IsValidLibraryCraft(LibraryCraft craft)
        {
            return CraftLibraryPath.CraftNameIsValid(craft.CraftName)
                   && CraftLibraryPath.CraftFolderIsValid(craft.CraftFolder)
                   && CraftLibraryPath.IsValidCraftType(craft.CraftType)
                   && CraftLibraryPath.PlayerFolderIsValid(craft.FolderName)
                   && !IsOwnFolder(craft.FolderName);
        }

        private static string InvalidCraftReason(LibraryCraft craft)
        {
            if (!CraftLibraryPath.CraftNameIsValid(craft.CraftName, out var reason)) return reason;
            if (!CraftLibraryPath.CraftFolderIsValid(craft.CraftFolder, out reason)) return reason;
            if (IsOwnFolder(craft.FolderName)) return "the folder name is reserved for the own craft folder";

            return "unknown craft type";
        }

        private static byte[] ReadCraft(string path)
        {
            return File.ReadAllBytes(path);
        }

        private static void WriteCraft(string path, byte[] data, int length)
        {
            TryDeleteSidecar(path);

            //The buffer comes from a message pool and can be longer than the craft
            using (var stream = File.Create(path))
                stream.Write(data, 0, length);
        }

        /// <summary>A private copy, the pooled buffer is reused by the next craft message</summary>
        private static byte[] CopyOfCraft(byte[] buffer, int length)
        {
            var copy = new byte[length];
            Array.Copy(buffer, 0, copy, 0, length);
            return copy;
        }

        /// <summary>Compares our content with the first bytes of a (possibly pooled) message buffer</summary>
        private static bool ContentEquals(byte[] content, byte[] buffer, int bufferLength)
        {
            if (content == null || buffer == null || content.Length != bufferLength || bufferLength > buffer.Length) return false;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] != buffer[i]) return false;
            }
            return true;
        }

        private void ForgetCraftFile(string path)
        {
            _ownCraftFiles.TryRemove(path, out _);
            _lastLocalContent.TryRemove(path, out _);
        }

        private static LibraryCraft LibraryCraftFromPath(string folderName, CraftType craftType, string playerFolder, string file)
        {
            return new LibraryCraft(folderName, craftType,
                CraftLibraryPath.GetCraftFolderFromPath(file.Substring(playerFolder.Length + 1)),
                Path.GetFileNameWithoutExtension(file));
        }


        #endregion

        /// <summary>
        /// Identity of a craft inside the server library, used to tell which crafts the server has
        /// without building and re-parsing a string key
        /// </summary>
        private readonly struct LibraryCraft : IEquatable<LibraryCraft>
        {
            public LibraryCraft(string folderName, CraftType craftType, string craftFolder, string craftName)
            {
                FolderName = folderName ?? string.Empty;
                CraftType = craftType;
                CraftFolder = CraftLibraryPath.NormalizeCraftFolder(craftFolder);
                CraftName = craftName ?? string.Empty;
            }

            public string FolderName { get; }
            public CraftType CraftType { get; }
            public string CraftFolder { get; }
            public string CraftName { get; }

            public bool Equals(LibraryCraft other)
            {
                return CraftType == other.CraftType
                       && string.Equals(FolderName, other.FolderName, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(CraftFolder, other.CraftFolder, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(CraftName, other.CraftName, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is LibraryCraft other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(FolderName);
                    hash = (hash * 397) ^ (int)CraftType;
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(CraftFolder);
                    hash = (hash * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(CraftName);
                    return hash;
                }
            }

            public CraftEntry ToEntry()
            {
                return new CraftEntry
                {
                    FolderName = FolderName,
                    CraftType = CraftType,
                    CraftFolder = CraftFolder,
                    CraftName = CraftName
                };
            }
        }

        /// <summary>Write time and length of a craft file of ours, the cheap change detection</summary>
        private class CraftFile
        {
            public DateTime LastWriteTimeUtc { get; set; }
            public long Length { get; set; }
            public LibraryCraft Craft { get; set; }
        }
    }
}
