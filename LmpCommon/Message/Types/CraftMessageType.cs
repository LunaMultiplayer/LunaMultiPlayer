namespace LmpCommon.Message.Types
{
    public enum CraftMessageType
    {
        //0 to 7 were used by the craft library protocol up to LMP 0.30
        SyncRequest = 8,
        CraftData = 9,
        CraftDelete = 10,
        SyncComplete = 11
    }
}
