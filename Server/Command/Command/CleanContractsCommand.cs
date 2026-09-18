using LunaConfigNode.CfgNode;
using Server.Command.Command.Base;
using Server.Log;
using Server.System;
using Server.System.Scenario;
using System.Collections.Generic;
using System.Linq;

namespace Server.Command.Command
{
    /// <summary>
    /// Moves any finished contracts (Completed, Failed, Cancelled, DeadlineExpired, Withdrawn)
    /// that are incorrectly sitting in CONTRACTS into CONTRACTS_FINISHED, freeing up
    /// offered-contract slots. This can happen on servers that ran an older LMP build
    /// before WriteContractDataToFile was updated to handle finished states.
    /// </summary>
    public class CleanContractsCommand : SimpleCommand
    {
        private static readonly IReadOnlyCollection<string> FinishedContractStates = new HashSet<string>
        {
            "Completed", "Failed", "Cancelled", "DeadlineExpired", "Withdrawn"
        };

        public override bool Execute(string commandArgs)
        {
            if (!ScenarioStoreSystem.CurrentScenarios.TryGetValue("ContractSystem", out var scenario))
            {
                LunaLog.Normal("[CleanContracts]: ContractSystem scenario not found. Is the server running in Career mode?");
                return false;
            }

            var contractsNode = scenario.GetNode("CONTRACTS")?.Value;
            if (contractsNode == null)
            {
                LunaLog.Normal("[CleanContracts]: CONTRACTS node not found in ContractSystem scenario.");
                return false;
            }

            var finishedNodeEntry = scenario.GetNode("CONTRACTS_FINISHED");
            ConfigNode finishedNode;
            if (finishedNodeEntry == null)
            {
                finishedNode = new ConfigNode("") { Name = "CONTRACTS_FINISHED" };
                scenario.AddNode(finishedNode);
                LunaLog.Normal("[CleanContracts]: Created missing CONTRACTS_FINISHED node.");
            }
            else
            {
                finishedNode = finishedNodeEntry.Value;
            }

            var activeContracts  = contractsNode.GetNodes("CONTRACT").Select(c => c.Value).ToArray();
            var finishedContracts = finishedNode.GetNodes("CONTRACT").Select(c => c.Value).ToArray();

            var moved = 0;
            foreach (var contract in activeContracts)
            {
                var state = contract.GetValue("state")?.Value ?? string.Empty;
                if (!FinishedContractStates.Contains(state))
                    continue;

                var guid = contract.GetValue("guid")?.Value ?? "unknown";
                var type = contract.GetValue("type")?.Value ?? "unknown";

                contractsNode.RemoveNode(contract);

                var existingInFinished = finishedContracts.FirstOrDefault(n => n.GetValue("guid")?.Value == guid);
                if (existingInFinished != null)
                    finishedNode.ReplaceNode(existingInFinished, contract);
                else
                    finishedNode.AddNode(contract);

                LunaLog.Normal($"[CleanContracts]: Moved {type} ({guid}) — state: {state}");
                moved++;
            }

            if (moved == 0)
            {
                LunaLog.Normal("[CleanContracts]: No misplaced finished contracts found. Nothing to do.");
                return true;
            }

            LunaLog.Normal($"[CleanContracts]: Moved {moved} contract(s) to CONTRACTS_FINISHED. Running save to persist...");
            BackupSystem.RunSave();
            LunaLog.Normal("[CleanContracts]: Done.");
            return true;
        }
    }
}
