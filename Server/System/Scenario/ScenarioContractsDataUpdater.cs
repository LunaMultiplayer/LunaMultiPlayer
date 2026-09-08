using LmpCommon.Message.Data.ShareProgress;
using LunaConfigNode.CfgNode;
using System.Collections.Generic;
using System.Text;
using Server.Log;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server.System.Scenario
{
    public partial class ScenarioDataUpdater
    {
        /// <summary>
        /// KSP stores active and finished contracts together under a single CONTRACTS parent.
        /// </summary>
        private const string ContractsParentNodeName = "CONTRACTS";

        /// <summary>
        /// Child node name KSP expects for active contracts.
        /// </summary>
        private const string ActiveContractNodeName = "CONTRACT";

        /// <summary>
        /// Parent node that KSP uses to persist BOTH active and finished contracts.
        /// KSP does not use a separate CONTRACTS_FINISHED parent; instead it stores
        /// finished entries as CONTRACT_FINISHED siblings of CONTRACT entries inside
        /// this single CONTRACTS parent.
        /// </summary>
        private const string ContractsParentNodeName = "CONTRACTS";

        /// <summary>
        /// Child node name KSP expects for active contracts.
        /// </summary>
        private const string ActiveContractNodeName = "CONTRACT";

        /// <summary>
        /// Child node name KSP expects for archived/finished contracts.
        /// Storing finished contracts as CONTRACT here causes them to load as Active
        /// and never appear in the Mission Control "Archived" tab.
        /// </summary>
        private const string FinishedContractNodeName = "CONTRACT_FINISHED";

        /// <summary>
        /// Applies incoming contract updates by state-normalizing each node and upserting by guid.
        /// The CONTRACTS parent is then rebuilt (clear + add) to avoid LunaConfigNode 1.8.1
        /// RemoveNode list-sync issues that can leave stale duplicate entries on disk.
        /// </summary>
        public static void WriteContractDataToFile(ShareProgressContractsMsgData contractsMsg)
        {
            ObserveBackgroundTask(Task.Run(() =>
            {
                try
                {
                    lock (Semaphore.GetOrAdd("ContractSystem", new object()))
                    {
                        if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue("ContractSystem", out var scenario)) return;

                        var contractsNode = scenario.GetNode(ContractsParentNodeName)?.Value;
                        if (contractsNode == null) return;

                        // Get CONTRACTS_FINISHED, creating it if the scenario pre-dates the node.
                        var finishedNodeEntry = scenario.GetNode(FinishedContractNodeName);
                        ConfigNode finishedNode;
                        if (finishedNodeEntry == null)
                        {
                            finishedNode = new ConfigNode("") { Name = FinishedContractNodeName };
                            scenario.AddNode(finishedNode);
                        }
                        else
                        {
                            finishedNode = finishedNodeEntry.Value;
                        }

                        var existingActive   = contractsNode.GetNodes(ActiveContractNodeName).Select(c => c.Value).ToArray();
                        var existingFinished = finishedNode.GetNodes(ActiveContractNodeName).Select(c => c.Value).ToArray();

                        foreach (var contract in contractsMsg.Contracts.Select(v => ParseClientConfigNode(v.Data, v.NumBytes, ActiveContractNodeName)))
                        {
                            var incomingNode = ParseClientConfigNode(contractInfo.Data, contractInfo.NumBytes, ActiveContractNodeName);
                            var stateValue = incomingNode.GetValue("state")?.Value;
                            incomingNode.Name = IsFinishedContractState(stateValue) ? FinishedContractNodeName : ActiveContractNodeName;

                            var guid = incomingNode.GetValue("guid")?.Value;
                            if (string.IsNullOrEmpty(guid))
                            {
                                unidentified.Add(incomingNode);
                                continue;
                            }

                            byGuid[guid] = incomingNode;
                        }
                        var byGuid = IndexExistingContractsByGuid(contractsNode, out var unidentified);
                        var nonContractChildren = CollectNonContractChildren(contractsNode);
                        
                        foreach (var contractInfo in contractsMsg.Contracts)
                        {
                            var incomingNode = new ConfigNode(Encoding.UTF8.GetString(contractInfo.Data, 0, contractInfo.NumBytes));
                            var stateValue = incomingNode.GetValue("state")?.Value;
                            incomingNode.Name = IsFinishedContractState(stateValue) ? FinishedContractNodeName : ActiveContractNodeName;

                            var guid = incomingNode.GetValue("guid")?.Value;
                            if (string.IsNullOrEmpty(guid))
                            {
                                unidentified.Add(incomingNode);
                                continue;
                            }
                            
                            byGuid[guid] = incomingNode;
                        }
                        
                        var survivors = new List<ConfigNode>(byGuid.Count + unidentified.Count);
                        survivors.AddRange(byGuid.Values);
                        survivors.AddRange(unidentified);
                        
                        RebuildContractsParent(contractsNode, nonContractChildren, survivors);
                    }

                }
                catch (Exception e)
                {
                    LunaLog.Error($"Error updating contract scenario data: {e}");
                }
            }));
        }

        private static void ObserveBackgroundTask(Task task)
        {
            if (task == null) return;

            var ignored = task.ContinueWith(
                t => LunaLog.Error($"Background contract update task failed: {t.Exception}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        /// <summary>
        /// Builds a guid-keyed view of the contract children currently under the parent.
        /// Entries without a guid are returned via <paramref name="unidentified"/> so they
        /// can be preserved verbatim during the rebuild.
        /// </summary>
        private static Dictionary<string, ConfigNode> IndexExistingContractsByGuid(ConfigNode contractsParent, out List<ConfigNode> unidentified)
        {
            var index = new Dictionary<string, ConfigNode>();
            unidentified = new List<ConfigNode>();

            foreach (var child in contractsParent.GetAllNodes())
            {
                if (child.Name != ActiveContractNodeName && child.Name != FinishedContractNodeName)
                    continue;

                var guid = child.GetValue("guid")?.Value;
                if (string.IsNullOrEmpty(guid))
                {
                    unidentified.Add(child);
                    continue;
                }

                index[guid] = child;
            }

            return index;
        }

        /// <summary>
        /// Returns true for contract states that KSP persists as CONTRACT_FINISHED.
        /// Mirrors KSP's Contract.State enum members that drive ContractsFinished bucketing.
        /// </summary>
        internal static bool IsFinishedContractState(string state)
        {
            if (string.IsNullOrEmpty(state)) return false;

            switch (state)
            {
                case "Completed":
                case "Cancelled":
                case "DeadlineExpired":
                case "Failed":
                case "Withdrawn":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds a guid-keyed view of the contract children currently under the parent.
        /// Entries without a guid are returned via <paramref name="unidentified"/> so they
        /// can be preserved verbatim during the rebuild.
        /// </summary>
        private static Dictionary<string, ConfigNode> IndexExistingContractsByGuid(ConfigNode contractsParent, out List<ConfigNode> unidentified)
        {
            var index = new Dictionary<string, ConfigNode>();
            unidentified = new List<ConfigNode>();

            foreach (var child in contractsParent.GetAllNodes())
            {
                if (child.Name != ActiveContractNodeName && child.Name != FinishedContractNodeName)
                    continue;

                var guid = child.GetValue("guid")?.Value;
                if (string.IsNullOrEmpty(guid))
                {
                    unidentified.Add(child);
                    continue;
                }

                index[guid] = child;
            }

            return index;
        }

        /// <summary>
        /// Returns true for contract states that KSP persists as CONTRACT_FINISHED.
        /// Mirrors KSP's Contract.State enum members that drive ContractsFinished bucketing.
        /// </summary>
        internal static bool IsFinishedContractState(string state)
        {
            if (string.IsNullOrEmpty(state)) return false;

            switch (state)
            {
                case "Completed":
                case "Cancelled":
                case "DeadlineExpired":
                case "Failed":
                case "Withdrawn":
                    return true;
                default:
                    return false;
            }
        }
    }
}
