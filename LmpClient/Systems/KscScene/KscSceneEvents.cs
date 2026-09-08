using HarmonyLib;
using KSP.UI.Screens;
using LmpClient.Base;
using LmpClient.Diagnostics;
using LmpClient.Systems.VesselUpdateSys;
using LmpCommon.Locks;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace LmpClient.Systems.KscScene
{
    public class KscSceneEvents : SubSystem<KscSceneSystem>
    {
        private static readonly MethodInfo ClearVesselMarkers = typeof(KSCVesselMarkers).GetMethod("ClearVesselMarkers", AccessTools.all);

        public void OnLockAcquire(LockDefinition lockdefinition)
        {
            //Diagnostic: this handler is the prime suspect for the TRACKSTATION
            //entry stall — it fires once per LockAcquire response from the
            //server and rebuilds the entire TS vessel list each time. The
            //wrapping Stopwatch is the cheapest measurement available; the
            //branch+two-add aggregation in TsLoadProfiler.Record is dwarfed by
            //the work inside the handler itself.
            var t0 = Stopwatch.GetTimestamp();
            System.RefreshTrackingStationVessels();
            RefreshMarkers();
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.OnLockAcquire, Stopwatch.GetTimestamp() - t0);
        }

        public void OnLockRelease(LockDefinition lockdefinition)
        {
            var t0 = Stopwatch.GetTimestamp();
            System.RefreshTrackingStationVessels();
            RefreshMarkers();
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.OnLockRelease, Stopwatch.GetTimestamp() - t0);
        }

        /// <summary>
        /// Sometimes the vessel markers stay there with corrupt values so here we force clearing them
        /// </summary>
        public void OnSceneRequested(GameScenes requestedScene)
        {
            if (requestedScene > GameScenes.SPACECENTER)
            {
                ClearMarkers();
            }
        }

        /// <summary>
        /// Sometimes the vessel markers stay there with corrupt values so here we force clearing them
        /// </summary>
        public void LevelLoaded(GameScenes data)
        {
            if (data == GameScenes.SPACECENTER)
            {
                ClearMarkers();
                RefreshMarkers();
            }
        }

        public void OnVesselCreated(Vessel vessel)
        {
            var t0 = Stopwatch.GetTimestamp();
            System.RefreshTrackingStationVessels();
            RefreshMarkers();
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.OnVesselCreated, Stopwatch.GetTimestamp() - t0);
        }

        public void OnVesselRename(GameEvents.HostedFromToAction<Vessel, string> pair)
        {
            /**
             * Use this only in GameScenes.TRACKSTATION, because in FLIGHT working VesselUpdateSystem
             */
            if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
            {
                pair.host.name = pair.to;

                var vesselUpdateMessageSender = new VesselUpdateMessageSender();
                vesselUpdateMessageSender.SendVesselUpdate(pair.host);
            }
        }

        public void VesselInitialized(Vessel vessel, bool fromShipAssembly)
        {
            var t0 = Stopwatch.GetTimestamp();
            System.RefreshTrackingStationVessels();
            RefreshMarkers();
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.VesselInitialized, Stopwatch.GetTimestamp() - t0);
        }

        private static void ClearMarkers()
        {
            if (KSCVesselMarkers.fetch)
                ClearVesselMarkers?.Invoke(KSCVesselMarkers.fetch, null);

            foreach (var kscVesselMarker in Object.FindObjectsOfType<KSCVesselMarker>())
            {
                kscVesselMarker.Terminate();
                Object.DestroyImmediate(kscVesselMarker);
            }
        }

        private static void RefreshMarkers()
        {
            //Diagnostic: own bucket so we can separate "stock RefreshMarkers
            //is slow" from "the wrapping handler is doing other slow work".
            //This call is gated on SPACECENTER, so it is a no-op while the
            //user is actually inside TRACKSTATION — but the same handlers
            //also fire while sitting in SPACECENTER waiting for locks to
            //arrive, where this would be the dominant cost.
            var t0 = Stopwatch.GetTimestamp();
            if (KSCVesselMarkers.fetch && HighLogic.LoadedScene == GameScenes.SPACECENTER)
                KSCVesselMarkers.fetch.RefreshMarkers();
            TsLoadProfiler.Record(TsLoadProfiler.Bucket.RefreshMarkers, Stopwatch.GetTimestamp() - t0);
        }
    }
}
