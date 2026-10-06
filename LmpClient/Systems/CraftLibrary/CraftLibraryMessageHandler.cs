using System;
using System.Collections.Concurrent;
using LmpClient.Base;
using LmpClient.Base.Interface;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;

namespace LmpClient.Systems.CraftLibrary
{
    public class CraftLibraryMessageHandler : SubSystem<CraftLibrarySystem>, IMessageHandler
    {
        public ConcurrentQueue<IServerMessageBase> IncomingMessages { get; set; } = new ConcurrentQueue<IServerMessageBase>();

        public void HandleMessage(IServerMessageBase msg)
        {
            if (!(msg.Data is CraftLibraryBaseMsgData msgData)) return;

            switch (msgData.CraftMessageType)
            {
                case CraftMessageType.CraftData:
                    var craftMsg = (CraftLibraryDataMsgData)msgData;
                    System.StoreCraft(CreateCraftEntry(craftMsg));
                    break;
                case CraftMessageType.CraftDelete:
                    var deleteMsg = (CraftLibraryDeleteMsgData)msgData;
                    System.DeleteLocalCraft(new CraftEntry
                    {
                        FolderName = deleteMsg.FolderName,
                        CraftName = deleteMsg.CraftName,
                        CraftFolder = deleteMsg.CraftFolder,
                        CraftType = deleteMsg.CraftType
                    });
                    break;
                case CraftMessageType.SyncComplete:
                    System.OnLibrarySyncComplete();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static CraftEntry CreateCraftEntry(CraftLibraryDataMsgData craftMsg)
        {
            var craft = new CraftEntry
            {
                CraftName = craftMsg.Craft.CraftName,
                CraftType = craftMsg.Craft.CraftType,
                FolderName = craftMsg.Craft.FolderName,
                CraftFolder = craftMsg.Craft.CraftFolder,
                CraftNumBytes = craftMsg.Craft.NumBytes,
                CraftData = new byte[craftMsg.Craft.NumBytes]
            };

            Array.Copy(craftMsg.Craft.Data, craft.CraftData, craftMsg.Craft.NumBytes);
            return craft;
        }
    }
}
