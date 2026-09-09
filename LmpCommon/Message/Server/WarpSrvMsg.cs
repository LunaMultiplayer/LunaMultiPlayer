using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Data.Warp;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Server
{
    public class WarpSrvMsg : SrvMsgBase<WarpBaseMsgData>
    {
        /// <inheritdoc />
        internal WarpSrvMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(WarpSrvMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)WarpMessageType.SubspacesReply] = typeof(WarpSubspacesReplyMsgData),
            [(ushort)WarpMessageType.NewSubspace] = typeof(WarpNewSubspaceMsgData),
            [(ushort)WarpMessageType.ChangeSubspace] = typeof(WarpChangeSubspaceMsgData),
            [(ushort)WarpMessageType.WarpingTime] = typeof(WarpingTimeMsgData)
        };

        public override ServerMessageType MessageType => ServerMessageType.Warp;
        protected override int DefaultChannel => 13;
        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}