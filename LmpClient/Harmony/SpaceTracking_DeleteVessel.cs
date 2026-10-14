using HarmonyLib;
using KSP.UI.Screens;
using LmpClient.Base;
using LmpClient.Localization;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;

// ReSharper disable All

namespace LmpClient.Harmony
{
    /// <summary>
    /// Blocks the Tracking Station "Terminate" button when AllowVesselTermination is false on the server.
    /// Physical destruction (crashes, explosions) is unaffected — only the UI button path is intercepted.
    /// </summary>
    [HarmonyPatch(typeof(SpaceTracking))]
    [HarmonyPatch("BtnOnClick_DeleteSelectedVessel")]
    public class SpaceTracking_DeleteSelectedVessel
    {
        [HarmonyPrefix]
        private static bool PrefixDeleteSelectedVessel()
        {
            if (MainSystem.NetworkState < ClientState.Connected) return true;

            if (!SettingsSystem.ServerSettings.AllowVesselTermination)
            {
                LunaScreenMsg.PostScreenMessage(LocalizationContainer.ScreenText.TerminationDisabledByServer, 5f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Blocks the Tracking Station "Recover" button when AllowVesselTermination is false on the server,
    /// unless the vessel is recoverable (landed or splashed on the home body). Recoverable vessels are
    /// allowed through so players can still recover missions that have safely returned home.
    /// </summary>
    [HarmonyPatch(typeof(SpaceTracking))]
    [HarmonyPatch("BtnOnclick_RecoverSelectedVessel")]
    public class SpaceTracking_RecoverSelectedVessel
    {
        [HarmonyPrefix]
        private static bool PrefixRecoverSelectedVessel(SpaceTracking __instance)
        {
            if (MainSystem.NetworkState < ClientState.Connected) return true;

            if (!SettingsSystem.ServerSettings.AllowVesselTermination)
            {
                // Allow recovery of vessels that are landed/splashed on the home body
                if (__instance.SelectedVessel != null && __instance.SelectedVessel.IsRecoverable)
                    return true;

                LunaScreenMsg.PostScreenMessage(LocalizationContainer.ScreenText.TerminationDisabledByServer, 5f, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }

            return true;
        }
    }
}
