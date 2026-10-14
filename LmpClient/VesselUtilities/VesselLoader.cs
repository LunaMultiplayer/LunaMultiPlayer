using KSP.UI.Screens.Flight;
using LmpClient.Extensions;
using LmpClient.Systems.VesselPositionSys;
using LmpClient.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Object = UnityEngine.Object;

namespace LmpClient.VesselUtilities
{
    /// <summary>
    /// What <see cref="VesselLoader.LoadVessel"/> actually did. Returned in place of a
    /// plain bool so callers (notably <c>VesselProtoSystem.CheckVesselsToLoad</c>) can
    /// distinguish "we destructively reloaded the live Vessel and any reload-event
    /// listeners should fire" from "the incoming proto matched the existing structure
    /// so we early-returned and nothing actually changed". The pre-existing collapse to
    /// a single bool caused <c>[LMP]: Vessel ... reloaded</c> + the
    /// <c>VesselReloadEvent.onLmpVesselReloaded</c> fire to happen on every wire-side
    /// drift broadcast even when no destruction occurred, which (a) was misleading in
    /// KSP.log and (b) ran any future subscriber's reload work on a no-op.
    /// </summary>
    public enum VesselLoadOutcome
    {
        /// <summary>Validate() failed, Load() threw, or the proto produced a malformed orbit.</summary>
        Failed,
        /// <summary>No prior <see cref="Vessel"/> for this id; a brand-new one was created.</summary>
        FreshlyLoaded,
        /// <summary>An existing <see cref="Vessel"/> was destroyed and a replacement was created.</summary>
        Reloaded,
        /// <summary>An existing <see cref="Vessel"/> already matched the incoming structure — nothing was done.</summary>
        UnchangedEarlyOut,
    }

    public class VesselLoader
    {
        /// <summary>
        /// Loads/Reloads a vessel into game. See <see cref="VesselLoadOutcome"/> for the
        /// four possible outcomes; callers should only fire reload/load events on
        /// <see cref="VesselLoadOutcome.FreshlyLoaded"/> / <see cref="VesselLoadOutcome.Reloaded"/>.
        /// </summary>
        public static VesselLoadOutcome LoadVessel(ProtoVessel vesselProto, bool forceReload)
        {
            try
            {
                LogProtoVesselSummary(vesselProto, forceReload);
                if (!vesselProto.Validate(true)) return VesselLoadOutcome.Failed;
                return LoadVesselIntoGame(vesselProto, forceReload);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error loading vessel: {e}");
                SaveFailedProtoVesselToDisk(vesselProto, e);
                CleanUpFailedVesselLoad(vesselProto);
                return VesselLoadOutcome.Failed;
            }
        }

        /// <summary>
        /// Cheap in-place proto swap for scenes where peer vessels are not physically
        /// visible (currently SPACECENTER and EDITOR). Updates the existing
        /// <see cref="Vessel"/>'s <c>protoVessel</c> backreference, replaces the entry
        /// in <c>flightState.protoVessels</c>, and points the new proto's
        /// <c>vesselRef</c> at the surviving <see cref="Vessel"/>. Does NOT destroy or
        /// re-instantiate any <see cref="GameObject"/>, does NOT call
        /// <c>vesselProto.Load</c>, and does NOT trigger stock KSP's persistentId
        /// collision rewrite — which is what makes this a multi-frame-saving shortcut
        /// versus the full <see cref="LoadVessel"/> path. Safe only for unloaded /
        /// packed vessels, which is the steady state of every <see cref="Vessel"/> in
        /// FlightGlobals while the player is in SPACECENTER or EDITOR; in FLIGHT or
        /// TRACKSTATION the in-world vessel may be loaded and rendered, so callers
        /// MUST keep using the destructive <see cref="LoadVessel"/> there.
        /// </summary>
        /// <returns>True when the swap was performed; false when the inputs were unusable.</returns>
        public static bool UpdateProtoInPlace(Vessel existingVessel, ProtoVessel newProto)
        {
            if (existingVessel == null || newProto == null) return false;
            if (HighLogic.CurrentGame?.flightState == null) return false;

            try
            {
                //Wire-side DiscoveryInfo defenders for newProto already ran in
                //VesselSerializer.CreateSafeProtoVesselFromConfigNode. We deliberately
                //do NOT call EnsureSafeDiscoveryInfo here: that pass exists to keep
                //stock KSP's ProtoVessel.Load off its synthesise-then-Infinity-parse
                //branch, and Load is exactly what we are avoiding on this path.
                var protoVessels = HighLogic.CurrentGame.flightState.protoVessels;
                var vesselId = existingVessel.id;

                //Replace the old entry in flightState.protoVessels with newProto so any
                //subsequent save / scene-transition rebuild uses current wire data
                //instead of the now-stale proto we are about to detach.
                var replaced = false;
                if (protoVessels != null)
                {
                    for (var i = 0; i < protoVessels.Count; i++)
                    {
                        if (protoVessels[i] != null && protoVessels[i].vesselID == vesselId)
                        {
                            protoVessels[i] = newProto;
                            replaced = true;
                            break;
                        }
                    }
                    if (!replaced) protoVessels.Add(newProto);
                }

                existingVessel.protoVessel = newProto;
                newProto.vesselRef = existingVessel;
                return true;
            }
            catch (Exception e)
            {
                //Falls back to the caller's normal destructive path on next wire update.
                LunaLog.LogWarning($"[LMP]: UpdateProtoInPlace failed for {existingVessel.id}: {e.Message}");
                return false;
            }
        }

        //Tracks vessel IDs whose failure dump we have already written this session so
        //repeated server retries of the same persistently-broken vessel (e.g. one with a
        //malformed DiscoveryInfo double) don't keep rewriting an identical capture file
        //every ~30 seconds. The first failure per vessel is the one that matters for
        //triage; subsequent failures of the same id are duplicates by construction.
        private static readonly HashSet<Guid> _alreadyDumpedFailedVessels = new HashSet<Guid>();
        private static string _failedVesselDumpFolder;

        /// <summary>
        /// Resolves (and lazily creates) the folder where parse-failure captures live.
        /// Sits next to <c>KSP.log</c> at <c>{KspPath}/Logs/LMP/VesselParseFailures/</c> so
        /// users sending diagnostics to developers can grab everything from one obvious
        /// location instead of hunting under <c>GameData/</c>.
        /// </summary>
        private static string GetFailedVesselDumpFolder()
        {
            if (_failedVesselDumpFolder != null) return _failedVesselDumpFolder;
            try
            {
                var folder = CommonUtil.CombinePaths(MainSystem.KspPath, "Logs", "LMP", "VesselParseFailures");
                Directory.CreateDirectory(folder);
                _failedVesselDumpFolder = folder;
            }
            catch (Exception e)
            {
                LunaLog.LogWarning($"[LMP]: Could not initialise vessel-parse-failure dump folder: {e.Message}");
                _failedVesselDumpFolder = null;
            }
            return _failedVesselDumpFolder;
        }

        /// <summary>
        /// Captures the offending <see cref="ProtoVessel"/> and the exception that broke
        /// it to a single text file under the parse-failure folder. The captured file
        /// contains: a comment header with vessel id/name/type/situation/part-count, the
        /// full exception stack, and the result of <see cref="ProtoVessel.Save"/> (which
        /// is the same on-disk format stock KSP uses for <c>persistent.sfs</c>). All
        /// failure paths swallow their exceptions — diagnostic capture must never break
        /// the load path further than the original failure already did.
        /// </summary>
        private static void SaveFailedProtoVesselToDisk(ProtoVessel vesselProto, Exception loadException)
        {
            if (vesselProto == null) return;
            var vesselId = vesselProto.vesselID;
            if (vesselId == Guid.Empty) return;
            if (!_alreadyDumpedFailedVessels.Add(vesselId)) return;

            try
            {
                var folder = GetFailedVesselDumpFolder();
                if (folder == null) return;

                var safeName = MakeSafeFileName(SafeGetVesselName(vesselProto));
                var filePath = CommonUtil.CombinePaths(folder, $"{vesselId}_{safeName}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("// LMP vessel parse failure capture");
                sb.AppendLine($"// Captured at (UTC): {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}");
                sb.AppendLine($"// Vessel ID:    {vesselId}");
                sb.AppendLine($"// Vessel Name:  {safeName}");
                sb.AppendLine($"// Vessel Type:  {SafeGet(() => vesselProto.vesselType.ToString())}");
                sb.AppendLine($"// Situation:    {SafeGet(() => vesselProto.situation.ToString())}");
                sb.AppendLine($"// Part Count:   {SafeGet(() => vesselProto.protoPartSnapshots?.Count.ToString())}");
                sb.AppendLine($"// Crew Count:   {SafeGet(() => vesselProto.GetVesselCrew()?.Count.ToString())}");
                sb.AppendLine();
                sb.AppendLine("// Exception thrown by ProtoVessel.Load:");
                if (loadException != null)
                {
                    foreach (var line in loadException.ToString().Split('\n'))
                        sb.AppendLine($"//   {line.TrimEnd('\r')}");
                }
                sb.AppendLine();
                sb.AppendLine("// --- ProtoVessel ConfigNode dump ---");
                try
                {
                    var node = new ConfigNode("VESSEL");
                    vesselProto.Save(node);
                    sb.Append(Encoding.UTF8.GetString(node.Serialize()));
                }
                catch (Exception saveEx)
                {
                    sb.AppendLine($"// (ProtoVessel.Save also threw: {saveEx.Message})");
                }

                File.WriteAllText(filePath, sb.ToString());
                LunaLog.Log($"[LMP]: Wrote failed vessel dump to {filePath}");
            }
            catch (Exception e)
            {
                //Diagnostic capture must never break the load path further than already broken.
                LunaLog.LogWarning($"[LMP]: SaveFailedProtoVesselToDisk for {vesselId} failed: {e.Message}");
            }
        }

        /// <summary>
        /// Strips characters that are illegal in filenames on the host OS, collapses
        /// the result to a reasonable max length, and substitutes a placeholder when
        /// the input is empty so we always end up with a usable file name.
        /// </summary>
        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(invalid.Contains(c) ? '_' : c);
            var safe = sb.ToString().Trim();
            if (safe.Length == 0) return "unknown";
            if (safe.Length > 60) safe = safe.Substring(0, 60);
            return safe;
        }

        /// <summary>
        /// Invokes a string-returning getter inside a try/catch so any single broken
        /// proto field can't abort the whole capture — used only for the comment
        /// header lines, which are best-effort metadata.
        /// </summary>
        private static string SafeGet(Func<string> getter)
        {
            try { return getter() ?? "<null>"; }
            catch { return "<error>"; }
        }

        /// <summary>
        /// Cleans up a half-instantiated <see cref="Vessel"/> left behind when
        /// <see cref="ProtoVessel.Load"/> throws partway through. Stock KSP creates the
        /// Vessel GameObject very early in <c>ProtoVessel.Load</c> and only later loads
        /// parts and <c>DiscoveryInfo</c>; if any of those later steps throws (the most
        /// common shape in the wild is a server-side malformed double in
        /// <c>DiscoveryInfo</c> hitting <see cref="System.FormatException"/> inside
        /// <c>DiscoveryInfo.Load</c>), control unwinds back to <see cref="LoadVessel"/>'s
        /// catch block but the partial Vessel is still alive in the scene. On the next
        /// frame Unity invokes <c>Vessel.Start()</c> on it, which NREs in
        /// <c>RebuildCrewList()</c>, <c>CometVessel.OnStart()</c>, and
        /// <c>SuspensionLoadBalancer.OnStart()</c>; from that point the vessel NREs in
        /// <c>Vessel.UpdateCaches()</c>/<c>CommNetVessel.UpdateComm()</c> every FixedUpdate
        /// forever, producing the 1.5k+ NRE storms seen joining certain servers. Destroying
        /// the partial vessel here prevents <c>Vessel.Start()</c> from ever firing on it,
        /// and removing the partial <see cref="ProtoVessel"/> from
        /// <c>flightState.protoVessels</c> stops its part-persistentIds from colliding
        /// against future loads.
        /// </summary>
        private static void CleanUpFailedVesselLoad(ProtoVessel vesselProto)
        {
            if (vesselProto == null) return;

            //Each step is wrapped independently because the partially-instantiated vessel can
            //be in any half-state imaginable — vesselRef may be set OR null, FlightGlobals
            //may or may not yet have it registered, gameObject may already be destroyed,
            //parts may be null/empty/contain destroyed entries, etc. A throw in any single
            //step previously aborted the entire cleanup, leaving the partial Vessel alive
            //and Vessel.Start() free to NRE next frame. Each block must be allowed to fail
            //independently so the others still run.
            var vesselId = vesselProto.vesselID;
            var vesselName = SafeGetVesselName(vesselProto);

            //Try every avenue we have to locate the partial Vessel — vesselProto.vesselRef
            //is set by stock ProtoVessel.Load early, but on certain failure paths it ends up
            //null while the GameObject still exists in the scene (and is later findable via
            //FlightGlobals or by Guid).
            Vessel partialVessel = null;
            try { partialVessel = vesselProto.vesselRef; } catch { /* swallow */ }
            if (partialVessel == null && vesselId != Guid.Empty)
            {
                try { partialVessel = FlightGlobals.FindVessel(vesselId); } catch { /* swallow */ }
            }

            if (partialVessel != null)
            {
                LunaLog.Log($"[LMP]: Destroying partially-loaded vessel {vesselId} ({vesselName}) " +
                            $"to prevent Vessel.Start() NRE storms.");

                //Disable the GameObject FIRST so Unity stops dispatching Start/Update to its
                //components before we touch anything else. This is the single most important
                //step — even if every later call throws, a deactivated GameObject won't
                //fire Vessel.Start() next frame.
                try
                {
                    if (partialVessel.gameObject != null)
                        partialVessel.gameObject.SetActive(false);
                }
                catch (Exception e) { LunaLog.LogWarning($"[LMP]: SetActive(false) failed for {vesselId}: {e.Message}"); }

                try { FlightGlobals.RemoveVessel(partialVessel); }
                catch (Exception e) { LunaLog.LogWarning($"[LMP]: FlightGlobals.RemoveVessel failed for {vesselId}: {e.Message}"); }

                try
                {
                    var parts = partialVessel.parts;
                    if (parts != null)
                    {
                        for (var i = 0; i < parts.Count; i++)
                        {
                            try
                            {
                                var part = parts[i];
                                if (part != null && part.gameObject != null)
                                    Object.Destroy(part.gameObject);
                            }
                            catch { /* swallow per-part — keep destroying the rest */ }
                        }
                    }
                }
                catch (Exception e) { LunaLog.LogWarning($"[LMP]: parts enumeration failed for {vesselId}: {e.Message}"); }

                try
                {
                    if (partialVessel.gameObject != null)
                        Object.Destroy(partialVessel.gameObject);
                }
                catch (Exception e) { LunaLog.LogWarning($"[LMP]: Object.Destroy(gameObject) failed for {vesselId}: {e.Message}"); }

                try { vesselProto.vesselRef = null; }
                catch { /* swallow — best-effort backreference clear */ }
            }

            //Always drop the partial ProtoVessel from flightState even if we couldn't find or
            //destroy the live Vessel. This stops its protoPartSnapshot persistentIds from
            //colliding against future loads (the rename storms we see after each failed
            //load are exactly this — the orphaned protoVessel keeps every part-id registered
            //in FlightGlobals.PersistentUnloadedPartIds).
            try
            {
                if (HighLogic.CurrentGame?.flightState != null && vesselId != Guid.Empty)
                {
                    HighLogic.CurrentGame.flightState.protoVessels.RemoveAll(
                        v => v == null || v.vesselID == vesselId);
                }
            }
            catch (Exception e) { LunaLog.LogWarning($"[LMP]: flightState.protoVessels cleanup failed for {vesselId}: {e.Message}"); }
        }

        /// <summary>
        /// Reads <see cref="ProtoVessel.vesselName"/> in a try/catch so cleanup logging never
        /// throws on a half-loaded proto whose name field is in an unexpected state.
        /// </summary>
        private static string SafeGetVesselName(ProtoVessel vesselProto)
        {
            try { return vesselProto?.vesselName ?? "<unknown>"; }
            catch { return "<unreadable>"; }
        }

        /// <summary>
        /// Logs a single-line summary of an incoming protovessel before <see cref="ProtoVessel.Load"/>
        /// runs. Provides a baseline to correlate against KSP-side <c>Vessel.UpdateCaches()</c> /
        /// <c>CommNetVessel.UpdateComm()</c> NullReferenceExceptions: the offending vessel can be
        /// matched back to the most recent load by id, name, type, and the distinct part-name set
        /// (which fingerprints which mod set the originating client expected).
        /// </summary>
        private static void LogProtoVesselSummary(ProtoVessel vesselProto, bool forceReload)
        {
            if (vesselProto == null) return;
            try
            {
                var partCount = vesselProto.protoPartSnapshots?.Count ?? 0;
                var distinctParts = vesselProto.protoPartSnapshots == null
                    ? string.Empty
                    : string.Join(",", vesselProto.protoPartSnapshots.Select(p => p.partName).Distinct());
                LunaLog.Log($"[LMP]: Loading proto vessel {vesselProto.vesselID} ({vesselProto.vesselName}) " +
                            $"type={vesselProto.vesselType} situation={vesselProto.situation} " +
                            $"forceReload={forceReload} parts={partCount} distinctParts=[{distinctParts}]");
            }
            catch (Exception e)
            {
                //Diagnostic logging must never break the load path.
                LunaLog.LogWarning($"[LMP]: LogProtoVesselSummary failed for {vesselProto.vesselID}: {e.Message}");
            }
        }

        /// <summary>
        /// One-shot sanity walk of the freshly-loaded vessel that <see cref="ProtoVessel.Load"/> just
        /// produced. Detects the corruption shapes that cause stock KSP to NRE on every
        /// FixedUpdate inside <c>Vessel.UpdateCaches()</c> and inside <c>CommNetVessel.UpdateComm()</c>,
        /// as well as the part-graph cross-reference shapes that fall out of stock KSP renaming
        /// colliding part persistentIds during <c>Load</c> (broken <c>Part.parent</c>, broken
        /// <c>AttachNode.attachedPart</c> on attN/srfN). Pure logging — does not change which
        /// vessels survive load.
        /// </summary>
        private static void LogPostLoadVesselSanity(ProtoVessel vesselProto)
        {
            WalkVesselForCorruption(vesselProto?.vesselRef, "Post-load sanity");
        }

        /// <summary>
        /// Removes null entries from every <see cref="ProtoPartSnapshot.protoModuleCrew"/> list
        /// on the freshly-loaded protovessel. Stock KSP's <c>ProtoPartSnapshot.LoadCrew</c> adds a
        /// <c>null</c> placeholder to <c>protoModuleCrew</c> whenever a wire-side <c>crew = NAME</c>
        /// value cannot be resolved through <c>HighLogic.CurrentGame.CrewRoster</c> — typically
        /// because the originating client serialised an empty name (logged as
        /// <c>"[Protocrewmember]: Instance of crewmember  in part X on Y does not exist in the roster"</c>
        /// with the tell-tale double space) or a kerbal that exists in their roster but not ours
        /// (e.g. a tourist created on the originator's side that the server never replicated to us
        /// before the vessel proto arrived).
        ///
        /// Those null placeholders sit dormant on the proto until something walks them, at which
        /// point three different stock KSP code paths NRE in distinct ways:
        ///
        ///   1. <c>KSP.UI.Screens.KbApp_VesselCrew.CreateVesselCrewList</c> in the Tracking Station
        ///      pulls crew via <c>vessel.protoVessel.protoPartSnapshots[*].protoModuleCrew</c> and
        ///      <see cref="System.Collections.Generic.List{T}.Sort(System.Comparison{T})"/>s the
        ///      result by <c>KbApp_VesselCrew.CompareSeatIdx</c>, which dereferences <c>r1.seatIdx</c>
        ///      / <c>r2.seatIdx</c> — Sort then rethrows our NRE as
        ///      <c>InvalidOperationException: Failed to compare two elements in the array</c>
        ///      when the user clicks the vessel and stalls the Tracking Station UI permanently.
        ///   2. On scene transition to FLIGHT, <c>ProtoVessel.LoadObjects</c> →
        ///      <c>ProtoPartSnapshot.ConfigurePart</c> → <c>Part.RegisterCrew</c> dereferences each
        ///      <c>protoModuleCrew</c> entry to register seat assignments and NREs out of
        ///      <c>FlightDriver.Start</c>, leaving the player on a black flight scene.
        ///   3. The half-instantiated vessel's <c>ModuleCommand.UpdateControlSourceState</c> then
        ///      NREs every FixedUpdate forever once <c>FixedUpdate</c> resumes, producing the
        ///      "FixedUpdate NRE storm" pattern documented elsewhere in this file.
        ///
        /// All three paths require null entries inside <c>protoModuleCrew</c> as their precondition.
        /// Removing them here — at the single choke point through which every wire-arrival vessel
        /// passes — defangs all three crash sites at once. Stock KSP already considers a null
        /// entry to mean "this seat references a kerbal we don't have", so the local game state
        /// after the scrub is identical to the state stock KSP would have produced if the wire
        /// data simply omitted those crew entries to begin with: the seat is empty, the part is
        /// crewless from this client's perspective, and the originating peer's vessel-update
        /// stream remains the source of truth for who is actually aboard. We deliberately do not
        /// invent placeholder kerbals here because that would (a) leak fake names into the local
        /// roster the next time stock KSP saved it and (b) re-collide with the real kerbals when
        /// the wire data finally caught up.
        ///
        /// IMPORTANT — parallel-list invariant: <see cref="ProtoPartSnapshot"/> carries
        /// <c>protoModuleCrew</c> (<c>List&lt;ProtoCrewMember&gt;</c>) and
        /// <c>protoCrewNames</c> (<c>List&lt;string&gt;</c>) as parallel arrays indexed in lockstep.
        /// Stock <c>KerbalRoster.ValidateAssignments(Game)</c> walks both by the same index <c>i</c>
        /// (loop bound is <c>protoModuleCrew.Count</c>, lookup is <c>protoCrewNames[i]</c>) and on
        /// the missing-from-roster branch calls <c>SystemUtilities.ExpungeKerbal(protoModuleCrew[i])</c>.
        /// If we strip nulls from <c>protoModuleCrew</c> alone the indices shift and a subsequent
        /// validation pass — which fires on <i>every</i> game / scene load triggered by the
        /// post-`Game.Save` autosave round-trip — would call <c>ExpungeKerbal</c> on an unrelated,
        /// real kerbal that now lives at the same index as a still-unresolved name. That would
        /// silently delete real crew. We therefore remove matched (crew slot, name slot) pairs in
        /// lockstep, keeping the two-list invariant intact for every downstream stock consumer.
        ///
        /// The scrub is wrapped in try/catch — diagnostic / defensive cleanup must never break
        /// the load path further than the original failure already did.
        /// </summary>
        private static void ScrubInvalidProtoCrew(ProtoVessel vesselProto)
        {
            if (vesselProto?.protoPartSnapshots == null) return;
            try
            {
                var totalRemoved = 0;
                var partsAffected = 0;
                var nameDesyncs = 0;
                for (var p = 0; p < vesselProto.protoPartSnapshots.Count; p++)
                {
                    var snapshot = vesselProto.protoPartSnapshots[p];
                    var crew = snapshot?.protoModuleCrew;
                    if (crew == null || crew.Count == 0) continue;

                    var names = snapshot.protoCrewNames;
                    var removedHere = 0;

                    // Walk back-to-front so RemoveAt does not invalidate indices for entries
                    // we haven't visited yet.
                    for (var i = crew.Count - 1; i >= 0; i--)
                    {
                        if (crew[i] != null) continue;

                        crew.RemoveAt(i);
                        if (names != null)
                        {
                            if (i < names.Count) names.RemoveAt(i);
                            else nameDesyncs++;
                        }
                        removedHere++;
                    }

                    if (removedHere > 0)
                    {
                        totalRemoved += removedHere;
                        partsAffected++;
                    }
                }

                if (totalRemoved > 0)
                {
                    LunaLog.LogWarning(
                        $"[LMP]: Scrubbed {totalRemoved} null protoModuleCrew entr{(totalRemoved == 1 ? "y" : "ies")} " +
                        $"(and parallel protoCrewNames slots) across {partsAffected} part(s) on vessel " +
                        $"{vesselProto.vesselID} ({SafeGetVesselName(vesselProto)}). " +
                        $"Caused by wire-side crew names that don't resolve in this client's CrewRoster (typical: empty " +
                        $"crew name or a tourist not yet replicated to us). Without this scrub stock KSP NREs in " +
                        $"KbApp_VesselCrew.CompareSeatIdx (Tracking Station focus), Part.RegisterCrew (scene→FLIGHT), " +
                        $"and ModuleCommand.UpdateControlSourceState (every FixedUpdate); without the lockstep " +
                        $"removal, KerbalRoster.ValidateAssignments would later ExpungeKerbal on a misindexed real kerbal.");
                }

                if (nameDesyncs > 0)
                {
                    LunaLog.LogWarning(
                        $"[LMP]: ScrubInvalidProtoCrew encountered {nameDesyncs} crew slot(s) on vessel " +
                        $"{vesselProto.vesselID} where protoModuleCrew was longer than protoCrewNames before the scrub. " +
                        $"This means the proto already arrived with a broken parallel-list invariant; the live " +
                        $"protoModuleCrew has been cleaned but stock KerbalRoster.ValidateAssignments will silently " +
                        $"skip the surplus indices since its loop bound is now the (shorter) crew list.");
                }
            }
            catch (Exception e)
            {
                //Diagnostic / defensive cleanup must never break the load path.
                LunaLog.LogWarning($"[LMP]: ScrubInvalidProtoCrew failed for {vesselProto.vesselID}: {e.Message}");
            }
        }

        /// <summary>
        /// Schedules a second sanity walk one Unity frame after <see cref="ProtoVessel.Load"/> so we
        /// observe the vessel after stock KSP has run <c>Vessel.Awake</c>/<c>OnEnable</c>/<c>Start</c>
        /// and every <c>VesselModule.Start</c> on it. The synchronous post-load walk above runs
        /// inside the same call frame as <c>Load</c>, before any of those callbacks have fired, so
        /// it cannot see corruption that only materializes during start — for example part-graph
        /// cross-references (<c>parent_persistentId</c>, <c>srfN_persistentId</c>, <c>attN_persistentId</c>)
        /// that resolve to null after stock KSP has renamed colliding part persistentIds during
        /// <c>Load</c>, or stock VesselModules whose <c>Awake</c> snapshotted now-stale ids. The
        /// 1-frame delay places the re-walk after the next Update tick, by which point all of
        /// <c>Vessel.Start</c>/<c>VesselModule.Start</c> have run and the corruption (if any) is
        /// stably observable.
        /// </summary>
        private static void ScheduleDeferredPostStartSanityWalk(ProtoVessel vesselProto)
        {
            var vesselRef = vesselProto?.vesselRef;
            if (vesselRef == null) return;
            var vesselId = vesselRef.id;
            try
            {
                CoroutineUtil.StartFrameDelayedRoutine(
                    $"PostStartSanityWalk-{vesselId}",
                    () => WalkVesselForCorruption(FlightGlobals.FindVessel(vesselId), "Post-Start sanity"),
                    1);
            }
            catch (Exception e)
            {
                //Diagnostic logging must never break the load path.
                LunaLog.LogWarning($"[LMP]: ScheduleDeferredPostStartSanityWalk failed for {vesselId}: {e.Message}");
            }
        }

        /// <summary>
        /// Shared implementation of the corruption-shape walk used by both the synchronous
        /// post-load check and the deferred post-Start re-walk. The set of shapes is identical
        /// across both invocations so a single CORRUPT line lets you tell which phase first
        /// saw the problem just by reading the <paramref name="phase"/> prefix. Always emits
        /// either a CORRUPT line or an OK line per (vessel, phase), so an absent line in the
        /// log unambiguously means "the deferred coroutine never reached this vessel" rather
        /// than "the vessel was clean".
        /// </summary>
        private static void WalkVesselForCorruption(Vessel v, string phase)
        {
            if (v == null) return;
            try
            {
                if (v.parts == null)
                {
                    LunaLog.LogError($"[LMP]: {phase}: vessel {v.id} ({v.vesselName}) has a NULL parts list.");
                    return;
                }

                var nullProtoVessel = v.protoVessel == null ? 1 : 0;
                // rootPart is legitimately null for unloaded/packed vessels (every vessel in
                // the Tracking Station is on-rails and unloaded), so only treat a null root
                // as corruption when the vessel is actually loaded. Same goes for the per-part
                // checks below (parent/attN/srfN/parts iteration) — Vessel.parts is empty on
                // unloaded vessels by design.
                var nullRootPart = v.loaded && !v.rootPart ? 1 : 0;
                var rootPart = v.rootPart;
                var protoPartSnapshots = v.protoVessel?.protoPartSnapshots;
                int nullParts = 0, nullPartInfo = 0, nullVesselRef = 0, wrongVesselRef = 0,
                    nullModules = 0, moduleCountMismatch = 0, nullCrewSlot = 0, nullProtoCrewSlot = 0,
                    orphanParent = 0, brokenAttachNode = 0, brokenSrfAttachNode = 0;

                // Walk the ProtoVessel's protoPartSnapshots independently of the live Vessel.parts
                // list. For unloaded/packed vessels (every vessel in the Tracking Station, every
                // non-active in-flight vessel) Vessel.parts is empty by design, so the live-parts
                // null-crew check below cannot see corruption that lives on the proto — yet stock
                // KSP's KbApp_VesselCrew.CreateVesselCrewList pulls crew via
                // vessel.protoVessel.protoPartSnapshots[*].protoModuleCrew and crashes on null
                // entries there even for unloaded vessels. Counting both surfaces makes the
                // CORRUPT line accurate regardless of load state.
                if (protoPartSnapshots != null)
                {
                    for (var p = 0; p < protoPartSnapshots.Count; p++)
                    {
                        var protoCrew = protoPartSnapshots[p]?.protoModuleCrew;
                        if (protoCrew == null) continue;
                        for (var c = 0; c < protoCrew.Count; c++)
                        {
                            if (protoCrew[c] == null) nullProtoCrewSlot++;
                        }
                    }
                }
                for (var i = 0; i < v.parts.Count; i++)
                {
                    var part = v.parts[i];
                    if (part == null)
                    {
                        nullParts++;
                        continue;
                    }

                    if (part.partInfo == null) nullPartInfo++;
                    if (!part.vessel) nullVesselRef++;
                    else if (part.vessel != v) wrongVesselRef++;
                    if (part.Modules == null)
                    {
                        nullModules++;
                    }
                    else if (protoPartSnapshots != null)
                    {
                        var protoPart = i < protoPartSnapshots.Count ? protoPartSnapshots[i] : null;
                        if (protoPart?.modules != null && protoPart.modules.Count != part.Modules.Count)
                            moduleCountMismatch++;
                    }
                    if (part.protoModuleCrew != null && part.protoModuleCrew.Any(c => c == null))
                        nullCrewSlot++;

                    // Renamed-persistentId fallout: stock KSP's part-graph cross-references
                    // (parent/attN/srfN) that pointed at the old persistentId resolve to null
                    // after stock Load renamed the colliding part. This is the most likely
                    // shape behind the Vessel.RebuildCrewList()/Vessel.UpdateCaches() NREs.
                    if (rootPart != null && part != rootPart && !part.parent) orphanParent++;
                    if (part.attachNodes != null)
                    {
                        for (var n = 0; n < part.attachNodes.Count; n++)
                        {
                            var node = part.attachNodes[n];
                            if (node != null && node.attachedPartId != 0 && !node.attachedPart)
                                brokenAttachNode++;
                        }
                    }
                    if (part.srfAttachNode != null && part.srfAttachNode.attachedPartId != 0 && !part.srfAttachNode.attachedPart)
                        brokenSrfAttachNode++;
                }

                var nullVesselModule = 0;
                if (v.vesselModules != null)
                {
                    for (var m = 0; m < v.vesselModules.Count; m++)
                    {
                        if (v.vesselModules[m] == null) nullVesselModule++;
                    }
                }

                var corrupt = nullParts + nullPartInfo + nullVesselRef + wrongVesselRef +
                              nullModules + moduleCountMismatch + nullCrewSlot + nullProtoCrewSlot +
                              nullProtoVessel + nullRootPart +
                              orphanParent + brokenAttachNode + brokenSrfAttachNode + nullVesselModule;
                if (corrupt > 0)
                {
                    LunaLog.LogError($"[LMP]: {phase}: vessel {v.id} ({v.vesselName}) is CORRUPT - " +
                                     $"nullParts={nullParts} nullPartInfo={nullPartInfo} " +
                                     $"nullVesselRef={nullVesselRef} wrongVesselRef={wrongVesselRef} " +
                                     $"nullModules={nullModules} moduleCountMismatch={moduleCountMismatch} " +
                                     $"nullCrewSlot={nullCrewSlot} nullProtoCrewSlot={nullProtoCrewSlot} " +
                                     $"nullProtoVessel={nullProtoVessel} " +
                                     $"nullRootPart={nullRootPart} orphanParent={orphanParent} " +
                                     $"brokenAttachNode={brokenAttachNode} brokenSrfAttachNode={brokenSrfAttachNode} " +
                                     $"nullVesselModule={nullVesselModule}. " +
                                     $"This vessel will likely NRE in Vessel.UpdateCaches()/CommNetVessel.UpdateComm() every FixedUpdate.");
                }
                else
                {
                    LunaLog.Log($"[LMP]: {phase}: vessel {v.id} ({v.vesselName}) OK ({v.parts.Count} parts).");
                }
            }
            catch (Exception e)
            {
                LunaLog.LogWarning($"[LMP]: {phase} walk failed for {v.id}: {e.Message}");
            }
        }

        #region Private methods

        /// <summary>
        /// Loads the vessel proto into the current game. Returns the outcome so the
        /// caller can distinguish a real destructive reload from an unchanged early-out
        /// (the latter previously masqueraded as a successful reload in logs / events).
        /// </summary>
        private static VesselLoadOutcome LoadVesselIntoGame(ProtoVessel vesselProto, bool forceReload)
        {
            if (HighLogic.CurrentGame?.flightState == null)
                return VesselLoadOutcome.Failed;

            var reloadingOwnVessel = FlightGlobals.ActiveVessel && vesselProto.vesselID == FlightGlobals.ActiveVessel.id;
            var hadExistingVessel = false;

            //In case the vessel exists, silently remove them from unity and recreate it again
            var existingVessel = FlightGlobals.FindVessel(vesselProto.vesselID);
            if (existingVessel != null)
            {
                hadExistingVessel = true;
                // Compute structural counts using the in-world ProtoVessel as the source of truth
                // when the existing vessel is unloaded/packed. For unloaded vessels (every vessel
                // in the Tracking Station, every non-active vessel in flight) Vessel.parts is
                // empty and Vessel.GetCrewCount() returns 0, so the legacy comparison below
                // (parts.Count == protoPartSnapshots.Count) was always false in TS, forcing a
                // destructive destroy-then-Load on every wire-side update. That destructive
                // path triggers the persistentId-collision storm + Vessel.Start NREs documented
                // in Post-Start sanity. Comparing against the in-world ProtoVessel restores the
                // intended early-out for the no-structural-change case regardless of load state.
                var existingPartCount = existingVessel.loaded
                    ? existingVessel.parts.Count
                    : (existingVessel.protoVessel?.protoPartSnapshots?.Count ?? -1);
                var existingCrewCount = existingVessel.loaded
                    ? existingVessel.GetCrewCount()
                    : (existingVessel.protoVessel?.GetVesselCrew()?.Count ?? -1);

                if (!forceReload && existingPartCount == vesselProto.protoPartSnapshots.Count &&
                    existingCrewCount == vesselProto.GetVesselCrew().Count)
                {
                    //Maneuver-node-only updates always land here (a flight plan edit never
                    //changes part or crew count), so we must still ingest the incoming flight
                    //plan even though we skip the reload. Without this the vessel keeps its
                    //stale flight plan and the PatchedConicSolver loads empty/old maneuver
                    //data on the next GoOffRails, silently dropping synced burns on handoff.
                    if (existingVessel.protoVessel != null)
                        existingVessel.protoVessel.flightPlan = vesselProto.flightPlan;

                    //Structure matches — no work needed. Returning the dedicated outcome
                    //lets the caller skip the misleading "Vessel reloaded" log line and
                    //the VesselReloadEvent fire that previously ran on every wire-drift
                    //broadcast even when nothing actually changed.
                    return VesselLoadOutcome.UnchangedEarlyOut;
                }

                LunaLog.Log($"[LMP]: Reloading vessel {vesselProto.vesselID}");
                if (reloadingOwnVessel)
                    existingVessel.RemoveAllCrew();

                FlightGlobals.RemoveVessel(existingVessel);
                // Mirror VesselRemoveSystem.KillVessel: also drop the existing ProtoVessel from
                // flightState.protoVessels. FlightGlobals.RemoveVessel removes the live Vessel
                // and its loaded-part registry entries, but it does NOT clean up flightState's
                // ProtoVessel list, which keeps every protoPartSnapshot.persistentId of the
                // existing vessel registered in FlightGlobals.PersistentUnloadedPartIds. Without
                // this cleanup the next vesselProto.Load() below collides on every part, stock
                // KSP renames each one mid-Load, intra-vessel cross-references
                // (parent_persistentId, srfN_persistentId, attN_persistentId) and stock
                // VesselModule snapshots taken in Awake then resolve to null, and the new vessel
                // NREs in Vessel.RebuildCrewList()/CommNetVessel.UpdateComm() the moment
                // Vessel.Start() runs the next frame.
                HighLogic.CurrentGame.flightState.protoVessels.RemoveAll(v => v == null || v.vesselID == existingVessel.id);
                // Disable immediately so Unity stops calling FixedUpdate on this vessel before
                // Object.Destroy is processed — same deferred-destroy race that causes
                // Vessel.UpdateCaches() NullReferenceExceptions (see VesselRemoveSystem.KillVessel).
                existingVessel.gameObject.SetActive(false);
                foreach (var part in existingVessel.parts)
                {
                    Object.Destroy(part.gameObject);
                }
                Object.Destroy(existingVessel.gameObject);
            }
            else
            {
                LunaLog.Log($"[LMP]: Loading vessel {vesselProto.vesselID}");
            }

            // Pre-Load DiscoveryInfo guard. Guarantees vesselProto.discoveryInfo is a
            // non-null ConfigNode with all five fields (state/lastObservedTime/lifetime/
            // refTime/size) present and finite. This is the precondition that keeps stock
            // KSP off its synthesise-default-then-parse-Infinity branch inside
            // ProtoVessel.Load, which is what the FormatException stack trace hits when
            // a vessel arrives with no DISCOVERY sub-node in its wire ConfigNode (typical
            // for stations/probes/relays/EVAs/flags from peers that never had to give the
            // vessel a tracking lifetime). Detailed rationale lives on EnsureSafeDiscoveryInfo.
            DiscoveryInfoSanitizer.EnsureSafeDiscoveryInfo(vesselProto);

            vesselProto.Load(HighLogic.CurrentGame.flightState);
            if (vesselProto.vesselRef == null)
            {
                LunaLog.Log($"[LMP]: Protovessel {vesselProto.vesselID} failed to create a vessel!");
                return VesselLoadOutcome.Failed;
            }

            // Strip null entries from each ProtoPartSnapshot.protoModuleCrew that stock KSP just
            // appended when wire-side crew names failed to resolve through CrewRoster. Has to run
            // AFTER vesselProto.Load (which is what populates protoModuleCrew from the wire
            // ConfigNode) and BEFORE the corruption walks / scene→FLIGHT transition. See the full
            // rationale on ScrubInvalidProtoCrew itself.
            ScrubInvalidProtoCrew(vesselProto);

            LogPostLoadVesselSanity(vesselProto);
            ScheduleDeferredPostStartSanityWalk(vesselProto);

            VesselPositionSystem.Singleton.ForceUpdateVesselPosition(vesselProto.vesselRef.id);

            vesselProto.vesselRef.protoVessel = vesselProto;
            if (vesselProto.vesselRef.isEVA)
            {
                var evaModule = vesselProto.vesselRef.FindPartModuleImplementing<KerbalEVA>();
                if (evaModule != null && evaModule.fsm != null && !evaModule.fsm.Started)
                {
                    evaModule.fsm?.StartFSM("Idle (Grounded)");
                }
                vesselProto.vesselRef.GoOnRails();
            }

            if (vesselProto.vesselRef.situation > Vessel.Situations.PRELAUNCH)
            {
                vesselProto.vesselRef.orbitDriver.updateFromParameters();
            }

            if (double.IsNaN(vesselProto.vesselRef.orbitDriver.pos.x))
            {
                LunaLog.Log($"[LMP]: Protovessel {vesselProto.vesselID} has an invalid orbit");
                return VesselLoadOutcome.Failed;
            }

            if (reloadingOwnVessel)
            {
                vesselProto.vesselRef.Load();
                vesselProto.vesselRef.RebuildCrewList();

                //Do not do the setting of the active vessel manually, too many systems are dependant of the events triggered by KSP
                FlightGlobals.ForceSetActiveVessel(vesselProto.vesselRef);

                vesselProto.vesselRef.SpawnCrew();
                foreach (var crew in vesselProto.vesselRef.GetVesselCrew())
                {
                    ProtoCrewMember._Spawn(crew);
                    if (crew.KerbalRef)
                        crew.KerbalRef.state = Kerbal.States.ALIVE;
                }

                if (KerbalPortraitGallery.Instance.ActiveCrewItems.Count != vesselProto.vesselRef.GetCrewCount())
                {
                    KerbalPortraitGallery.Instance.StartReset(FlightGlobals.ActiveVessel);
                }
            }

            //Only the destructive-reload branch and the brand-new-vessel branch reach
            //here; the structure-matches early-out returned UnchangedEarlyOut above.
            return hadExistingVessel ? VesselLoadOutcome.Reloaded : VesselLoadOutcome.FreshlyLoaded;
        }

        #endregion
    }
}
