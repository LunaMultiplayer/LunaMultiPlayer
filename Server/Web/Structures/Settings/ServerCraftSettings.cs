using Server.Settings.Structures;

namespace Server.Web.Structures.Settings
{
    public class ServerCraftSettings
    {
        public int MaxCraftsPerUser => CraftSettings.SettingsStore.MaxCraftsPerUser;
        public int MaxCraftFolders => CraftSettings.SettingsStore.MaxCraftFolders;
    }
}
