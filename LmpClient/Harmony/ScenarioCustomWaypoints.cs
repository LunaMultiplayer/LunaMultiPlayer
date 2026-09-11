using FinePrint;
using HarmonyLib;
using LmpClient.Systems.Waypoint;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// This harmony patch is intended to send a custom waypoint to the server when the local player
    /// (or a mod) adds one to the game. KSP routes every custom waypoint through this method, which
    /// also adds it to the waypoint manager. Mission waypoints are skipped as those are not synced.
    /// </summary>
    [HarmonyPatch(typeof(ScenarioCustomWaypoints))]
    [HarmonyPatch("AddWaypoint", new[] { typeof(Waypoint), typeof(bool) })]
    public class ScenarioCustomWaypoints_AddWaypoint
    {
        [HarmonyPostfix]
        private static void PostfixAddWaypoint(Waypoint waypoint, bool isMission)
        {
            if (waypoint == null || isMission) return;

            WaypointSystem.Singleton.HandleLocalWaypointAdded(waypoint);
        }
    }

    /// <summary>
    /// This harmony patch is intended to tell the server when the local player (or a mod) removes a
    /// custom waypoint from the game. KSP routes custom waypoint deletions through this method.
    /// </summary>
    [HarmonyPatch(typeof(ScenarioCustomWaypoints))]
    [HarmonyPatch("RemoveWaypoint")]
    public class ScenarioCustomWaypoints_RemoveWaypoint
    {
        [HarmonyPostfix]
        private static void PostfixRemoveWaypoint(Waypoint waypoint)
        {
            if (waypoint == null) return;

            WaypointSystem.Singleton.HandleLocalWaypointRemoved(waypoint);
        }
    }
}
