using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Extensions;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.Warp;
using LmpCommon.Time;
using System;
using UniLinq;
using UnityEngine;
using UnityEngine.Profiling;

namespace LmpClient.Systems.TimeSync
{
    /// <inheritdoc />
    /// <summary>
    /// This system syncs the game time with the UTC time.
    /// Your game time may shift, for example when you computer hangs or there are lots of vessels etc.
    /// Those "micro hangs" should be corrected so all the players are in the same time as the UTC.
    /// The correction is done by warping the game speed (running slightly faster or slower) so the sync
    /// is eased in smoothly and the physics is never reset. Only for huge errors we jump as a last resort
    /// </summary>
    public class TimeSyncSystem : System<TimeSyncSystem>
    {
        #region Fields & properties

        #region Public

        public TimeSyncEvents TimerSyncEvents { get; } = new TimeSyncEvents();

        public static long ServerStartTime { get; set; }

        private static double _universalTime;

        /// <summary>
        /// Thread safe way of accessing Planetarium.GetUniversalTime()
        /// </summary>
        public static double UniversalTime
        {
            get => MainSystem.IsUnityThread ? Planetarium.GetUniversalTime() : _universalTime;
            private set => _universalTime = value;
        }

        /// <summary>
        /// Gets the server clock in seconds.
        /// </summary>
        public static double ServerClockSec => TimeUtil.TicksToSeconds(LunaNetworkTime.UtcNow.Ticks - ServerStartTime);

        /// <summary>
        /// Gets the current time error between the server time and the game time
        /// </summary>
        public static double CurrentErrorSec => UniversalTime - WarpSystem.Singleton.CurrentSubspaceTime;

        #endregion

        #region Constants

        /// <summary>
        /// If the time between UTC and game is greater than this, the game time will be fixed using the phisics clock (makes game go faster/slower)
        /// </summary>
        private const int MinPhysicsClockMsError = 25;
        /// <summary>
        /// Minimum speed that the game can go
        /// </summary>
        private const float MinPhysicsClockRate = 0.85f;
        /// <summary>
        /// Max speed that the game can go. If you put this number too high the game will lag a lot.
        /// </summary>
        private const float MaxPhysicsClockRate = 1.20f;
        /// <summary>
        /// Limit at which we won't fix the time with the GAME timescale
        /// </summary>
        private const int MaxPhysicsClockMsError = 5000;
        /// <summary>
        /// Maximum change of the game speed on every fixed update
        /// </summary>
        private const float MaxTimeScaleChangePerFixedUpdate = 0.01f;

        #endregion

        #region Private

        private static bool CurrentlyWarping => WarpSystem.Singleton.CurrentSubspace == -1;

        private static bool CanSyncTime
        {
            get
            {
                switch (HighLogic.LoadedScene)
                {
                    case GameScenes.TRACKSTATION:
                    case GameScenes.FLIGHT:
                    case GameScenes.SPACECENTER:
                    case GameScenes.EDITOR:
                        return true;
                    default:
                        return false;
                }
            }
        }

        #endregion

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(TimeSyncSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();

            //Refresh it at TimingManager.TimingStage.Precalc so it's updated JUST after KSP updates it's time
            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, UpdateUniversalTime);

            //Uncomment to check if the TimingManager.TimingStage.Precalc is the correct time. All the operations should give "0"
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.ObscenelyEarly, ()=> LunaLog.Log($"ObscenelyEarly {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Early, () => LunaLog.Log($"Early {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, () => LunaLog.Log($"Precalc {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Earlyish, () => LunaLog.Log($"Earlyish {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Normal, () => LunaLog.Log($"Normal {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FashionablyLate, () => LunaLog.Log($"FashionablyLate {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.FlightIntegrator, () => LunaLog.Log($"FlightIntegrator {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Late, () => LunaLog.Log($"Late {Planetarium.GetUniversalTime() - UniversalTime}"));
            //TimingManager.FixedUpdateAdd(TimingManager.TimingStage.BetterLateThanNever, () => LunaLog.Log($"BetterLateThanNever {Planetarium.GetUniversalTime() - UniversalTime}"));

            TimingManager.FixedUpdateAdd(TimingManager.TimingStage.Precalc, CheckGameTime);

            SpectateEvent.onStartSpectating.Add(TimerSyncEvents.OnStartSpectating);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, UpdateUniversalTime);
            TimingManager.FixedUpdateRemove(TimingManager.TimingStage.Precalc, CheckGameTime);

            SpectateEvent.onStartSpectating.Remove(TimerSyncEvents.OnStartSpectating);
            ServerStartTime = 0;
            Time.timeScale = 1;
        }

        #endregion

        #region Update methods

        /// <summary>
        /// Routine that checks our time against the server time and adjust it if needed.
        /// We only adjust the GAME speed and we will only do it if the time error is between <see cref="MinPhysicsClockMsError"/> and <see cref="MaxPhysicsClockMsError"/>
        /// We cannot do it with more than <see cref="MaxPhysicsClockMsError"/> as then we would need a lot of time to catch up with the time error.
        /// For greater errors we just fix the time with the StepClock
        /// </summary>
        private void CheckGameTime()
        {
            Profiler.BeginSample(nameof(CheckGameTime));

            if (Enabled && !CurrentlyWarping && CanSyncTime && !WarpSystem.Singleton.WaitingSubspaceIdFromServer)
            {
                var targetTime = WarpSystem.Singleton.CurrentSubspaceTime;
                var currentError = TimeUtil.SecondsToMilliseconds(CurrentErrorSec);

                if (Math.Abs(currentError) < MaxPhysicsClockMsError)
                {
                    //Time error is not so big so we fix it by warping the game speed: the sync is eased in
                    //smoothly and the physics is never reset (a hard jump to the target time makes the physics shake)
                    SkewClock(currentError);
                }
                else
                {
                    LunaLog.LogWarning($"[LMP] Adjusted time from: {UniversalTime} to: {targetTime} due to error: {currentError}");
                    SetGameTime(targetTime);
                    Time.timeScale = 1;
                }
            }
            else
            {
                //While warping (KSP time warp) or while we cannot sync, ease the game speed back to normal
                //so our clock skew doesn't interfere with the KSP warp rates
                EaseGameSpeedToNormal();
            }

            Profiler.EndSample();
        }

        #endregion

        #region FixedUpdateMethods

        /// <summary>
        /// Updates the UniversalTime with the game time on every fixed update
        /// </summary>
        private static void UpdateUniversalTime()
        {
            UniversalTime = Planetarium.GetUniversalTime();
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Forces a time sync against the server time
        /// </summary>
        public void ForceTimeSync()
        {
            if (Enabled && !CurrentlyWarping && CanSyncTime && !WarpSystem.Singleton.WaitingSubspaceIdFromServer)
            {
                var targetTime = WarpSystem.Singleton.CurrentSubspaceTime;
                LunaLog.LogWarning($"FORCING a time sync from: {UniversalTime} to: {targetTime}. Error: {TimeUtil.SecondsToMilliseconds(CurrentErrorSec)}");
                SetGameTime(targetTime);
            }
        }


        /// <summary>
        /// Call this method to set a new time using the planetarium
        /// </summary>
        public void SetGameTime(double targetTick)
        {
            if (double.IsNaN(targetTick) || double.IsInfinity(targetTick) || targetTick < 0)
            {
                LunaLog.LogWarning($"[LMP]: Ignoring invalid game-time sync target {targetTick} — value is non-finite or negative. This indicates a server time-difference calculation error.");
                return;
            }

            if (HighLogic.LoadedSceneIsFlight)
            {
                //As we are syncing to a new game time, we must advance all the ship positions and put them in the correct orbit "epoch"
                var vesselsToUpdate = LockSystem.LockQuery.GetAllUnloadedUpdateLocks(SettingsSystem.CurrentSettings.PlayerName)
                    .Select(l => FlightGlobals.FindVessel(l.VesselId))
                    .Where(v => v != null);

                foreach (var vessel in vesselsToUpdate)
                {
                    vessel.AdvanceShipPosition(targetTick);
                }
            }

            Planetarium.SetUniversalTime(targetTick);
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Here we adjust the GAME timescale and make the game go faster or slower
        /// </summary>
        private static void SkewClock(double currentErrorMs)
        {
            float targetRate;
            if (Math.Abs(currentErrorMs) < MinPhysicsClockMsError)
            {
                targetRate = 1f;
            }
            else
            {
                targetRate = Mathf.Clamp((float)Math.Pow(2, -TimeUtil.MillisecondsToSeconds(currentErrorMs)), MinPhysicsClockRate, MaxPhysicsClockRate);
            }

            Time.timeScale = Mathf.MoveTowards(Time.timeScale, targetRate, MaxTimeScaleChangePerFixedUpdate / 2);
        }

        /// <summary>
        /// Eases the game speed back to normal without an instant change of the time scale
        /// </summary>
        private static void EaseGameSpeedToNormal()
        {
            if (!Mathf.Approximately(Time.timeScale, 1f))
                Time.timeScale = Mathf.MoveTowards(Time.timeScale, 1f, MaxTimeScaleChangePerFixedUpdate);
        }

        #endregion
    }
}
