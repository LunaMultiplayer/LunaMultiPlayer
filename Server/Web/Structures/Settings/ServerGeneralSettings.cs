using Server.Settings.Structures;

namespace Server.Web.Structures.Settings
{
    public class ServerGeneralSettings
    {
        public string ServerName => GeneralSettings.SettingsStore.ServerName;
        public string Description => GeneralSettings.SettingsStore.Description;
        public string CountryCode => GeneralSettings.SettingsStore.CountryCode;
        public string WebsiteText => GeneralSettings.SettingsStore.WebsiteText;
        public string Website => GeneralSettings.SettingsStore.Website;
        public bool HasPassword => !string.IsNullOrEmpty(GeneralSettings.SettingsStore.Password);
        public bool HasAdminPassword => !string.IsNullOrEmpty(GeneralSettings.SettingsStore.AdminPassword);
        public string Motd => GeneralSettings.SettingsStore.ServerMotd;
        public int MaxPlayers => GeneralSettings.SettingsStore.MaxPlayers;
        public int MaxUsernameLength => GeneralSettings.SettingsStore.MaxUsernameLength;
        public float AutoDekessler => GeneralSettings.SettingsStore.AutoDekessler;
        public float AutoNuke => GeneralSettings.SettingsStore.AutoDekessler;
        public bool Cheats => GeneralSettings.SettingsStore.Cheats;
        public bool AllowSackKerbals => GeneralSettings.SettingsStore.AllowSackKerbals;
        public string ConsoleIdentifier => GeneralSettings.SettingsStore.ConsoleIdentifier;
        public string GameDifficulty => GeneralSettings.SettingsStore.GameDifficulty.ToString();
        public string GameMode => GeneralSettings.SettingsStore.GameMode.ToString();
        public bool ModControl => GeneralSettings.SettingsStore.ModControl;
        public int NumberOfAsteroids => GeneralSettings.SettingsStore.NumberOfAsteroids;
        public int NumberOfComets => GeneralSettings.SettingsStore.NumberOfComets;
        public string TerrainQuality => GeneralSettings.SettingsStore.TerrainQuality.ToString();
        public float SafetyBubbleDistance => GeneralSettings.SettingsStore.SafetyBubbleDistance;
        public int MaxBackups => GeneralSettings.SettingsStore.MaxBackups;
    }
}
