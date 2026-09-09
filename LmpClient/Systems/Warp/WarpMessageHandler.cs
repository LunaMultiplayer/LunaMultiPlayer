using System.Collections.Concurrent;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Systems.SettingsSys;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;

namespace LmpClient.Systems.Warp
{
    public class WarpMessageHandler : SubSystem<WarpSystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is WarpBaseMsgData msgData)) return;

            switch (msgData.WarpMessageType)
            {
                case WarpMessageType.SubspacesReply:
                    {
                        var data = (WarpSubspacesReplyMsgData)msgData;
                        for (var i = 0; i < data.SubspaceCount; i++)
                        {
                            var subspaceKey = data.Subspaces[i].SubspaceKey;
                            AddSubspace(subspaceKey, data.Subspaces[i].SubspaceTime);
                            for (var j = 0; j < data.Subspaces[i].PlayerCount; j++)
                            {
                                var player = data.Subspaces[i].Players[j];
                                if (System.ClientSubspaceList.ContainsKey(player))
                                {
                                    System.ClientSubspaceList[player] = subspaceKey;
                                }
                                else
                                {
                                    System.ClientSubspaceList.TryAdd(player, subspaceKey);
                                }
                            }
                        }

                        AddSubspace(-1, 0);//Add warping subspace

                        MainSystem.NetworkState = ClientState.WarpsubspacesSynced;
                    }
                    break;
                case WarpMessageType.NewSubspace:
                    {
                        var data = (WarpNewSubspaceMsgData)msgData;
                        AddSubspace(data.SubspaceKey, data.ServerTimeDifference);
                        if (data.PlayerCreator == SettingsSystem.CurrentSettings.PlayerName)
                        {
                            //It's our subspace that we just created so set it as ours
                            System.WaitingSubspaceIdFromServer = false;
                            System.SkipSubspaceProcess = true;
                            System.CurrentSubspace = data.SubspaceKey;
                        }
                    }
                    break;
                case WarpMessageType.ChangeSubspace:
                    {
                        var data = (WarpChangeSubspaceMsgData)msgData;
                        System.ClientSubspaceList[data.PlayerName] = data.Subspace;
                    }
                    break;
                case WarpMessageType.WarpingTime:
                    {
                        var data = (WarpingTimeMsgData)msgData;
                        if (data.PlayerName != SettingsSystem.CurrentSettings.PlayerName && !System.CurrentlyWarping)
                            System.WarpingSubspaceTime = data.ServerTimeDifference;
                    }
                    break;
                default:
                    {
                        LunaLog.LogError($"[LMP]: Unhandled WARP_MESSAGE type: {msgData.WarpMessageType}");
                        break;
                    }
            }
        }

        private static void AddSubspace(int subspaceId, double subspaceTime)
        {
            if (!System.Subspaces.ContainsKey(subspaceId))
                System.Subspaces.TryAdd(subspaceId, subspaceTime);
        }
    }
}