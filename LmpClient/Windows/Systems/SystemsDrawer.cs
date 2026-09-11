using LmpClient.Systems.CraftLibrary;
using LmpClient.Systems.Facility;
using LmpClient.Systems.Flag;
using LmpClient.Systems.Groups;
using LmpClient.Systems.KerbalSys;
using LmpClient.Systems.KscScene;
using LmpClient.Systems.Lock;
using LmpClient.Systems.Mod;
using LmpClient.Systems.PlayerColorSys;
using LmpClient.Systems.PlayerConnection;
using LmpClient.Systems.Scenario;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpClient.Systems.TimeSync;
using LmpClient.Systems.VesselActionGroupSys;
using LmpClient.Systems.VesselCrewSys;
using LmpClient.Systems.VesselEvaEditorSys;
using LmpClient.Systems.VesselFairingsSys;
using LmpClient.Systems.VesselFlightStateSys;
using LmpClient.Systems.VesselImmortalSys;
using LmpClient.Systems.VesselLockSys;
using LmpClient.Systems.VesselPartSyncCallSys;
using LmpClient.Systems.VesselPartSyncFieldSys;
using LmpClient.Systems.VesselPositionSys;
using LmpClient.Systems.VesselProtoSys;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Systems.VesselResourceSys;
using LmpClient.Systems.VesselSwitcherSys;
using LmpClient.Systems.VesselUpdateSys;
using LmpClient.Systems.Warp;
using LmpClient.Systems.Waypoint;
using UnityEngine;

namespace LmpClient.Windows.Systems
{
    public partial class SystemsWindow
    {
        protected override void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(MoveRect);

            ScrollPos = GUILayout.BeginScrollView(ScrollPos, GUILayout.Width(WindowWidth), GUILayout.Height(WindowHeight));
            PrintSystemButtons();
            GUILayout.EndScrollView();
        }

        private static void PrintSystemButtons()
        {
            //Asteroid = GUILayout.Toggle(Asteroid, "Asteroid system");
            //if (Asteroid)
            //{
            //    //This system should never be toggled
            //    //SystemContainer.Get<AsteroidSystem>().Enabled = GUILayout.Toggle(SystemContainer.Get<AsteroidSystem>().Enabled, .Singleton.SystemName);
            //}
            CraftLibrarySystem.Singleton.Enabled = GUILayout.Toggle(CraftLibrarySystem.Singleton.Enabled, CraftLibrarySystem.Singleton.SystemName);
            FacilitySystem.Singleton.Enabled = GUILayout.Toggle(FacilitySystem.Singleton.Enabled, FacilitySystem.Singleton.SystemName);
            FlagSystem.Singleton.Enabled = GUILayout.Toggle(FlagSystem.Singleton.Enabled, FlagSystem.Singleton.SystemName);
            KscSceneSystem.Singleton.Enabled = GUILayout.Toggle(KscSceneSystem.Singleton.Enabled, KscSceneSystem.Singleton.SystemName);
            GroupSystem.Singleton.Enabled = GUILayout.Toggle(GroupSystem.Singleton.Enabled, GroupSystem.Singleton.SystemName);
            KerbalSystem.Singleton.Enabled = GUILayout.Toggle(KerbalSystem.Singleton.Enabled, KerbalSystem.Singleton.SystemName);
            LockSystem.Singleton.Enabled = GUILayout.Toggle(LockSystem.Singleton.Enabled, LockSystem.Singleton.SystemName);
            ModSystem.Singleton.Enabled = GUILayout.Toggle(ModSystem.Singleton.Enabled, ModSystem.Singleton.SystemName);

            //ModApi = GUILayout.Toggle(ModApi, "Mod api system");
            //if (ModApi)
            //{
            //    //This system cannot be toggled
            //    //SystemContainer.Get<ModApiSystem>().Enabled = GUILayout.Toggle(SystemContainer.Get<ModApiSystem>().Enabled, .Singleton.SystemName);
            //}
            PlayerColorSystem.Singleton.Enabled = GUILayout.Toggle(PlayerColorSystem.Singleton.Enabled, PlayerColorSystem.Singleton.SystemName);
            PlayerConnectionSystem.Singleton.Enabled = GUILayout.Toggle(PlayerConnectionSystem.Singleton.Enabled, PlayerConnectionSystem.Singleton.SystemName);

            ScenarioSystem.Singleton.Enabled = GUILayout.Toggle(ScenarioSystem.Singleton.Enabled, ScenarioSystem.Singleton.SystemName);
            TimeSyncSystem.Singleton.Enabled = GUILayout.Toggle(TimeSyncSystem.Singleton.Enabled, TimeSyncSystem.Singleton.SystemName);
            WarpSystem.Singleton.Enabled = GUILayout.Toggle(WarpSystem.Singleton.Enabled, WarpSystem.Singleton.SystemName);
            WaypointSystem.Singleton.Enabled = GUILayout.Toggle(WaypointSystem.Singleton.Enabled, WaypointSystem.Singleton.SystemName);


            //Toolbar = GUILayout.Toggle(Toolbar, "Toolbar system");
            //if (Toolbar)
            //{
            //    ToolbarSystem.Singleton.Enabled = GUILayout.Toggle(ToolbarSystem.Singleton.Enabled, .Singleton.SystemName);
            //}
            VesselCrewSystem.Singleton.Enabled = GUILayout.Toggle(VesselCrewSystem.Singleton.Enabled, VesselCrewSystem.Singleton.SystemName);
            VesselFlightStateSystem.Singleton.Enabled = GUILayout.Toggle(VesselFlightStateSystem.Singleton.Enabled, VesselFlightStateSystem.Singleton.SystemName);
            VesselImmortalSystem.Singleton.Enabled = GUILayout.Toggle(VesselImmortalSystem.Singleton.Enabled, VesselImmortalSystem.Singleton.SystemName);
            VesselLockSystem.Singleton.Enabled = GUILayout.Toggle(VesselLockSystem.Singleton.Enabled, VesselLockSystem.Singleton.SystemName);
            VesselPositionSystem.Singleton.Enabled = GUILayout.Toggle(VesselPositionSystem.Singleton.Enabled, VesselPositionSystem.Singleton.SystemName);
            VesselUpdateSystem.Singleton.Enabled = GUILayout.Toggle(VesselUpdateSystem.Singleton.Enabled, VesselUpdateSystem.Singleton.SystemName);
            VesselPartSyncFieldSystem.Singleton.Enabled = GUILayout.Toggle(VesselPartSyncFieldSystem.Singleton.Enabled, VesselPartSyncFieldSystem.Singleton.SystemName);
            VesselPartSyncCallSystem.Singleton.Enabled = GUILayout.Toggle(VesselPartSyncCallSystem.Singleton.Enabled, VesselPartSyncCallSystem.Singleton.SystemName);
            VesselResourceSystem.Singleton.Enabled = GUILayout.Toggle(VesselResourceSystem.Singleton.Enabled, VesselResourceSystem.Singleton.SystemName);
            VesselActionGroupSystem.Singleton.Enabled = GUILayout.Toggle(VesselActionGroupSystem.Singleton.Enabled, VesselActionGroupSystem.Singleton.SystemName);
            VesselFairingsSystem.Singleton.Enabled = GUILayout.Toggle(VesselFairingsSystem.Singleton.Enabled, VesselFairingsSystem.Singleton.SystemName);
            VesselProtoSystem.Singleton.Enabled = GUILayout.Toggle(VesselProtoSystem.Singleton.Enabled, VesselProtoSystem.Singleton.SystemName);
            VesselRemoveSystem.Singleton.Enabled = GUILayout.Toggle(VesselRemoveSystem.Singleton.Enabled, VesselRemoveSystem.Singleton.SystemName);
            VesselSwitcherSystem.Singleton.Enabled = GUILayout.Toggle(VesselSwitcherSystem.Singleton.Enabled, VesselSwitcherSystem.Singleton.SystemName);
            VesselEvaEditorSystem.Singleton.Enabled = GUILayout.Toggle(VesselEvaEditorSystem.Singleton.Enabled, VesselEvaEditorSystem.Singleton.SystemName);

            ShareFundsSystem.Singleton.Enabled = GUILayout.Toggle(ShareFundsSystem.Singleton.Enabled, ShareFundsSystem.Singleton.SystemName);
            ShareScienceSystem.Singleton.Enabled = GUILayout.Toggle(ShareScienceSystem.Singleton.Enabled, ShareScienceSystem.Singleton.SystemName);
            ShareScienceSubjectSystem.Singleton.Enabled = GUILayout.Toggle(ShareScienceSubjectSystem.Singleton.Enabled, ShareScienceSubjectSystem.Singleton.SystemName);
            ShareReputationSystem.Singleton.Enabled = GUILayout.Toggle(ShareReputationSystem.Singleton.Enabled, ShareReputationSystem.Singleton.SystemName);
            ShareTechnologySystem.Singleton.Enabled = GUILayout.Toggle(ShareTechnologySystem.Singleton.Enabled, ShareTechnologySystem.Singleton.SystemName);
            ShareContractsSystem.Singleton.Enabled = GUILayout.Toggle(ShareContractsSystem.Singleton.Enabled, ShareContractsSystem.Singleton.SystemName);
            ShareAchievementsSystem.Singleton.Enabled = GUILayout.Toggle(ShareAchievementsSystem.Singleton.Enabled, ShareAchievementsSystem.Singleton.SystemName);
            ShareStrategySystem.Singleton.Enabled = GUILayout.Toggle(ShareStrategySystem.Singleton.Enabled, ShareStrategySystem.Singleton.SystemName);

        }
    }
}
