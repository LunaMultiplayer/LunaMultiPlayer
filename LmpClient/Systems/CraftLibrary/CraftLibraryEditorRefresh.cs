using KSP.UI.Screens;
using LmpCommon;
using LmpCommon.CraftLibrary;
using LmpCommon.Enums;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;

namespace LmpClient.Systems.CraftLibrary
{
    /// <summary>
    /// Makes the vehicle editor show the craft folders the library changed while the editor is already
    /// open.
    /// </summary>
    internal static class CraftLibraryEditorRefresh
    {
        #region Fields

        private static readonly FieldInfo CraftBrowserDialogField = typeof(EditorLogic).GetField("craftBrowserDialog",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo ContentAreaField = typeof(DirectoryController).GetField("contentArea",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo DirectoryGroupsField = typeof(DirectoryController).GetField("directoryActionGroups",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo BuildStockDirectoryMethod = typeof(DirectoryController).GetMethod("BuildStockDirectoryUI",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo BuildSteamDirectoryMethod = typeof(DirectoryController).GetMethod("BuildSteamDirectoryUI",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly ConcurrentDictionary<CraftType, byte> PendingCraftTypes
            = new ConcurrentDictionary<CraftType, byte>();

        private static readonly ConcurrentDictionary<CraftType, byte> PendingFolderTypes
            = new ConcurrentDictionary<CraftType, byte>();

        private const int SettleTimeMs = 500;

        private const string StaleNodeName = "LMPStale";

        private static long _lastChangeTimeTicks;

        private static bool _warnedMissingTreeApi;

        #endregion

        #region Public methods

        /// <summary>
        /// Remembers that the craft folders of a type changed. Called from the threads doing the IO, so
        /// this only queues the work for <see cref="FlushPending" />
        /// </summary>
        public static void QueueRefresh(CraftType craftType, bool folderLayoutChanged)
        {
            PendingCraftTypes[craftType] = 0;
            if (folderLayoutChanged)
                PendingFolderTypes[craftType] = 0;

            Interlocked.Exchange(ref _lastChangeTimeTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>Forgets the queued changes, so a disconnected client does not refresh an editor later on</summary>
        public static void ClearPending()
        {
            PendingCraftTypes.Clear();
            PendingFolderTypes.Clear();
            Interlocked.Exchange(ref _lastChangeTimeTicks, 0);
        }

        /// <summary>Applies the queued changes to an editor that is already open. Unity thread only</summary>
        public static void FlushPending()
        {
            if (PendingCraftTypes.IsEmpty && PendingFolderTypes.IsEmpty) return;

            //The editor builds its own lists when it is entered and none of this exists in the other scenes
            if (!HighLogic.LoadedSceneIsEditor) return;

            var waitingMs = new TimeSpan(DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastChangeTimeTicks)).TotalMilliseconds;
            if (waitingMs < SettleTimeMs) return;

            var changedTypes = new List<CraftType>();
            var foldersChanged = false;
            foreach (var craftType in CraftLibraryPath.AllTypes)
            {
                if (PendingCraftTypes.TryRemove(craftType, out _)) changedTypes.Add(craftType);
                foldersChanged |= PendingFolderTypes.TryRemove(craftType, out _);
            }

            if (!foldersChanged && changedTypes.Count == 0) return;

            try
            {
                if (changedTypes.Contains(CraftType.Subassembly))
                    RefreshSubassemblyList();

                //Not open: the dialog reads the craft folders itself when the player opens it
                var dialog = FindOpenCraftBrowser();
                if (dialog == null) return;

                var directoryController = dialog.GetComponent<DirectoryController>();
                RefreshFolderTree(directoryController, foldersChanged);

                //The dialog only ever lists the ships of the facility it was opened for
                if (changedTypes.Contains(CraftTypeOf(EditorDriver.editorFacility)))
                    dialog.BuildPlayerCraftList();
            }
            catch (Exception ex)
            {
                LunaLog.LogError($"[LMP]: Error refreshing the craft dialog of the editor: {ex.Message}");
            }
        }

        #endregion

        #region Refresh

        /// <summary>
        /// Brings the folder tree of the dialog up to date. New folders need the whole tree built again,
        /// otherwise the counts of the folders that are already shown are enough
        /// </summary>
        private static void RefreshFolderTree(DirectoryController directoryController, bool foldersChanged)
        {
            if (directoryController == null) return;

            if (foldersChanged && RebuildDirectoryTree(directoryController))
            {
                //Everything is collapsed again, so the folder the player was on is reselected
                directoryController.ShowDirectoryTreeForEditor(EditorDriver.editorFacility);
                return;
            }

            directoryController.UpdateAllDirectoryDisplays();
        }

        /// <summary>
        /// Builds the folder tree again, the same way DirectoryController.Awake does. Returns false when
        /// this KSP version does not have the fields or the methods it needs, leaving the tree as it was
        /// </summary>
        private static bool RebuildDirectoryTree(DirectoryController directoryController)
        {
            if (directoryController == null) return false;
            if (ContentAreaField == null || DirectoryGroupsField == null || BuildStockDirectoryMethod == null || BuildSteamDirectoryMethod == null)
            {
                //Said once, every refresh would otherwise repeat it
                if (!_warnedMissingTreeApi)
                {
                    _warnedMissingTreeApi = true;
                    LunaLog.LogWarning("[LMP]: The craft dialog of the editor cannot show a new craft folder on this KSP version, " +
                                       "the craft list itself is still refreshed");
                }

                return false;
            }

            //The mission dialog is built from the mission folder and the training one has no craft folders,
            //neither of them is the tree we are about to build
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.IsMissionMode || DirectoryController.IsTrainingScenario)
                return false;

            var contentArea = ContentAreaField.GetValue(directoryController) as GameObject;
            var directoryGroups = DirectoryGroupsField.GetValue(directoryController) as IList;
            if (contentArea == null || directoryGroups == null) return false;

            var transforms = contentArea.transform;
            for (var i = transforms.childCount - 1; i >= 0; i--)
            {
                var child = transforms.GetChild(i).gameObject;

                //The stock and the workshop folders are looked up by name and Unity only destroys at the
                //end of the frame, so the old nodes are renamed and hidden to not be found or seen twice
                child.name = $"{StaleNodeName}{child.name}";
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }

            directoryGroups.Clear();

            directoryController.BuildDirectoryUI(EditorFacility.VAB);
            directoryController.BuildDirectoryUI(EditorFacility.SPH);

            //Not public, the stock and the workshop folders hang from the ones built just above
            BuildStockDirectoryMethod.Invoke(directoryController, new object[] { EditorFacility.VAB });
            BuildStockDirectoryMethod.Invoke(directoryController, new object[] { EditorFacility.SPH });
            BuildSteamDirectoryMethod.Invoke(directoryController, new object[] { EditorFacility.VAB });
            BuildSteamDirectoryMethod.Invoke(directoryController, new object[] { EditorFacility.SPH });

            return true;
        }

        /// <summary>
        /// The subassemblies are a tab of the part list of the editor instead of a dialog of their own,
        /// and only the ones directly in the root of the subassemblies folder are shown there
        /// </summary>
        private static void RefreshSubassemblyList()
        {
            var partList = PartCategorizer.Instance?.editorPartList;
            partList?.RefreshSubassemblies();
        }

        /// <summary>The dialog the editor is using, or null when the player did not open it</summary>
        private static CraftBrowserDialog FindOpenCraftBrowser()
        {
            if (EditorLogic.fetch != null && CraftBrowserDialogField != null)
            {
                var dialog = CraftBrowserDialogField.GetValue(EditorLogic.fetch) as CraftBrowserDialog;
                if (dialog != null && dialog.gameObject.activeInHierarchy) return dialog;
            }

            //The dialog hangs from the dialog canvas, which outlives a scene change, so a hidden one has
            //to be ignored
            var found = UnityEngine.Object.FindObjectOfType<CraftBrowserDialog>();
            return found != null && found.gameObject.activeInHierarchy ? found : null;
        }

        private static CraftType CraftTypeOf(EditorFacility facility)
        {
            return facility == EditorFacility.SPH ? CraftType.Sph : CraftType.Vab;
        }

        #endregion
    }
}