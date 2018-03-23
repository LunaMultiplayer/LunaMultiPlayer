﻿using Harmony;
using LunaCommon.Enums;
// ReSharper disable All

namespace LunaClient.Harmony
{
    /// <summary>
    /// This harmony patch is intended to deny the loading while in a multiplayer game and in the KSC screen
    /// </summary>
    [HarmonyPatch(typeof(KSCPauseMenu))]
    [HarmonyPatch("quickLoad")]
    public class KSCPauseMenu_QuickLoad
    {
        [HarmonyPrefix]
        private static bool PrefixQuickLoad()
        {
            if (MainSystem.NetworkState < ClientState.Connected) return true;

            ScreenMessages.PostScreenMessage("Cannot load games in LMP!", 5f, ScreenMessageStyle.UPPER_CENTER);

            return false;
        }
    }
}
