using Lidgren.Network;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Warp
{
    public class WarpingTimeMsgData : WarpBaseMsgData
    {
        /// <inheritdoc />
        internal WarpingTimeMsgData() { }
        public override WarpMessageType WarpMessageType => WarpMessageType.WarpingTime;

        public string PlayerName;
        public double ServerTimeDifference;

        public override string ClassName { get; } = nameof(WarpingTimeMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write(PlayerName);
            lidgrenMsg.Write(ServerTimeDifference);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            PlayerName = lidgrenMsg.ReadString();
            ServerTimeDifference = lidgrenMsg.ReadDouble();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + PlayerName.GetByteCount() + sizeof(double);
        }
    }
}
