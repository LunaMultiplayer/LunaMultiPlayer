using LmpCommon.Enums;

namespace LmpClient.Systems.CraftLibrary
{
    public class CraftEntry
    {
        public string FolderName { get; set; }
        public CraftType CraftType { get; set; }
        public string CraftName { get; set; }
        public string CraftFolder { get; set; }
        public int CraftNumBytes { get; set; }
        public byte[] CraftData { get; set; }
    }
}
