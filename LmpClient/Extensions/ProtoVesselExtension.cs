using LmpClient.Systems.Chat;
using LmpClient.Systems.Flag;
using LmpClient.Systems.Mod;
using System;
using System.Collections.Generic;
using System.Linq;

namespace LmpClient.Extensions
{
    public static class ProtoVesselExtension
    {
        /// <summary>
        /// Finds a proto part snapshot in a proto vessel without generating garbage. Returns null if not found
        /// </summary>
        public static ProtoPartSnapshot GetProtoPart(this ProtoVessel protoVessel, uint partFlightId)
        {
            if (protoVessel == null) return null;

            for (var i = 0; i < protoVessel.protoPartSnapshots.Count; i++)
            {
                if (protoVessel.protoPartSnapshots[i].flightID == partFlightId)
                    return protoVessel.protoPartSnapshots[i];
            }
            return null;
        }

        /// <summary>
        /// Checks if the protovessel has resources, parts that you don't have or that they are banned.
        /// Also collects diagnostic information about EVERY problem on the vessel (banned parts, banned
        /// resources, missing parts, missing resources) and logs it as a single line per category — this
        /// is the line you want when chasing repeated <c>Vessel.UpdateCaches()</c> /
        /// <c>CommNetVessel.UpdateComm()</c> NullReferenceExceptions on a peer's vessel: it tells you
        /// which mod set the originating client expected without spamming one log line per part.
        /// Behavior is unchanged: still returns true on the first hard-failure category encountered.
        /// </summary>
        public static bool HasInvalidParts(this ProtoVessel pv, bool verboseErrors)
        {
            HashSet<string> bannedParts = null;
            HashSet<string> missingParts = null;
            HashSet<string> bannedResources = null;
            HashSet<string> missingResources = null;
            var sawHardFailure = false;

            foreach (var pps in pv.protoPartSnapshots)
            {
                if (ModSystem.Singleton.ModControl && !ModSystem.Singleton.AllowedParts.Contains(pps.partName))
                {
                    if (verboseErrors)
                    {
                        var msg = $"Protovessel {pv.vesselID} ({pv.vesselName}) contains the BANNED PART '{pps.partName}'. Skipping load.";
                        LunaLog.LogWarning(msg);
                        ChatSystem.Singleton.PmMessageServer(msg);
                    }

                    return true;
                }

                var nonWhitelistedResources = pps.resources.Select(r => r.resourceName)
                    .Except(ModSystem.Singleton.AllowedResources)
                    .Where(r => PartResourceLibrary.Instance.resourceDefinitions.Contains(r))
                    .Distinct()
                    .ToArray();
                if (ModSystem.Singleton.ModControl && nonWhitelistedResources.Any() && verboseErrors)
                {
                    var msg = $"Protovessel {pv.vesselID} ({pv.vesselName}) contains RESOURCE/S '{string.Join(", ", nonWhitelistedResources)}' not present in the server allowlist on part '{pps.partName}'. Allowing load because the resources exist locally.";
                    LunaLog.LogWarning(msg);
                    ChatSystem.Singleton.PmMessageServer(msg);
                }

                if (pps.partInfo == null)
                {
                    (missingParts ?? (missingParts = new HashSet<string>())).Add(pps.partName);
                    sawHardFailure = true;
                }

                foreach (var res in pps.resources)
                {
                    if (!PartResourceLibrary.Instance.resourceDefinitions.Contains(res.resourceName))
                        (missingResources ?? (missingResources = new HashSet<string>())).Add(res.resourceName);
                }
            }

            if (verboseErrors)
            {
                if (bannedParts != null)
                {
                    var msg = $"Protovessel {pv.vesselID} ({pv.vesselName}) contains BANNED PART(S) [{string.Join(", ", bannedParts)}]. Skipping load.";
                    LunaLog.LogWarning(msg);
                    ChatSystem.Singleton.PmMessageServer(msg);
                }
                if (bannedResources != null)
                {
                    var msg = $"Protovessel {pv.vesselID} ({pv.vesselName}) contains BANNED RESOURCE(S) [{string.Join(", ", bannedResources)}]. Skipping load.";
                    LunaLog.LogWarning(msg);
                    ChatSystem.Singleton.PmMessageServer(msg);
                }
                if (missingParts != null)
                {
                    LunaLog.LogWarning($"Protovessel {pv.vesselID} ({pv.vesselName}) contains MISSING PART(S) [{string.Join(", ", missingParts)}] - your install is missing the mod(s) that define them. Skipping load.");
                    LunaScreenMsg.PostScreenMessage($"Cannot load '{pv.vesselName}' - missing part(s): {string.Join(", ", missingParts)}", 10f, ScreenMessageStyle.UPPER_CENTER);
                }
                if (missingResources != null)
                {
                    //We allow loading of vessels that have missing resources. They will be removed by the player with the lock though...
                    var msg = $"Protovessel {pv.vesselID} ({pv.vesselName}) contains MISSING RESOURCE(S) [{string.Join(", ", missingResources)}].";
                    LunaLog.LogWarning(msg);
                    ChatSystem.Singleton.PmMessageServer(msg);
                    LunaScreenMsg.PostScreenMessage($"Vessel '{pv.vesselName}' contains modded RESOURCE(S): {string.Join(", ", missingResources)}", 10f, ScreenMessageStyle.UPPER_CENTER);
                }
            }

            return sawHardFailure;
        }

        /// <summary>
        /// Returns true or false in case the protovessel is an asteroid or a comet
        /// </summary>
        public static bool IsCometOrAsteroid(this ProtoVessel protoVessel)
        {
            return IsComet(protoVessel) || IsAsteroid(protoVessel);
        }

        /// <summary>
        /// Returns true or false in case the protovessel is a comet
        /// </summary>
        public static bool IsComet(this ProtoVessel protoVessel)
        {
            if (protoVessel == null) return false;

            if ((protoVessel.protoPartSnapshots == null || protoVessel.protoPartSnapshots.Count == 0) && protoVessel.vesselName.StartsWith("Ast."))
                return true;

            return protoVessel.protoPartSnapshots != null && protoVessel.protoPartSnapshots.Count == 1 && protoVessel.protoPartSnapshots[0].partName == "PotatoComet";
        }

        /// <summary>
        /// Returns true or false in case the protovessel is an asteroid
        /// </summary>
        public static bool IsAsteroid(this ProtoVessel protoVessel)
        {
            if (protoVessel == null) return false;

            if ((protoVessel.protoPartSnapshots == null || protoVessel.protoPartSnapshots.Count == 0) && protoVessel.vesselName.StartsWith("Ast."))
                return true;

            return protoVessel.protoPartSnapshots != null && protoVessel.protoPartSnapshots.Count == 1 && protoVessel.protoPartSnapshots[0].partName == "PotatoRoid";
        }

        /// <summary>
        /// Checks the protovessel for errors
        /// </summary>
        public static bool Validate(this ProtoVessel protoVessel, bool verboseErrors)
        {
            if (protoVessel == null)
            {
                if (verboseErrors) LunaLog.LogError("[LMP]: protoVessel is null!");
                return false;
            }

            if (protoVessel.vesselID == Guid.Empty)
            {
                if (verboseErrors) LunaLog.LogError("[LMP]: protoVessel id is null!");
                return false;
            }

            if (protoVessel.orbitSnapShot == null)
            {
                if (verboseErrors) LunaLog.LogWarning($"[LMP]: Skipping vessel {protoVessel.vesselID} load - Protovessel does not have an orbit snapshot");
                return false;
            }

            if (FlightGlobals.Bodies == null || protoVessel.orbitSnapShot.ReferenceBodyIndex < 0 || protoVessel.orbitSnapShot.ReferenceBodyIndex >= FlightGlobals.Bodies.Count)
            {
                if (verboseErrors) LunaLog.LogWarning($"[LMP]: Skipping vessel {protoVessel.vesselID} load - Could not find celestial body index {protoVessel.orbitSnapShot.ReferenceBodyIndex}");
                return false;
            }

            //Fix the flags urls in the vessel. The flag have the value as: "Squad/Flags/default"
            var missingFlagCounts = new Dictionary<string, int>();
            foreach (var part in protoVessel.protoPartSnapshots.Where(p => !string.IsNullOrEmpty(p.flagURL)))
            {
                if (!FlagSystem.Singleton.FlagExists(part.flagURL))
                {
                    if (!missingFlagCounts.ContainsKey(part.flagURL))
                        missingFlagCounts[part.flagURL] = 0;
                    missingFlagCounts[part.flagURL]++;
                    part.flagURL = "Squad/Flags/default";
                }
            }
            if (verboseErrors)
            {
                foreach (var kvp in missingFlagCounts)
                    LunaLog.Log($"[LMP]: Flag '{kvp.Key}' doesn't exist - replaced on {kvp.Value} part(s) with default.");
            }
            return true;
        }
    }
}
