using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.CraftLibrary
{
    public class CraftLibrarySyncRequestMsgData : CraftLibraryBaseMsgData
    {
        internal CraftLibrarySyncRequestMsgData() { }
        public override CraftMessageType CraftMessageType => CraftMessageType.SyncRequest;
        public override string ClassName { get; } = nameof(CraftLibrarySyncRequestMsgData);
    }
}
