using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.CraftLibrary
{
    public class CraftLibraryDeleteMsgData : CraftLibraryBaseMsgData
    {
        internal CraftLibraryDeleteMsgData() { }
        public override CraftMessageType CraftMessageType => CraftMessageType.CraftDelete;

        public override string ClassName { get; } = nameof(CraftLibraryDeleteMsgData);

        public string FolderName;
        public string CraftName;
        public CraftType CraftType;
        public string CraftFolder;

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);
            lidgrenMsg.Write(FolderName);
            lidgrenMsg.Write(CraftName);
            lidgrenMsg.Write((int)CraftType);
            lidgrenMsg.Write(CraftFolder ?? string.Empty);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);
            FolderName = lidgrenMsg.ReadString();
            CraftName = lidgrenMsg.ReadString();
            CraftType = (CraftType)lidgrenMsg.ReadInt32();

            CraftFolder = lidgrenMsg.Position < lidgrenMsg.LengthBits ? lidgrenMsg.ReadString() : string.Empty;
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + FolderName.GetByteCount() + CraftName.GetByteCount() + sizeof(CraftType)
                   + CraftFolder.GetByteCount();
        }
    }
}