using HarmonyLib;
using KSP.UI.Screens;
using LmpClient.Base;
using LmpClient.Diagnostics;
using LmpClient.Events;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Systems.KscScene
{
    /// <summary>
    /// This class controls what happens when we are in KSC screen
    /// </summary>
    public class KscSceneSystem : System<KscSceneSystem>
    {
        private static MethodInfo BuildSpaceTrackingVesselList { get; } = typeof(SpaceTracking).GetMethod("buildVesselsList", AccessTools.all);

        private static KscSceneEvents KscSceneEvents { get; } = new KscSceneEvents();

        //Coalesces tracking-station list refreshes into one rebuild per frame.
        //Without this, bursty scene-init events can trigger repeated O(N) UI rebuilds.
        private static volatile bool _trackingStationRebuildPending;

        #region Base overrides

        public override string SystemName { get; } = nameof(KscSceneSystem);

        protected override void OnEnabled()
        {
            LockEvent.onLockAcquire.Add(KscSceneEvents.OnLockAcquire);
            LockEvent.onLockRelease.Add(KscSceneEvents.OnLockRelease);
            GameEvents.onGameSceneLoadRequested.Add(KscSceneEvents.OnSceneRequested);
            GameEvents.onLevelWasLoadedGUIReady.Add(KscSceneEvents.LevelLoaded);
            GameEvents.onVesselCreate.Add(KscSceneEvents.OnVesselCreated);
            VesselInitializeEvent.onVesselInitialized.Add(KscSceneEvents.VesselInitialized);
            GameEvents.onVesselRename.Add(KscSceneEvents.OnVesselRename);

            SetupRoutine(new RoutineDefinition(0, RoutineExecution.FixedUpdate, IncreaseTimeWhileInEditor));
            //Drain pending refresh once per frame after event bursts settle.
            SetupRoutine(new RoutineDefinition(0, RoutineExecution.LateUpdate, FlushPendingRefreshes));
        }

        protected override void OnDisabled()
        {
            LockEvent.onLockAcquire.Remove(KscSceneEvents.OnLockAcquire);
            LockEvent.onLockRelease.Remove(KscSceneEvents.OnLockRelease);
            GameEvents.onGameSceneLoadRequested.Remove(KscSceneEvents.OnSceneRequested);
            GameEvents.onLevelWasLoadedGUIReady.Remove(KscSceneEvents.LevelLoaded);
            GameEvents.onVesselCreate.Remove(KscSceneEvents.OnVesselCreated);
            VesselInitializeEvent.onVesselInitialized.Remove(KscSceneEvents.VesselInitialized);
            GameEvents.onVesselRename.Remove(KscSceneEvents.OnVesselRename);
        }

        #endregion

        #region Routines

        /// <summary>
        /// While in editor the time doesn't advance so here we make it advance
        /// </summary>
        private static void IncreaseTimeWhileInEditor()
        {
            if (!HighLogic.LoadedSceneHasPlanetarium && HighLogic.LoadedScene >= GameScenes.SPACECENTER)
            {
                Planetarium.fetch.time += Time.fixedDeltaTime;
                HighLogic.CurrentGame.flightState.universalTime = Planetarium.fetch.time;
            }
        }

        #endregion

        #region Public methods

        /// Requests a tracking-station vessel-list refresh.
        /// Calls are coalesced and drained in <see cref="FlushPendingRefreshes"/>.
        /// </summary>
        public void RefreshTrackingStationVessels()
        {
            var t0 = Stopwatch.GetTimestamp();
            _trackingStationRebuildPending = true;
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.RefreshTrackingStationVessels, Stopwatch.GetTimestamp() - t0);
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Drains pending tracking-station refresh requests once per frame.
        /// </summary>
        private static void FlushPendingRefreshes()
        {
            if (!_trackingStationRebuildPending) return;
            _trackingStationRebuildPending = false;
            DoRefreshTrackingStationVessels();
        }

        /// <summary>
        /// Performs the actual tracking-station rebuild when in TRACKSTATION.
        /// </summary>
        private static void DoRefreshTrackingStationVessels()
        {
            var t0 = Stopwatch.GetTimestamp();
            if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
            {
                var spaceTracking = Object.FindObjectOfType<SpaceTracking>();
                if (spaceTracking != null)
                    BuildSpaceTrackingVesselList?.Invoke(spaceTracking, null);
            }
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.TsRebuildFlush, Stopwatch.GetTimestamp() - t0);
        }

        #endregion
    }
}
