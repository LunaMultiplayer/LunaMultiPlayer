using Lidgren.Network;
using LmpClient.Systems.Admin;
using LmpClient.Systems.Chat;
using LmpClient.Systems.CraftLibrary;
using LmpClient.Systems.Facility;
using LmpClient.Systems.Flag;
using LmpClient.Systems.Groups;
using LmpClient.Systems.Handshake;
using LmpClient.Systems.KerbalSys;
using LmpClient.Systems.Lock;
using LmpClient.Systems.ModApi;
using LmpClient.Systems.Motd;
using LmpClient.Systems.PlayerColorSys;
using LmpClient.Systems.PlayerConnection;
using LmpClient.Systems.Scenario;
using LmpClient.Systems.Screenshot;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.ShareAchievements;
using LmpClient.Systems.ShareContracts;
using LmpClient.Systems.ShareExperimentalParts;
using LmpClient.Systems.ShareFunds;
using LmpClient.Systems.SharePurchaseParts;
using LmpClient.Systems.ShareReputation;
using LmpClient.Systems.ShareScience;
using LmpClient.Systems.ShareScienceSubject;
using LmpClient.Systems.ShareStrategy;
using LmpClient.Systems.ShareTechnology;
using LmpClient.Systems.ShareUpgradeableFacilities;
using LmpClient.Systems.Status;
using LmpClient.Systems.VesselActionGroupSys;
using LmpClient.Systems.VesselCoupleSys;
using LmpClient.Systems.VesselDecoupleSys;
using LmpClient.Systems.VesselFairingsSys;
using LmpClient.Systems.VesselFlightStateSys;
using LmpClient.Systems.VesselPartSyncCallSys;
using LmpClient.Systems.VesselPartSyncFieldSys;
using LmpClient.Systems.VesselPartSyncUiFieldSys;
using LmpClient.Systems.VesselPositionSys;
using LmpClient.Systems.VesselProtoSys;
using LmpClient.Systems.VesselRemoveSys;
using LmpClient.Systems.VesselResourceSys;
using LmpClient.Systems.VesselUndockSys;
using LmpClient.Systems.VesselUpdateSys;
using LmpClient.Systems.Warp;
using LmpClient.Systems.Waypoint;
using LmpCommon.Enums;
using LmpCommon.Message.Data.ShareProgress;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using LmpCommon.Time;
using System;
using System.Threading;

namespace LmpClient.Network
{
    public class NetworkReceiver
    {
        private const string BenignNatPunchUnhandledWarning = "Connection received unhandled library message: NatPunchMessage";

        /// <summary>
        /// Main receiving thread. Here we map each received message to the specific system
        /// </summary>
        public static void ReceiveMain()
        {
            LunaLog.Log("[LMP]: Receive thread started");
            try
            {
                while (!NetworkConnection.ResetRequested)
                {
                    while (NetworkMain.ClientConnection.Status == NetPeerStatus.NotRunning)
                    {
                        Thread.Sleep(50);
                    }

                    if (NetworkMain.ClientConnection.ReadMessage(out var msg))
                    {
                        switch (msg.MessageType)
                        {
                            case NetIncomingMessageType.DebugMessage:
                                LunaLog.Log($"[Lidgren DEBUG] {msg.ReadString()}");
                                break;
                            case NetIncomingMessageType.VerboseDebugMessage:
                                LunaLog.Log($"[Lidgren VERBOSE] {msg.ReadString()}");
                                break;
                            case NetIncomingMessageType.WarningMessage:
                                var warningText = msg.ReadString();
                                if (!string.Equals(warningText, BenignNatPunchUnhandledWarning, StringComparison.Ordinal))
                                {
                                    LunaLog.Log($"[Lidgren WARNING] {warningText}");
                                }
                                break;
                            case NetIncomingMessageType.NatIntroductionSuccess:
                                NetworkServerList.HandleNatIntroductionSuccess(msg);
                                break;
                            case NetIncomingMessageType.UnconnectedData:
                                NetworkServerList.HandleServersList(msg);
                                break;
                            case NetIncomingMessageType.ConnectionLatencyUpdated:
                                NetworkStatistics.PingSec = msg.ReadFloat();
                                break;
                            case NetIncomingMessageType.Data:
                                try
                                {
                                    var deserializedMsg = NetworkMain.SrvMsgFactory.Deserialize(msg, LunaNetworkTime.UtcNow.Ticks);
                                    if (deserializedMsg != null)
                                    {
                                        QueueMessageToSystem(deserializedMsg as IServerMessageBase);
                                    }
                                }
                                catch (Exception e)
                                {
                                    LunaLog.LogError($"[LMP]: Error deserializing message! {e}");
                                }
                                break;
                            case NetIncomingMessageType.StatusChanged:
                                switch ((NetConnectionStatus)msg.ReadByte())
                                {
                                    case NetConnectionStatus.Disconnected:
                                        var reason = msg.ReadString();
                                        NetworkConnection.Disconnect(reason);
                                        break;
                                }
                                break;
                            default:
                                LunaLog.Log($"[LMP]: LIDGREN: {msg.MessageType} -- {msg.PeekString()}");
                                break;
                        }
                        NetworkMain.ClientConnection.Recycle(msg);
                    }
                    else
                    {
                        Thread.Sleep(SettingsSystem.CurrentSettings.SendReceiveMsInterval);
                    }
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Receive thread error: {e}");
                NetworkMain.HandleDisconnectException(e);
            }
            LunaLog.Log("[LMP]: Receive thread exited");
        }

        /// <summary>
        /// Queues the received message to the correct system
        /// </summary>
        private static void QueueMessageToSystem(IServerMessageBase msg)
        {
            switch (msg.MessageType)
            {
                case ServerMessageType.Admin:
                    AdminSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Handshake:
                    HandshakeSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Chat:
                    ChatSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Settings:
                    SettingsSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.PlayerStatus:
                    StatusSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.PlayerColor:
                    PlayerColorSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.PlayerConnection:
                    PlayerConnectionSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Scenario:
                    ScenarioSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Kerbal:
                    KerbalSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Vessel:
                    switch (((VesselBaseMsgData)msg.Data).VesselMessageType)
                    {
                        case VesselMessageType.Position:
                            VesselPositionSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Flightstate:
                            VesselFlightStateSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Proto:
                            VesselProtoSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Remove:
                            VesselRemoveSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Update:
                            VesselUpdateSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Resource:
                            VesselResourceSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.PartSyncField:
                            VesselPartSyncFieldSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.PartSyncUiField:
                            VesselPartSyncUiFieldSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.PartSyncCall:
                            VesselPartSyncCallSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.ActionGroup:
                            VesselActionGroupSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Fairing:
                            VesselFairingsSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Decouple:
                            VesselDecoupleSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Couple:
                            VesselCoupleSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case VesselMessageType.Undock:
                            VesselUndockSystem.Singleton.EnqueueMessage(msg);
                            break;
                    }
                    break;
                case ServerMessageType.CraftLibrary:
                    CraftLibrarySystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Flag:
                    FlagSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Motd:
                    MotdSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Warp:
                    WarpSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Lock:
                    LockSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Mod:
                    ModApiSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Groups:
                    GroupSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Facility:
                    FacilitySystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.Waypoint:
                    WaypointSystem.Singleton.EnqueueMessage(msg);
                    break;
                case ServerMessageType.ShareProgress:
                    switch (((ShareProgressBaseMsgData)msg.Data).ShareProgressMessageType)
                    {
                        case ShareProgressMessageType.FundsUpdate:
                            ShareFundsSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.ScienceUpdate:
                            ShareScienceSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.ScienceSubjectUpdate:
                            ShareScienceSubjectSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.ReputationUpdate:
                            ShareReputationSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.TechnologyUpdate:
                            ShareTechnologySystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.ContractsUpdate:
                            ShareContractsSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.AchievementsUpdate:
                            ShareAchievementsSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.StrategyUpdate:
                            ShareStrategySystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.FacilityUpgrade:
                            ShareUpgradeableFacilitiesSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.PartPurchase:
                            SharePurchasePartsSystem.Singleton.EnqueueMessage(msg);
                            break;
                        case ShareProgressMessageType.ExperimentalPart:
                            ShareExperimentalPartsSystem.Singleton.EnqueueMessage(msg);
                            break;
                    }
                    break;
                case ServerMessageType.Screenshot:
                    ScreenshotSystem.Singleton.EnqueueMessage(msg);
                    break;
                default:
                    LunaLog.LogError($"[LMP]: Unhandled Message type {msg.MessageType}");
                    break;
            }
        }
    }
}
