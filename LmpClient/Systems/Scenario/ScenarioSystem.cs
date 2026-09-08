using LmpClient.Base;
using LmpClient.Extensions;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.ShareContracts;
using LmpClient.Utilities;
using LmpCommon;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Expansions;
using UniLinq;

namespace LmpClient.Systems.Scenario
{
    public class ScenarioSystem : MessageSystem<ScenarioSystem, ScenarioMessageSender, ScenarioMessageHandler>
    {
        #region Fields

        private ConcurrentDictionary<string, string> CheckData { get; } = new ConcurrentDictionary<string, string>();
        public ConcurrentQueue<ScenarioEntry> ScenarioQueue { get; private set; } = new ConcurrentQueue<ScenarioEntry>();

        // ReSharper disable once InconsistentNaming
        private static readonly ConcurrentDictionary<string, Type> _allScenarioTypesInAssemblies = new ConcurrentDictionary<string, Type>();
        private static ConcurrentDictionary<string, Type> AllScenarioTypesInAssemblies
        {
            get
            {
                if (!_allScenarioTypesInAssemblies.Any())
                {
                    var scenarioTypes = AssemblyLoader.loadedAssemblies
                        .SelectMany(a => a.assembly.GetLoadableTypes())
                        .Where(s => s.IsSubclassOf(typeof(ScenarioModule)) && !_allScenarioTypesInAssemblies.ContainsKey(s.Name));

                    foreach (var scenarioType in scenarioTypes)
                        _allScenarioTypesInAssemblies.TryAdd(scenarioType.Name, scenarioType);
                }

                return _allScenarioTypesInAssemblies;
            }
        }

        private static List<string> ScenarioName { get; } = new List<string>();
        private static List<byte[]> ScenarioData { get; } = new List<byte[]>();
        #endregion

        #region Base overrides

        public override string SystemName { get; } = nameof(ScenarioSystem);

        protected override bool ProcessMessagesInUnityThread => false;

        protected override void OnEnabled()
        {
            base.OnEnabled();
            //Run it every 30 seconds
            SetupRoutine(new RoutineDefinition(30000, RoutineExecution.Update, SendScenarioModules));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            CheckData.Clear();
            ScenarioQueue = new ConcurrentQueue<ScenarioEntry>();
            AllScenarioTypesInAssemblies.Clear();
        }

        private static readonly List<Tuple<string, ConfigNode>> ScenariosConfigNodes = new List<Tuple<string, ConfigNode>>();

        #endregion

        #region Public methods

        public void LoadMissingScenarioDataIntoGame()
        {
            //ResourceScenario.Instance.Load();

            var validScenarios = KSPScenarioType.GetAllScenarioTypesInAssemblies()
                .Where(s => !HighLogic.CurrentGame.scenarios.Exists(psm => psm.moduleName == s.ModuleType.Name) 
                            && LoadModuleByGameMode(s)
                            && IsDlcScenarioInstalled(s.ModuleType.Name));

            foreach (var validScenario in validScenarios)
            {
                LunaLog.Log($"[LMP]: Creating new scenario module {validScenario.ModuleType.Name}");
                HighLogic.CurrentGame.AddProtoScenarioModule(validScenario.ModuleType, validScenario.ScenarioAttributes.TargetScenes);
            }
        }

        /// <summary>
        /// Check if the scenario has changed and sends it to the server
        /// </summary>
        public void SendScenarioModules()
        {
            if (Enabled)
            {
                try
                {
                    var modules = ScenarioRunner.GetLoadedModules().Where(s=> s != null);
                    ParseModulesToConfigNodes(modules);
                    TaskFactory.StartNew(SendModulesConfigNodes);
                }
                catch (Exception e)
                {
                    LunaLog.LogError($"Error while trying to send the scenario modules!. Details {e}");
                }
            }
        }

        /// <summary>
        /// Immediately saves the named <see cref="ScenarioModule"/> and sends it to the server,
        /// bypassing the 30-second periodic send. Updates the hash-check cache so the next
        /// periodic send skips the module if it has not changed again.
        /// Must be called from the Unity main thread because <see cref="ScenarioModule.Save"/>
        /// may invoke Localisation APIs that require the main thread.
        /// </summary>
        public void SendScenarioModuleImmediate(string moduleName)
        {
            if (!Enabled) return;
            try
            {
                var module = ScenarioRunner.GetLoadedModules()
                    .FirstOrDefault(m => m != null && m.GetType().Name == moduleName);
                if (module == null)
                {
                    LunaLog.LogWarning($"[LMP]: SendScenarioModuleImmediate — '{moduleName}' not found in loaded modules.");
                    return;
                }

                var configNode = new ConfigNode();
                module.Save(configNode);
                var scenarioBytes = configNode.Serialize();
                if (scenarioBytes.Length == 0)
                {
                    LunaLog.LogWarning($"[LMP]: SendScenarioModuleImmediate — '{moduleName}' serialized to empty bytes.");
                    return;
                }

                CheckData[moduleName] = Common.CalculateSha256Hash(scenarioBytes);

                var names = new List<string> { moduleName };
                var dataList = new List<byte[]> { scenarioBytes };
                TaskFactory.StartNew(() => MessageSender.SendScenarioModuleData(names, dataList));
                LunaLog.Log($"[LMP]: Sent immediate scenario for '{moduleName}' ({scenarioBytes.Length} bytes).");
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error sending immediate scenario for '{moduleName}': {e}");
            }
        }

        /// <summary>
        /// This transforms the scenarioModule to a config node. We cannot do this in another thread as Lingoona 
        /// is called sometimes and that makes a hard crash
        /// </summary>
        private static void ParseModulesToConfigNodes(IEnumerable<ScenarioModule> modules)
        {
            ScenariosConfigNodes.Clear();
            foreach (var scenarioModule in modules)
            {
                var scenarioType = scenarioModule.GetType().Name;

                if (IgnoredScenarios.IgnoreSend.Contains(scenarioType))
                    continue;

                if (!IsScenarioModuleAllowed(scenarioType))
                    continue;

                var configNode = new ConfigNode();
                scenarioModule.Save(configNode);

                ScenariosConfigNodes.Add(new Tuple<string, ConfigNode>(scenarioType, configNode));
            }
        }

        /// <summary>
        /// Sends the parsed config nodes to the server after doing basic checks
        /// </summary>
        private void SendModulesConfigNodes()
        {
            ScenarioData.Clear();
            ScenarioName.Clear();

            foreach (var scenarioConfigNode in ScenariosConfigNodes)
            {
                var scenarioBytes = scenarioConfigNode.Item2.Serialize();
                var scenarioHash = Common.CalculateSha256Hash(scenarioBytes);

                if (scenarioBytes.Length == 0)
                {
                    LunaLog.Log($"[LMP]: Error writing scenario data for {scenarioConfigNode.Item1}");
                    continue;
                }

                //Data is the same since last time - Skip it.
                if (CheckData.ContainsKey(scenarioConfigNode.Item1) && CheckData[scenarioConfigNode.Item1] == scenarioHash) continue;

                CheckData[scenarioConfigNode.Item1] = scenarioHash;

                ScenarioName.Add(scenarioConfigNode.Item1);
                ScenarioData.Add(scenarioBytes);
            }

            if (ScenarioName.Any())
                MessageSender.SendScenarioModuleData(ScenarioName, ScenarioData);
        }

        public void LoadScenarioDataIntoGame()
        {
            while (ScenarioQueue.TryDequeue(out var scenarioEntry))
            {
                LoadSingleScenarioEntry(scenarioEntry);
            }
        }

        /// <summary>
        /// Loads a single dequeued scenario entry into the active game. Isolated into its
        /// own method so that an exception thrown while constructing or registering one
        /// ProtoScenarioModule cannot abort the entire load loop and leave the player
        /// stuck on the loading screen with a partially-populated game state. Each entry
        /// is logged by module name on entry and on failure so that bad scenarios can be
        /// identified from the log without an attached debugger.
        /// </summary>
        private static void LoadSingleScenarioEntry(ScenarioEntry scenarioEntry)
        {
            var moduleName = scenarioEntry?.ScenarioModule ?? "<unknown>";

            try
            {
                if (scenarioEntry == null || scenarioEntry.ScenarioNode == null)
                {
                    LunaLog.LogError($"[LMP]: Skipping scenario entry '{moduleName}' because its data is missing.");
                    WriteNullScenarioDebugLog(null);
                    return;
                }

                // Short-circuit on the wire-level module name BEFORE constructing
                // ProtoScenarioModule. The stock ctor calls ConfigNode.CopyTo, which
                // NREs in CopyToRecursive when a scenario file on the server can't
                // round-trip through LunaConfigNode (e.g. CivilianPopulation's
                // repoJSON value contains literal '{' / '}'). If we know up front we
                // wouldn't load this module anyway, don't even risk the ctor.
                if (IgnoredScenarios.IgnoreReceive.Contains(moduleName))
                {
                    LunaLog.Log($"[LMP]: Ignoring {moduleName} scenario data (IgnoreReceive)");
                    return;
                }

                // Strip any shared contracts that reference parts not installed on this
                // client BEFORE handing the node to ProtoScenarioModule. KSP's stock
                // scenario loader will otherwise pass each contract through
                // ContractConfigurator's PartValidation parameter loader during scene
                // transition, which throws ArgumentException and surfaces the in-game
                // exception popup. Safe because ContractSystem is in
                // IgnoredScenarios.IgnoreSend (clients never echo it back to the server)
                // and live contract changes flow through ShareContractsSystem instead.
                if (moduleName == ContractsScenarioSanitizer.ScenarioModuleName)
                {
                    ContractsScenarioSanitizer.StripContractsReferencingUnknownParts(scenarioEntry.ScenarioNode);
                }

                LunaLog.Log($"[LMP]: Processing {moduleName} scenario data");

                if (scenarioEntry.ScenarioNode == null)
                {
                    LunaLog.LogError(
                        $"[LMP]: Skipping scenario '{scenarioEntry.ScenarioModule}' with null ConfigNode. See NullScenario.log in your KSP install folder.");
                    WriteNullScenarioDebugLog(scenarioEntry);
                    return;
                }

                if (scenarioEntry.ScenarioModule == "ContractPreLoader")
                {
                    // Inject the full Offered contract nodes that were saved when the server's
                    // ContractSystem scenario was received.  KSPCF's ContractPreLoader.OnLoad
                    // will deserialise these and store them internally.  When CC's
                    // onContractsLoaded handler subsequently calls GenerateContracts(0), KSPCF's
                    // patched implementation restores every contract whose GUID is in
                    // ContractPreLoader's list rather than clearing them all.
                    // Without this injection the list is empty → all 43 server contracts are
                    // cleared → 0 Available in Mission Control for non-lock-holders.
                    try
                    {
                        InjectServerContractsIntoPreLoader(scenarioEntry.ScenarioNode);
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[ContractPreLoader]: Error injecting server contracts into ContractPreLoader node: {e.Message}");
                    }
                }

                if (scenarioEntry.ScenarioModule == "ContractSystem")
                {
                    try
                    {
                        var migrated = MigrateFinishedContractsIntoMain(scenarioEntry.ScenarioNode);
                        if (migrated > 0)
                            LunaLog.Log($"[ShareContracts]: Migrated {migrated} contract(s) from CONTRACTS_FINISHED into CONTRACTS with correct state so ReconcileFinishedContracts can place them in the Archive tab.");
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[ShareContracts]: Error migrating CONTRACTS_FINISHED into CONTRACTS: {e.Message}. The scenario will be loaded as-is.");
                    }

                    Dictionary<string, (string TypeName, string MissingAsset)> stripped = null;
                    try
                    {
                        stripped = StripContractsWithMissingParts(scenarioEntry.ScenarioNode);
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[ShareContracts]: Error while pre-filtering ContractSystem scenario data: {e.Message}. The scenario will be loaded as-is.");
                    }

                    try
                    {
                        ShareContracts.ShareContractsSystem.Singleton?.PrepareUnavailableContractStubs(scenarioEntry.ScenarioNode, stripped);
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[ShareContracts]: Error while preparing unavailability stubs: {e.Message}.");
                    }
                }

                try
                {
                    var psm = new ProtoScenarioModule(scenarioEntry.ScenarioNode);
                    if (IsScenarioModuleAllowed(psm.moduleName) && !IgnoredScenarios.IgnoreReceive.Contains(psm.moduleName))
                    {
                        LunaLog.Log($"[LMP]: Loading {psm.moduleName} scenario data");
                        HighLogic.CurrentGame.scenarios.Add(psm);
                    }
                    else
                    {
                        LunaLog.Log($"[LMP]: Skipping {psm.moduleName} scenario data in {SettingsSystem.ServerSettings.GameMode} mode");
                    }
                }
                catch (Exception e)
                {
                    LunaLog.LogError(
                        $"[LMP]: Failed to apply scenario '{scenarioEntry.ScenarioModule}' (ConfigNode could not be copied into ProtoScenarioModule). {e}");
                    return;
                }

            }
            catch (Exception e)
            {
                // Swallow per-entry so the rest of the queue can still load. The user
                // will be missing this one scenario module's state (e.g. an empty
                // contracts board) but the game will still come up rather than NRE'ing
                // out of MainSystem.Update and stranding the player.
                LunaLog.LogError($"[LMP]: Failed to load '{moduleName}' scenario data; skipping it. Exception: {e}");
            }
        }

        /// <summary>
        /// Injects the full CONTRACT nodes from the server snapshot into the ContractPreLoader
        /// scenario node so that KSPCF's ContractPreLoader.OnLoad will store them and
        /// subsequently restore them when ContractConfigurator triggers
        /// <c>ContractSystem.GenerateContracts</c> from its <c>onContractsLoaded</c> handler.
        ///
        /// KSPCF's patched <c>GenerateContracts</c> uses ContractPreLoader's in-memory list as
        /// a whitelist.  Any contract not in that list is removed.  With an empty list every
        /// server contract is cleared and, since <c>generateContractIterations == 0</c> for
        /// non-lock-holders, nothing new is generated → 0 Available.  With full nodes in the
        /// list KSPCF recognises and restores all server contracts.
        ///
        /// The nodes come from <see cref="ShareContracts.ShareContractsSystem.ServerOfferedContractNodes"/>,
        /// which is populated in <see cref="ScenarioMessageHandler.QueueScenarioBytes"/> when
        /// the server's ContractSystem data arrives (well before loading begins).
        /// </summary>
        private static void InjectServerContractsIntoPreLoader(ConfigNode preLoaderNode)
        {
            var nodes = ShareContracts.ShareContractsSystem.Singleton?.ServerOfferedContractNodes;
            if (nodes == null || nodes.Count == 0)
            {
                LunaLog.Log("[ContractPreLoader]: No server Offered contract nodes to inject.");
                return;
            }

            // Build a set of GUIDs already present so we never create duplicates.
            var existingGuids = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode child in preLoaderNode.nodes)
            {
                var g = child.GetValue("guid") ?? child.GetValue("id");
                if (!string.IsNullOrEmpty(g))
                    existingGuids.Add(g);
            }

            var injected = 0;
            foreach (var contractNode in nodes)
            {
                var guid = contractNode.GetValue("guid");
                if (string.IsNullOrEmpty(guid) || existingGuids.Contains(guid))
                    continue;

                // KSPCF's ContractPreLoader.OnLoad calls Contract.Load(node) on each child
                // outside the ContractSystem.LoadContract path that LMP's Harmony finalizer
                // guards, so exceptions there are never suppressed.  Apply the same body-index
                // validation here so contracts that would cause CC's ParseCelestialBodyValue
                // to throw ArgumentException never reach that code path.
                var typeName = contractNode.GetValue("type") ?? string.Empty;
                var isCC = string.Equals(typeName, "ConfiguredContract", StringComparison.OrdinalIgnoreCase);
                var invalidBody = FindInvalidBodyIndex(contractNode, isCC);
                if (invalidBody != null)
                {
                    LunaLog.LogWarning($"[ContractPreLoader]: Skipping injection of contract {guid} ({typeName}) — {invalidBody}.");
                    continue;
                }

                var missingBody = FindMissingBodyReference(contractNode, isCC);
                if (missingBody != null)
                {
                    LunaLog.LogWarning($"[ContractPreLoader]: Skipping injection of contract {guid} ({typeName}) — {missingBody}.");
                    continue;
                }

                // Add the full CONTRACT node (same name KSP uses in ContractSystem.OnSave).
                // KSPCF's ContractPreLoader.OnLoad calls Contract.Load(node) on each child,
                // which requires type, guid, state, and all parameters to succeed.
                preLoaderNode.AddNode(contractNode);
                existingGuids.Add(guid);
                injected++;
            }

            LunaLog.Log($"[ContractPreLoader]: Injected {injected} full CONTRACT nodes into ContractPreLoader ({nodes.Count} server Offered contracts).");
        }

        /// <summary>
        /// Corrects a server data inconsistency where completed contracts are stored in both
        /// <c>CONTRACTS</c> (with a stale <c>Active</c> state) and <c>CONTRACTS_FINISHED</c>
        /// (with the authoritative <c>Completed</c> state and fully-resolved parameter states).
        ///
        /// KSP's <c>ContractSystem.OnLoad</c> cannot reliably load contracts from
        /// <c>CONTRACTS_FINISHED</c> — exceptions during parameter loading are suppressed by the
        /// <see cref="ContractSystem_LoadContract"/> finalizer, leaving
        /// <c>ContractSystem.ContractsFinished</c> empty.  The designed path for getting finished
        /// contracts into the Archive tab is: load them from <c>CONTRACTS</c> with the correct
        /// finished state, then let <see cref="ShareContractsEvents.ReconcileFinishedContracts"/>
        /// move them from <c>ContractSystem.Contracts</c> to <c>ContractSystem.ContractsFinished</c>.
        ///
        /// This method replaces each stale <c>CONTRACTS</c> entry with the authoritative node
        /// from <c>CONTRACTS_FINISHED</c>, then removes those entries from <c>CONTRACTS_FINISHED</c>
        /// so KSP does not attempt a second (failing) load from there.
        /// </summary>
        /// <returns>The number of contract entries migrated.</returns>
        private static int MigrateFinishedContractsIntoMain(ConfigNode scenarioNode)
        {
            var contractsNode = scenarioNode.GetNode("CONTRACTS");
            var finishedNode  = scenarioNode.GetNode("CONTRACTS_FINISHED");
            if (contractsNode == null || finishedNode == null) return 0;

            // Index CONTRACTS_FINISHED by GUID.
            var finishedByGuid = new System.Collections.Generic.Dictionary<string, ConfigNode>(StringComparer.OrdinalIgnoreCase);
            foreach (var finishedContract in finishedNode.GetNodes("CONTRACT"))
            {
                var guid = finishedContract.GetValue("guid");
                if (!string.IsNullOrEmpty(guid))
                    finishedByGuid[guid] = finishedContract;
            }

            if (finishedByGuid.Count == 0) return 0;

            // Rebuild CONTRACTS, swapping stale Active entries for their authoritative counterparts.
            var contractNodes = contractsNode.GetNodes("CONTRACT");
            contractsNode.ClearNodes();
            var migrated = 0;
            foreach (var contractNode in contractNodes)
            {
                var guid = contractNode.GetValue("guid");
                if (!string.IsNullOrEmpty(guid) && finishedByGuid.TryGetValue(guid, out var authoritative))
                {
                    LunaLog.LogWarning($"[ShareContracts]: Replacing CONTRACTS entry for {guid} ({contractNode.GetValue("type") ?? "Unknown"}) " +
                                       $"with authoritative CONTRACTS_FINISHED node (state: {contractNode.GetValue("state")} → {authoritative.GetValue("state")}).");
                    contractsNode.AddNode(authoritative);
                    migrated++;
                }
                else
                {
                    contractsNode.AddNode(contractNode);
                }
            }

            if (migrated == 0) return 0;

            // Remove migrated entries from CONTRACTS_FINISHED so KSP does not attempt
            // a second load of the same contracts from there.
            var finishedNodes = finishedNode.GetNodes("CONTRACT");
            finishedNode.ClearNodes();
            foreach (var fc in finishedNodes)
            {
                var guid = fc.GetValue("guid");
                if (string.IsNullOrEmpty(guid) || !finishedByGuid.ContainsKey(guid))
                    finishedNode.AddNode(fc);
                // Entries that were migrated are intentionally dropped from CONTRACTS_FINISHED.
            }

            return migrated;
        }

        /// <summary>
        /// Removes CONTRACT nodes from the ContractSystem scenario that would cause
        /// <see cref="Contracts.ContractSystem.LoadContract"/> to throw an unhandled exception on
        /// this client. Currently detects:
        /// <list type="bullet">
        ///   <item>References to part names absent from <see cref="PartLoader"/> (e.g. mod parts the
        ///         client does not have installed).</item>
        ///   <item>Celestial body indices (integer-format "body" / "targetBody" values) that are
        ///         out of range for this client's <see cref="FlightGlobals.Bodies"/> list (e.g. the
        ///         server has a planet pack the client does not have).</item>
        /// </list>
        /// Stripped contracts are returned so that <see cref="ShareContractsSystem.PrepareUnavailableContractStubs"/>
        /// can create informative <see cref="ShareContracts.LmpUnavailableContract"/> stubs for them.
        /// </summary>
        private static Dictionary<string, (string TypeName, string MissingAsset)> StripContractsWithMissingParts(ConfigNode scenarioNode)
        {
            var strippedOut = new Dictionary<string, (string TypeName, string MissingAsset)>();
            StripContractSectionWithMissingParts(scenarioNode, "CONTRACTS", strippedOut);
            StripContractSectionWithMissingParts(scenarioNode, "CONTRACTS_FINISHED", strippedOut);
            return strippedOut;
        }

        private static void StripContractSectionWithMissingParts(ConfigNode scenarioNode, string sectionName,
            Dictionary<string, (string TypeName, string MissingAsset)> strippedOut)
        {
            var sectionNode = scenarioNode.GetNode(sectionName);
            if (sectionNode == null) return;

            var contractNodes = sectionNode.GetNodes("CONTRACT");
            sectionNode.ClearNodes();
            foreach (var contractNode in contractNodes)
            {
                var guid = contractNode.GetValue("guid");
                var typeName = contractNode.GetValue("type") ?? "Unknown";
                var isConfiguredContract = string.Equals(typeName, "ConfiguredContract", StringComparison.OrdinalIgnoreCase);

                var missingPart = FindMissingPartName(contractNode);
                if (missingPart != null)
                {
                    LunaLog.LogWarning($"[ShareContracts]: Dropping contract {guid} ({typeName}) from {sectionName} — references part '{missingPart}' which is not installed on this client.");
                    if (guid != null)
                        strippedOut[guid] = (typeName, $"part '{missingPart}'");
                    continue;
                }

                var invalidBody = FindInvalidBodyIndex(contractNode, isConfiguredContract);
                if (invalidBody != null)
                {
                    LunaLog.LogWarning($"[ShareContracts]: Dropping contract {guid} ({typeName}) from {sectionName} — {invalidBody}.");
                    if (guid != null)
                        strippedOut[guid] = (typeName, invalidBody);
                    continue;
                }

                var missingBody = FindMissingBodyReference(contractNode, isConfiguredContract);
                if (missingBody != null)
                {
                    LunaLog.LogWarning($"[ShareContracts]: Dropping contract {guid} ({typeName}) from {sectionName} — {missingBody}.");
                    if (guid != null)
                        strippedOut[guid] = (typeName, missingBody);
                    continue;
                }

                sectionNode.AddNode(contractNode);
            }
        }

        /// <summary>
        /// Recursively searches a contract ConfigNode for any "part = X" value where X is not a
        /// recognised part in PartLoader. Returns the first missing part name found, or null if all
        /// referenced parts are present.
        /// </summary>
        private static string FindMissingPartName(ConfigNode node)
        {
            foreach (ConfigNode.Value v in node.values)
            {
                if (v.name == "part" && PartLoader.getPartInfoByName(v.value) == null)
                    return v.value;
            }
            foreach (ConfigNode childNode in node.nodes)
            {
                var missing = FindMissingPartName(childNode);
                if (missing != null) return missing;
            }
            return null;
        }

        /// <summary>
        /// Set of ConfigNode value names that KSP contract parameters use as integer celestial-body
        /// indices.  Any integer stored under one of these keys is validated against
        /// <see cref="FlightGlobals.Bodies"/>; an out-of-range value means this client is missing
        /// a body (e.g. a planet-pack mod) and the contract must be stripped to prevent
        /// <see cref="ArgumentOutOfRangeException"/> spam from parameters like
        /// <c>ReachDestination.OnLoad</c>, <c>CollectScience.OnLoad</c>, etc.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> BodyIndexKeys =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "body",         // most parameters (CollectScience, ReachSpace, etc.)
                "targetBody",   // survey / plant flag parameters
                "destination",  // ReachDestination
                "origin",       // return-from-body parameters
                "body1",        // flyby / multi-body parameters
                "body2",
            };

        /// <summary>
        /// ConfigNode value keys found exclusively (or almost exclusively) in CC contract
        /// parameters that also require a body-target key (<see cref="BodyIndexKeys"/>).
        /// If a CC PARAM node contains any of these keys but none of the body-target keys,
        /// the node is missing a required field that CC's <c>ConfigNodeUtil.ParseValue</c>
        /// would throw <see cref="System.ArgumentException"/> for when loading.
        ///
        /// Examples: <c>SCANsatCoverage</c> requires both <c>coverage</c>/<c>scanType</c>
        /// AND <c>targetBody</c>; if <c>targetBody</c> was never saved (malformed server data),
        /// CC shows an in-game popup and the contract cannot be displayed.
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> BodyContextKeys =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // SCANsat integration parameters
                "coverage", "scanType", "scanMode",
                // CC science / biome / situation parameters
                "experiment", "biome", "situation",
                // CC orbital / altitude parameters
                "orbitType", "minOrbit", "maxOrbit",
                "minAltitude", "maxAltitude",
                "minPeriapsis", "maxPeriapsis",
                "minApoapsis", "maxApoapsis",
                "minEccentricity", "maxEccentricity",
                "minInclination", "maxInclination",
            };

        /// <summary>
        /// Recursively searches a contract ConfigNode for any value whose key is a recognised
        /// celestial-body index field and whose integer value is invalid for this client.
        ///
        /// For ordinary (non-CC) contracts, only out-of-range indices are flagged because KSP
        /// resolves body references by integer index.
        ///
        /// For ContractConfigurator contracts (<paramref name="isConfiguredContract"/> = true),
        /// <em>any</em> integer value is invalid: CC's <c>ParseCelestialBodyValue</c> resolves
        /// bodies by name and throws <see cref="System.ArgumentException"/> when given a numeric
        /// string such as <c>"5"</c>, even if that index is in range for
        /// <see cref="FlightGlobals.Bodies"/>.  Such values can appear when a contract was
        /// serialised by an older CC build or by KSPCF's ContractPreLoader using the raw body
        /// index rather than the body name.
        ///
        /// Returns a human-readable description on mismatch, or null if all body references are valid.
        /// </summary>
        private static string FindInvalidBodyIndex(ConfigNode node, bool isConfiguredContract = false)
        {
            // Guard: if Bodies hasn't been populated yet (shouldn't happen at load time, but be safe).
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0)
                return null;

            foreach (ConfigNode.Value v in node.values)
            {
                if (!BodyIndexKeys.Contains(v.name)) continue;

                int idx;
                if (int.TryParse(v.value, out idx))
                {
                    if (idx < 0 || idx >= FlightGlobals.Bodies.Count)
                        return $"body index #{idx} (key '{v.name}') out of range";
                    // CC contracts always store body names; any in-range integer is also invalid
                    // because ParseCelestialBodyValue only accepts names, not numeric strings.
                    if (isConfiguredContract)
                        return $"body index #{idx} (key '{v.name}') — CC contract expects a body name, not an integer";
                }
                else if (double.TryParse(v.value,
                             System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture,
                             out var dbl)
                         && dbl >= 0
                         && dbl == System.Math.Floor(dbl))
                {
                    // KSP occasionally serialises body indices as floats (e.g. "17" → "17.0").
                    idx = (int)dbl;
                    if (idx >= FlightGlobals.Bodies.Count)
                        return $"body index #{idx} (key '{v.name}', stored as float) out of range";
                    if (isConfiguredContract)
                        return $"body index #{idx} (key '{v.name}', stored as float) — CC contract expects a body name, not an integer";
                }
            }
            foreach (ConfigNode childNode in node.nodes)
            {
                var invalid = FindInvalidBodyIndex(childNode, isConfiguredContract);
                if (invalid != null) return invalid;
            }
            return null;
        }

        /// <summary>
        /// Searches the direct PARAM child nodes of a <strong>CC ConfiguredContract</strong>
        /// node for parameters that declare body-context fields (e.g. <c>coverage</c>,
        /// <c>scanType</c>, <c>situation</c>) without any corresponding body-target field
        /// (e.g. <c>targetBody</c>).  Such nodes are malformed — CC's
        /// <c>ConfigNodeUtil.ParseValue&lt;CelestialBody&gt;</c> will throw
        /// <see cref="System.ArgumentException"/> and display an in-game popup when it
        /// tries to load a required body field that is absent.
        ///
        /// Only meaningful for CC contracts; always returns <c>null</c> for non-CC contracts.
        /// </summary>
        /// <returns>
        /// A human-readable description of the problem (e.g.
        /// <c>"PARAM 'SCANsatCoverage' has body-context field but no body target"</c>),
        /// or <c>null</c> if no issue is found.
        /// </returns>
        private static string FindMissingBodyReference(ConfigNode contractNode, bool isConfiguredContract)
        {
            if (!isConfiguredContract) return null;

            foreach (ConfigNode child in contractNode.nodes)
            {
                if (!string.Equals(child.name, "PARAM", StringComparison.OrdinalIgnoreCase)) continue;

                var hasBodyContextKey = false;
                var hasBodyKey = false;
                foreach (ConfigNode.Value v in child.values)
                {
                    if (BodyContextKeys.Contains(v.name)) hasBodyContextKey = true;
                    if (BodyIndexKeys.Contains(v.name)) hasBodyKey = true;
                }

                if (hasBodyContextKey && !hasBodyKey)
                {
                    var paramName = child.GetValue("name") ?? "unknown";
                    return $"CC PARAM '{paramName}' has body-context field but is missing a required body target (targetBody/body)";
                }

                // Recurse into nested PARAM nodes.
                var nested = FindMissingBodyReference(child, isConfiguredContract: true);
                if (nested != null) return nested;
            }

            return null;
        }

        #endregion

        #region Private methods

        private static void WriteNullScenarioDebugLog(ScenarioEntry entry)
        {
            try
            {
                var path = Path.Combine(MainSystem.KspPath, "NullScenario.log");
                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine($"UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
                if (entry == null)
                {
                    sb.AppendLine("ScenarioEntry: null");
                    sb.AppendLine();
                    File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
                    return;
                }

                sb.AppendLine($"ScenarioModule: {entry.ScenarioModule ?? "(null)"}");
                sb.AppendLine($"ScenarioNode: {(entry.ScenarioNode == null ? "null" : "non-null (unexpected in this log)")}");
                sb.AppendLine($"RawNumBytes (from network): {entry.RawNumBytes}");
                if (entry.RawScenarioBytes != null && entry.RawScenarioBytes.Length > 0)
                {
                    sb.AppendLine($"RawScenarioBytes.Length: {entry.RawScenarioBytes.Length}");
                    sb.AppendLine();
                    sb.AppendLine("--- Payload as UTF-8 text (wire bytes before ConfigNode parse) ---");
                    sb.AppendLine(Encoding.UTF8.GetString(entry.RawScenarioBytes, 0, entry.RawScenarioBytes.Length));
                    sb.AppendLine();
                    sb.AppendLine("--- Payload as Base64 ---");
                    sb.AppendLine(Convert.ToBase64String(entry.RawScenarioBytes, 0, entry.RawScenarioBytes.Length));
                }
                else
                {
                    sb.AppendLine("RawScenarioBytes: (none — not captured or empty)");
                }

                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Failed to write NullScenario.log: {e.Message}");
            }
        }

        private static bool LoadModuleByGameMode(KSPScenarioType validScenario)
        {
            switch (HighLogic.CurrentGame.Mode)
            {
                case Game.Modes.CAREER:
                    return validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                case Game.Modes.SCIENCE_SANDBOX:
                    return validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                case Game.Modes.SANDBOX:
                    return validScenario.ScenarioAttributes.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
            }
            return false;
        }

        private static bool IsDlcScenarioInstalled(string scenarioName)
        {
            if (scenarioName == "DeployedScience" && !ExpansionsLoader.IsExpansionInstalled("Serenity"))
                return false;

            return true;
        }

        private static bool IsScenarioModuleAllowed(string scenarioName)
        {
            if (string.IsNullOrEmpty(scenarioName)) return false;

            if (scenarioName == "DeployedScience" && !ExpansionsLoader.IsExpansionInstalled("Serenity"))
                return false;

            if (!IsDlcScenarioInstalled(scenarioName))
                return false;

            if (!AllScenarioTypesInAssemblies.ContainsKey(scenarioName)) return false; //Module missing

            var scenarioType = AllScenarioTypesInAssemblies[scenarioName];

            var scenarioAttributes = (KSPScenario[])scenarioType.GetCustomAttributes(typeof(KSPScenario), true);
            if (scenarioAttributes.Length > 0)
            {
                var attribute = scenarioAttributes[0];
                var protoAllowed = false;
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER)
                {
                    protoAllowed = attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingCareerGames);
                    protoAllowed |= attribute.HasCreateOption(ScenarioCreationOptions.AddToNewCareerGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX)
                {
                    protoAllowed |= attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingScienceSandboxGames);
                    protoAllowed |= attribute.HasCreateOption(ScenarioCreationOptions.AddToNewScienceSandboxGames);
                }
                if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX)
                {
                    protoAllowed |= attribute.HasCreateOption(ScenarioCreationOptions.AddToExistingSandboxGames);
                    protoAllowed |= attribute.HasCreateOption(ScenarioCreationOptions.AddToNewSandboxGames);
                }
                return protoAllowed;
            }

            //Scenario is not marked with KSPScenario - let's load it anyway.
            return true;
        }

        #endregion
    }
}
