using Contracts;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Extensions;
using LmpClient.Systems.ShareCareer;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace LmpClient.Systems.ShareContracts
{
    public class ShareContractsMessageHandler : SubSystem<ShareContractsSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is ShareProgressBaseMsgData msgData)) return;
            if (msgData.ShareProgressMessageType != ShareProgressMessageType.ContractsUpdate) return;

            if (msgData is ShareProgressContractsMsgData data)
            {
                var contractInfos = CopyContracts(data.Contracts); //create a deep copy of the contracts array so it will not change in the future.

                LunaLog.Log("Queue ContractsUpdate.");
                ShareCareerSystem.Singleton.QueueAction(() =>
                {
                    ContractUpdate(contractInfos);
                });
            }
        }

        private static ContractInfo[] CopyContracts(ContractInfo[] contracts)
        {
            var newContracts = new ContractInfo[contracts.Length];
            for (var i = 0; i < contracts.Length; i++)
            {
                newContracts[i] = new ContractInfo(contracts[i]);
            }

            return newContracts;
        }

        private static void ContractUpdate(ContractInfo[] contractInfos)
        {
            LunaLog.Log($"[ShareContracts]: Syncing {contractInfos.Length} contract(s).");

            //Don't listen to these events for the time this message is processing.
            System.StartIgnoringEvents();
            ShareFundsSystem.Singleton.StartIgnoringEvents();
            ShareScienceSystem.Singleton.StartIgnoringEvents();
            ShareReputationSystem.Singleton.StartIgnoringEvents();
            ShareExperimentalPartsSystem.Singleton.StartIgnoringEvents();

            var failedCount = 0;
            foreach (var cInfo in contractInfos)
            {
                var incomingContract = ConvertByteArrayToContract(cInfo.Data, cInfo.NumBytes);
                if (incomingContract == null)
                {
                    failedCount++;
                    continue;
                }

                LunaLog.Log($"[ShareContracts]: Contract - GUID: {incomingContract.ContractGuid} | Title: {incomingContract.Title} | Type: {incomingContract.GetType().Name} | State: {incomingContract.ContractState}");

                var contractIndex = ContractSystem.Instance.Contracts.FindIndex(c => c.ContractGuid == cInfo.ContractGuid);

                if (contractIndex != -1)
                {
                    //found the contract in the local ContractSystem
                    UpdateContract(contractIndex, incomingContract);
                }
                else
                {
                    //There is no matching contract in the local ContractSystem
                    AddContract(incomingContract);
                }
            }

            //Listen to the events again.
            //Restore funds, science and reputation in case the contract action changed some of that.
            ShareFundsSystem.Singleton.StopIgnoringEvents(true);
            ShareScienceSystem.Singleton.StopIgnoringEvents(true);
            ShareReputationSystem.Singleton.StopIgnoringEvents(true);
            ShareExperimentalPartsSystem.Singleton.StopIgnoringEvents();

            if (failedCount > 0)
            {
                LunaScreenMsg.PostScreenMessage(
                    $"[LMP] {failedCount} contract(s) could not be loaded — missing parts or mods. Check KSP.log for details.",
                    20f,
                    ScreenMessageStyle.UPPER_LEFT,
                    Color.yellow);
            }

            GameEvents.Contract.onContractsListChanged.Fire();
            System.StopIgnoringEvents();
        }

        /// <summary>
        /// Convert a byte array to a ConfigNode and then to a Contract.
        /// If anything goes wrong it will return null.
        /// </summary>
        private static Contract ConvertByteArrayToContract(byte[] data, int numBytes)
        {
            ConfigNode node;
            try
            {
                node = data.DeserializeToConfigNode(numBytes);
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Error while deserializing contract configNode: {e}");
                return null;
            }

            if (node == null)
            {
                LunaLog.LogError("[LMP]: Error, the contract configNode was null.");
                return null;
            }

            // Extract identifying fields before modifying the node or attempting a load,
            // so failure messages always include as much context as possible.
            var contractTypeName = node.GetValue("type") ?? "Unknown";
            var contractGuid = node.GetValue("guid") ?? "Unknown";
            var partName = node.GetValue("part"); // Only present on PartTest contracts

            node.RemoveValues("type");

            var contractType = ContractSystem.GetContractType(contractTypeName);
            if (contractType == null)
            {
                // Contract's C# type isn't registered in this KSP install (e.g. the mod
                // that defines the contract class isn't installed on this client).
                // Skip it cleanly rather than letting Activator.CreateInstance NRE.
                LunaLog.Log($"[LMP]: Skipping contract (GUID: {contractGuid}) with unknown type '{contractTypeName}'. The mod that defines it is likely not installed on this client.");
                return null;
            }

            if (ContractPartReferenceChecker.TryFindUnknownPartReference(node, out var unknownPartName))
            {
                // A contract parameter (typically ContractConfigurator's PartValidation)
                // references a part that PartLoader doesn't know about on this client.
                // Rehydrating it would throw deep inside the parameter loader and spam
                // the log with a stack trace; skip it cleanly instead.
                LunaLog.Log($"[LMP]: Skipping contract '{contractTypeName}' (GUID: {contractGuid}) because it references part '{unknownPartName}' which is not installed on this client.");
                return null;
            }

            Contract contract;
            try
            {
                node.RemoveValues("type");
                var contractType = ContractSystem.GetContractType(contractTypeName);
                if (contractType == null)
                {
                    LunaLog.LogError($"[LMP]: Cannot load contract (GUID: {contractGuid}) — unknown type '{contractTypeName}'. Is a required contract mod missing?");
                    return null;
                }

                if (ContractPartReferenceChecker.TryFindUnknownPartReference(node, out var unknownPartName))
                {
                    // A contract parameter (typically ContractConfigurator's PartValidation)
                    // references a part that PartLoader doesn't know about on this client.
                    // Rehydrating it would throw deep inside the parameter loader and spam
                    // the log with a stack trace; skip it cleanly instead.
                    LunaLog.Log($"[LMP]: Skipping contract '{contractTypeName}' because it references part '{unknownPartName}' which is not installed on this client.");
                    return null;
                }

                contract = Contract.Load((Contract)Activator.CreateInstance(contractType), node);
            }
            catch (Exception e)
            {
                var partDetail = partName != null ? $", requires part: '{partName}'" : "";
                LunaLog.LogError($"[LMP]: Cannot load {contractTypeName} contract (GUID: {contractGuid}{partDetail}): {e.Message}");
                return null;
            }

            return contract;
        }

        /// <summary>
        /// Updates the local contract at given index in the ContractSystem.Instance.Contracts list
        /// with the given incomingContract data.
        /// </summary>
        private static void UpdateContract(int contractIndex, Contract incomingContract)
        {
            if (ContractSystem.Instance.Contracts[contractIndex].ContractState != incomingContract.ContractState)
            {
                //Do the same action on the contract that the incoming contract has already done.
                switch (incomingContract.ContractState)
                {
                    case Contract.State.Active:
                        ContractSystem.Instance.Contracts[contractIndex].Accept();
                        break;
                    case Contract.State.Cancelled:
                        ContractSystem.Instance.Contracts[contractIndex].Cancel();
                        break;
                    case Contract.State.Completed:
                        ContractSystem.Instance.Contracts[contractIndex].Complete();
                        break;
                    case Contract.State.Declined:
                        ContractSystem.Instance.Contracts[contractIndex].Decline();
                        break;
                    case Contract.State.Failed:
                        ContractSystem.Instance.Contracts[contractIndex].Fail();
                        break;
                    case Contract.State.Offered:
                        ContractSystem.Instance.Contracts[contractIndex].Offer();
                        break;
                    case Contract.State.Withdrawn:
                        ContractSystem.Instance.Contracts[contractIndex].Withdraw();
                        break;
                }
            }
            else
            {
                //The incoming contract has the same state as the current one (so it doesn't have changed).

                //Maybe update the parameters and trigger some parameter changed event or something simelar.

                //Or replace the complete contract and hope everything goes fine:
                ContractSystem.Instance.Contracts[contractIndex].Unregister();
                ContractSystem.Instance.Contracts[contractIndex] = incomingContract;
                if (ContractSystem.Instance.Contracts[contractIndex].ContractState == Contract.State.Active)
                {
                    ContractSystem.Instance.Contracts[contractIndex].Register();
                }
            }

            LunaLog.Log($"Contract update received - contract state changed on: {incomingContract.ContractGuid} - {incomingContract.Title}");
        }

        /// <summary>
        /// Adds a contract to the local ContractSystem.
        /// </summary>
        private static void AddContract(Contract incomingContract)
        {
            if (!incomingContract.IsFinished())
            {
                ContractSystem.Instance.Contracts.Add(incomingContract);
                var contractIndex = ContractSystem.Instance.Contracts.FindIndex(c => c.ContractGuid == incomingContract.ContractGuid);

                //Trigger the contract events manually because the incoming contract object has already the state that it should have.
                ContractSystem.Instance.Contracts[contractIndex].OnStateChange.Fire(incomingContract.ContractState);
                switch (ContractSystem.Instance.Contracts[contractIndex].ContractState)
                {
                    case Contract.State.Active:
                        ContractSystem.Instance.Contracts[contractIndex].Register();
                        GameEvents.Contract.onAccepted.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                    case Contract.State.Cancelled:
                        GameEvents.Contract.onCancelled.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        GameEvents.Contract.onFailed.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        GameEvents.Contract.onFinished.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                    case Contract.State.Completed:
                        GameEvents.Contract.onCompleted.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        GameEvents.Contract.onFinished.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                    case Contract.State.Declined:
                        GameEvents.Contract.onDeclined.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                    case Contract.State.Failed:
                        GameEvents.Contract.onFailed.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        GameEvents.Contract.onFinished.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                    case Contract.State.Offered:
                        // Do not fire the global onOffered event here. KSP systems such as
                        // ContractPreLoader listen to it and enforce slot limits by withdrawing
                        // existing server contracts when new ones are offered, which causes
                        // contracts received in earlier batches to disappear. The Available tab
                        // will still refresh because onContractsListChanged is fired at the end
                        // of ContractUpdate(), which is all the UI needs.
                        break;
                    case Contract.State.Withdrawn:
                        GameEvents.Contract.onFinished.Fire(ContractSystem.Instance.Contracts[contractIndex]);
                        break;
                }
            }
            else
            {
                incomingContract.Unregister();
                if (incomingContract.ContractState == Contract.State.Completed ||
                    incomingContract.ContractState == Contract.State.DeadlineExpired ||
                    incomingContract.ContractState == Contract.State.Failed ||
                    incomingContract.ContractState == Contract.State.Cancelled)
                {
                    ContractSystem.Instance.ContractsFinished.Add(incomingContract);
                }
            }

            LunaLog.Log($"New contract received: {incomingContract.ContractGuid} - {incomingContract.Title}");
        }
    }
}
