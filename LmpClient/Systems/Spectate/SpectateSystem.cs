using System;
using System.Collections.Generic;
using LmpClient.Base;
using LmpClient.Systems.Lock;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.VesselSwitcherSys;
using LmpClient.VesselUtilities;
using LmpCommon;

namespace LmpClient.Systems.Spectate
{
    public class SpectateSystem : System<SpectateSystem>
    {
        #region Fields & properties

        /// <summary>
        /// The vessel each player is currently piloting. The vessel a player pilots is the one he has the control lock of
        /// </summary>
        private static readonly Dictionary<string, Guid> PilotedVessels = new Dictionary<string, Guid>();

        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(SpectateSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, RefreshPilotedVessels));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            PilotedVessels.Clear();
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Returns true if the spectate option should be shown for the given player: the player must be piloting a vessel,
        /// he cannot be the local player and we cannot be already spectating him
        /// </summary>
        public bool CanSpectatePlayer(string playerName)
        {
            if (playerName == SettingsSystem.CurrentSettings.PlayerName)
                return false;

            if (!PilotedVessels.TryGetValue(playerName, out var pilotedVesselId) || FlightGlobals.FindVessel(pilotedVesselId) == null)
                return false;

            return !IsSpectatingPlayer(pilotedVesselId);
        }

        /// <summary>
        /// Opens the vessel the given player is currently piloting. In flight we switch to the vessel which starts the
        /// spectating as the other player owns the control lock of it. Outside of flight we do the same as the tracking
        /// station "Launch" button: save the game and start the flight focused on the vessel
        /// </summary>
        public void SpectatePlayer(string playerName)
        {
            if (!PilotedVessels.TryGetValue(playerName, out var pilotedVesselId) || IsSpectatingPlayer(pilotedVesselId))
                return;

            //The piloted vessels are refreshed once per second so double check the player still owns the control lock
            if (!LockSystem.LockQuery.ControlLockBelongsToPlayer(pilotedVesselId, playerName))
                return;

            var pilotedVessel = FlightGlobals.FindVessel(pilotedVesselId);
            if (pilotedVessel == null)
                return;

            LunaLog.Log($"[LMP]: Spectating vessel {pilotedVessel.vesselName} piloted by {playerName}");

            if (HighLogic.LoadedSceneIsFlight)
            {
                VesselSwitcherSystem.Singleton.SwitchToVessel(pilotedVessel);
                return;
            }

            var vesselIndex = FlightGlobals.Vessels.IndexOf(pilotedVessel);
            if (vesselIndex < 0)
                return;

            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            FlightDriver.StartAndFocusVessel("persistent", vesselIndex);
        }

        #endregion

        #region Update methods

        /// <summary>
        /// Fill the piloted vessels dictionary with the vessel each player is currently piloting
        /// </summary>
        private static void RefreshPilotedVessels()
        {
            PilotedVessels.Clear();
            foreach (var controlLock in LockSystem.LockQuery.GetAllControlLocks())
                PilotedVessels[controlLock.PlayerName] = controlLock.VesselId;
        }

        #endregion

        #region Private methods

        /// <summary>
        /// Returns true if we are currently spectating the vessel the given player is piloting
        /// </summary>
        private static bool IsSpectatingPlayer(Guid pilotedVesselId)
        {
            return VesselCommon.IsSpectating && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.id == pilotedVesselId;
        }

        #endregion
    }
}