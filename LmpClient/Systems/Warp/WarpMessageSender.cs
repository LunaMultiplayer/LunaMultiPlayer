using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpClient.Systems.SettingsSys;
using LmpClient.Systems.TimeSync;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Interface;

namespace LmpClient.Systems.Warp
{
    public class WarpMessageSender : SubSystem<WarpSystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<WarpCliMsg>(msg)));
        }

        public void SendWarpSubspacesRequest()
        {
            TaskFactory.StartNew(() => NetworkSender.QueueOutgoingMessage(NetworkMain.CliMsgFactory.CreateNew<WarpCliMsg, WarpSubspacesRequestMsgData>()));
        }

        /// <summary>
        /// Sends the EXISTING subspace that we jumped into
        /// </summary>
        public void SendChangeSubspaceMsg(int subspaceId)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpChangeSubspaceMsgData>();
            msgData.PlayerName = SettingsSystem.CurrentSettings.PlayerName;
            msgData.Subspace = subspaceId;

            SendMessage(msgData);
        }

        /// <summary>
        /// Sends the new subspace that we jumped into
        /// </summary>
        public void SendNewSubspace()
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpNewSubspaceMsgData>();
            msgData.ServerTimeDifference = TimeSyncSystem.UniversalTime - TimeSyncSystem.ServerClockSec;
            msgData.PlayerCreator = SettingsSystem.CurrentSettings.PlayerName;
            //we don't send the SubspaceKey as it will be given by the server except when warping that we set it to -1

            SendMessage(msgData);
        }

        /// <summary>
        /// Sends our current time difference against the server clock so other players can see
        /// how far ahead/behind we are while we are warping
        /// </summary>
        public void SendWarpingTimeMsg(double serverTimeDifference)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<WarpingTimeMsgData>();
            msgData.PlayerName = SettingsSystem.CurrentSettings.PlayerName;
            msgData.ServerTimeDifference = serverTimeDifference;

            SendMessage(msgData);
        }
    }
}