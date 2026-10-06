using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpClient.Network;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Interface;
using System;

namespace LmpClient.Systems.CraftLibrary
{
    public class CraftLibraryMessageSender : SubSystem<CraftLibrarySystem>, IMessageSender
    {
        public void SendMessage(IMessageData msg)
        {
            NetworkSender.QueueOutgoingMessage(MessageFactory.CreateNew<CraftLibraryCliMsg>(msg));
        }

        public void SendCraftMsg(CraftEntry craft)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryDataMsgData>();
            msgData.Craft.FolderName = craft.FolderName;
            msgData.Craft.CraftName = craft.CraftName;
            msgData.Craft.CraftFolder = craft.CraftFolder ?? string.Empty;
            msgData.Craft.CraftType = craft.CraftType;

            msgData.Craft.NumBytes = craft.CraftNumBytes;

            if (msgData.Craft.Data.Length < craft.CraftNumBytes)
                msgData.Craft.Data = new byte[craft.CraftNumBytes];

            Array.Copy(craft.CraftData, msgData.Craft.Data, craft.CraftNumBytes);

            SendMessage(msgData);
        }

        public void SendCraftDeleteMsg(CraftEntry craft)
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibraryDeleteMsgData>();
            msgData.FolderName = craft.FolderName;
            msgData.CraftName = craft.CraftName;
            msgData.CraftFolder = craft.CraftFolder ?? string.Empty;
            msgData.CraftType = craft.CraftType;

            SendMessage(msgData);
        }

        public void SendSyncRequestMsg()
        {
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<CraftLibrarySyncRequestMsgData>();
            SendMessage(msgData);
        }
    }
}
