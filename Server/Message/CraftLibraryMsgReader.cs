using System;
using LmpCommon.Message.Data.CraftLibrary;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class CraftLibraryMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = (CraftLibraryBaseMsgData)message.Data;

            try
            {
                switch (data.CraftMessageType)
                {
                    case CraftMessageType.SyncRequest:
                        CraftLibrarySystem.SendFullLibrary(client);
                        break;
                    case CraftMessageType.CraftData:
                        CraftLibrarySystem.SaveCraft(client, (CraftLibraryDataMsgData)data);
                        break;
                    case CraftMessageType.CraftDelete:
                        CraftLibrarySystem.DeleteCraft(client, (CraftLibraryDeleteMsgData)data);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            finally
            {
                message.Recycle();
            }
        }
    }
}
