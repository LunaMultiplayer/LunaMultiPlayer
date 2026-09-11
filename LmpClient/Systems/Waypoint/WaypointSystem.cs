using System;
using System.Collections.Concurrent;
using System.Linq;
using FinePrint;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Waypoint;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.Waypoint
{
    /// <summary>
    /// This system syncs the custom map waypoints (the ones persisted by <see cref="ScenarioCustomWaypoints"/>) between players.
    /// Waypoints created by contracts are not synced as those are already handled by the contract sync.
    /// </summary>
    public class WaypointSystem : MessageSystem<WaypointSystem, WaypointMessageSender, WaypointMessageHandler>
    {
        #region Fields

        private WaypointEvents WaypointEvents { get; } = new WaypointEvents();

        /// <summary>
        /// All the waypoints known to be on the server, keyed by the navigation id
        /// </summary>
        public ConcurrentDictionary<string, WaypointInfo> Waypoints { get; } = new ConcurrentDictionary<string, WaypointInfo>();

        /// <summary>
        /// The game waypoint objects we have added to the game (or that already existed in the game), keyed by the navigation id
        /// </summary>
        private readonly ConcurrentDictionary<string, global::FinePrint.Waypoint> AppliedWaypoints =
            new ConcurrentDictionary<string, global::FinePrint.Waypoint>();

        /// <summary>
        /// Guard used while we are applying or removing a remote waypoint in the game so the harmony patches
        /// don't send those changes back to the server
        /// </summary>
        internal bool ApplyingRemoteWaypoints { get; set; }

        private static bool InWaypointScene => HighLogic.LoadedScene == GameScenes.FLIGHT || HighLogic.LoadedScene == GameScenes.TRACKSTATION;

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(WaypointSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();

            GameEvents.onLevelWasLoadedGUIReady.Add(WaypointEvents.LevelLoaded);

            MessageSender.SendWaypointsRequest();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, SyncWaypointsWithGame));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            //Always try to remove the event, as when we disconnect from a server the system will get disabled
            GameEvents.onLevelWasLoadedGUIReady.Remove(WaypointEvents.LevelLoaded);

            Waypoints.Clear();
            AppliedWaypoints.Clear();
            ApplyingRemoteWaypoints = false;
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Called from the harmony patch when the local player added a custom waypoint to the game
        /// </summary>
        internal void HandleLocalWaypointAdded(global::FinePrint.Waypoint gameWaypoint)
        {
            if (!Enabled || MainSystem.NetworkState < ClientState.Running) return;
            if (ApplyingRemoteWaypoints) return;
            if (gameWaypoint == null || gameWaypoint.isMission) return;

            //Some mods create waypoints without a navigation id. KSP generates a new one on load in that case, so we do the same
            //here to have a stable identifier to sync
            if (gameWaypoint.navigationId == Guid.Empty)
                gameWaypoint.navigationId = Guid.NewGuid();

            var waypointInfo = CreateWaypointInfo(gameWaypoint);
            var navigationId = waypointInfo.NavigationId;

            Waypoints.AddOrUpdate(navigationId, waypointInfo, (key, existingVal) => waypointInfo);
            AppliedWaypoints[navigationId] = gameWaypoint;

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WaypointCreateMsgData>();
            msgData.Waypoint = waypointInfo;
            MessageSender.SendMessage(msgData);
        }

        /// <summary>
        /// Called from the harmony patch when the local player removed a custom waypoint from the game
        /// </summary>
        internal void HandleLocalWaypointRemoved(global::FinePrint.Waypoint gameWaypoint)
        {
            if (!Enabled || MainSystem.NetworkState < ClientState.Running) return;
            if (ApplyingRemoteWaypoints) return;
            if (gameWaypoint == null || gameWaypoint.isMission) return;

            var navigationId = gameWaypoint.navigationId.ToString();

            //Remove it locally right away, otherwise the sync routine could add it back to the game
            //before the server confirmation arrives
            Waypoints.TryRemove(navigationId, out _);
            AppliedWaypoints.TryRemove(navigationId, out _);

            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WaypointRemoveMsgData>();
            msgData.NavigationId = navigationId;
            MessageSender.SendMessage(msgData);
        }

        /// <summary>
        /// Replaces the local waypoint list with the one from the server, removing the waypoints that no longer
        /// exist on the server and adding the new ones to the game
        /// </summary>
        internal void ApplyServerWaypointList(WaypointInfo[] serverWaypoints, int waypointsCount)
        {
            var serverIds = new string[Math.Max(waypointsCount, 0)];
            for (var i = 0; i < waypointsCount; i++)
            {
                serverIds[i] = serverWaypoints[i].NavigationId;
            }

            foreach (var existingId in Waypoints.Keys)
            {
                if (!serverIds.Contains(existingId))
                {
                    RemoveServerWaypoint(existingId);
                }
            }

            for (var i = 0; i < waypointsCount; i++)
            {
                ApplyServerWaypoint(serverWaypoints[i].Clone());
            }
        }

        /// <summary>
        /// Adds or updates a waypoint that comes from the server and applies it to the game
        /// </summary>
        internal void ApplyServerWaypoint(WaypointInfo waypointInfo)
        {
            if (waypointInfo == null || !IsNavigationIdValid(waypointInfo.NavigationId)) return;

            var navigationId = waypointInfo.NavigationId;
            Waypoints.AddOrUpdate(navigationId, waypointInfo, (key, existingVal) => waypointInfo);

            if (InWaypointScene)
                ApplyWaypointToGame(navigationId, waypointInfo);
        }

        /// <summary>
        /// Removes a waypoint that comes from the server from our list and from the game
        /// </summary>
        internal void RemoveServerWaypoint(string navigationId)
        {
            if (string.IsNullOrEmpty(navigationId)) return;

            Waypoints.TryRemove(navigationId, out _);
            UnapplyWaypointFromGame(navigationId);
        }

        /// <summary>
        /// Applies the known server waypoints to the game, removes the applied waypoints that are no longer known
        /// and pushes the custom waypoints that were created while offline to the server.
        /// Runs on the unity thread.
        /// </summary>
        internal void SyncWaypointsWithGame()
        {
            if (!Enabled || MainSystem.NetworkState < ClientState.Running) return;
            if (!InWaypointScene) return;
            if (ScenarioCustomWaypoints.Instance == null || MapView.MapCamera == null) return;

            //A scene change destroys and recreates the game waypoint objects, so drop the stale references.
            //The waypoints that were saved into the game persistence are found again by navigation id below.
            foreach (var applied in AppliedWaypoints)
            {
                if (WaypointManager.FindWaypoint(applied.Value.navigationId) == null)
                    AppliedWaypoints.TryRemove(applied.Key, out _);
            }

            //Apply the server waypoints that are not in the game yet
            foreach (var waypoint in Waypoints.Values)
            {
                ApplyWaypointToGame(waypoint.NavigationId, waypoint);
            }

            //Push the custom waypoints that exist in the game but that the server doesn't know about
            //(eg: waypoints created while we were offline or before connecting)
            var gameWaypointManager = WaypointManager.Instance();
            if (gameWaypointManager == null) return;

            foreach (var gameWaypoint in gameWaypointManager.Waypoints)
            {
                if (gameWaypoint == null || gameWaypoint.isMission || !gameWaypoint.isCustom) continue;
                if (gameWaypoint.navigationId == Guid.Empty) continue;

                var navigationId = gameWaypoint.navigationId.ToString();
                if (Waypoints.ContainsKey(navigationId)) continue;

                var waypointInfo = CreateWaypointInfo(gameWaypoint);
                Waypoints.TryAdd(navigationId, waypointInfo);

                var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WaypointCreateMsgData>();
                msgData.Waypoint = waypointInfo;
                MessageSender.SendMessage(msgData);
            }
        }

        #endregion

        #region Private methods

        private static WaypointInfo CreateWaypointInfo(global::FinePrint.Waypoint gameWaypoint)
        {
            return new WaypointInfo
            {
                Name = gameWaypoint.name ?? string.Empty,
                CelestialName = gameWaypoint.celestialName ?? string.Empty,
                Latitude = gameWaypoint.latitude,
                Longitude = gameWaypoint.longitude,
                NavigationId = gameWaypoint.navigationId.ToString(),
            };
        }

        private static bool IsNavigationIdValid(string navigationId) =>
            !string.IsNullOrEmpty(navigationId) && Guid.TryParse(navigationId, out _);

        /// <summary>
        /// Adds the given waypoint to the game unless it already exists in it. Must be called from the unity thread
        /// while we are in a scene where custom waypoints can exist
        /// </summary>
        private void ApplyWaypointToGame(string navigationId, WaypointInfo waypointInfo)
        {
            if (!InWaypointScene || ScenarioCustomWaypoints.Instance == null || MapView.MapCamera == null) return;

            //If we already applied it, there is nothing to do
            if (AppliedWaypoints.ContainsKey(navigationId)) return;

            //If the game already has this waypoint (eg: loaded from the save file) just track the existing object
            if (Guid.TryParse(navigationId, out var guid))
            {
                var existing = WaypointManager.FindWaypoint(guid);
                if (existing != null)
                {
                    AppliedWaypoints[navigationId] = existing;
                    return;
                }
            }

            var gameWaypoint = new global::FinePrint.Waypoint
            {
                name = waypointInfo.Name,
                celestialName = waypointInfo.CelestialName,
                latitude = waypointInfo.Latitude,
                longitude = waypointInfo.Longitude,
                navigationId = guid,
            };

            ApplyingRemoteWaypoints = true;
            try
            {
                ScenarioCustomWaypoints.AddWaypoint(gameWaypoint);
            }
            finally
            {
                ApplyingRemoteWaypoints = false;
            }

            AppliedWaypoints[navigationId] = gameWaypoint;
        }

        /// <summary>
        /// Removes the given waypoint from the game. Must be called from the unity thread
        /// </summary>
        private void UnapplyWaypointFromGame(string navigationId)
        {
            if (!AppliedWaypoints.TryRemove(navigationId, out var gameWaypoint)) return;
            if (gameWaypoint == null) return;

            ApplyingRemoteWaypoints = true;
            try
            {
                ScenarioCustomWaypoints.RemoveWaypoint(gameWaypoint);
            }
            finally
            {
                ApplyingRemoteWaypoints = false;
            }
        }

        #endregion
    }

    public class WaypointEvents : SubSystem<WaypointSystem>
    {
        /// <summary>
        /// Triggered when a scene has finished loading. We sync the waypoints into the game as soon
        /// as we enter a scene where custom waypoints can exist.
        /// </summary>
        public void LevelLoaded(GameScenes data)
        {
            System.SyncWaypointsWithGame();
        }
    }

    public class WaypointMessageSender : SubSystem<WaypointSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<WaypointCliMsg>(msg)));
        }

        public void SendWaypointsRequest()
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(NetworkMain.CliMsgFactory.CreateNew<WaypointCliMsg, WaypointListRequestMsgData>()));
        }
    }

    public class WaypointMessageHandler : SubSystem<WaypointSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is WaypointBaseMsgData msgData)) return;

            switch (msgData.WaypointMessageType)
            {
                case LmpCommon.Message.Types.WaypointMessageType.ListResponse:
                    {
                        var data = (WaypointListResponseMsgData)msgData;
                        System.ApplyServerWaypointList(data.Waypoints, data.WaypointsCount);
                        break;
                    }
                case LmpCommon.Message.Types.WaypointMessageType.WaypointCreate:
                    {
                        var data = (WaypointCreateMsgData)msgData;
                        System.ApplyServerWaypoint(data.Waypoint.Clone());
                        break;
                    }
                case LmpCommon.Message.Types.WaypointMessageType.WaypointRemove:
                    {
                        var data = (WaypointRemoveMsgData)msgData;
                        System.RemoveServerWaypoint(data.NavigationId);
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
