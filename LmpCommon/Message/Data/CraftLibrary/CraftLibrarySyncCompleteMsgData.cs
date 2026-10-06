using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.CraftLibrary
{
    public class CraftLibrarySyncCompleteMsgData : CraftLibraryBaseMsgData
    {
        internal CraftLibrarySyncCompleteMsgData() { }
        public override CraftMessageType CraftMessageType => CraftMessageType.SyncComplete;
        public override string ClassName { get; } = nameof(CraftLibrarySyncCompleteMsgData);
    }
}
