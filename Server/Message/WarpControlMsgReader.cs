using System;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Types;
using Server.Client;
using Server.Message.Base;
using Server.System;

namespace Server.Message
{
    public class WarpControlMsgReader : ReaderBase
    {
        private static readonly WarpSystemReceiver WarpReceiver = new WarpSystemReceiver();

        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var messageData = message.Data as WarpBaseMsgData;
            switch (messageData?.WarpMessageType)
            {
                case WarpMessageType.NewSubspace:
                    WarpReceiver.HandleNewSubspace(client, (WarpNewSubspaceMsgData)messageData);
                    break;
                case WarpMessageType.ChangeSubspace:
                    WarpReceiver.HandleChangeSubspace(client, (WarpChangeSubspaceMsgData)messageData);
                    break;
                case WarpMessageType.WarpingTime:
                    WarpReceiver.HandleWarpingTime(client, (WarpingTimeMsgData)messageData);
                    break;
                case WarpMessageType.SubspacesRequest:
                    WarpReceiver.HandleSubspaceRequest(client);
                    //We don't use this message anymore so we can recycle it
                    message.Recycle();
                    break;
                default:
                    throw new NotImplementedException("Warp Type not implemented");
            }
        }
    }
}
