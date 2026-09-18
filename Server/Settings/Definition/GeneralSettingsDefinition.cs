using LmpCommon.Enums;
using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class GeneralSettingsDefinition
    {
        [XmlComment(Value = "Name of the server. Max 30 char")]
        public string ServerName { get; set; } = "Luna Server";

        [XmlComment(Value = "Description of the server. Max 200 char")]
        public string Description { get; set; } = "Luna Server Description";

        [XmlComment(Value = "By default this will be given by the masterserver but you can override it here if you want. Max 2 char")]
        public string CountryCode { get; set; } = "";

        [XmlComment(Value = "Website text to display (discord, website, forum, etc). Can be left empty. Max 15 char")]
        public string WebsiteText { get; set; } = "LMP";

        [XmlComment(Value = "Actual website URL. Can be left empty. Max 60 char")]
        public string Website { get; set; } = "lunamultiplayer.com";

        [XmlComment(Value = "Password for the server. Leave it empty if you want to make a public server. Max 30 chars")]
        public string Password { get; set; } = "";

        [XmlComment(Value = "Admin password for the server. Leave it empty if you don't want to allow server administration from KSP. Max 30 chars")]
        public string AdminPassword { get; set; } = "";

        [XmlComment(Value = "Specify the server's MOTD (message of the day). 255 chars max")]
        public string ServerMotd { get; set; } = "Hi %Name%!\nWelcome to %ServerName%.\nOnline players: %PlayerCount%";

        [XmlComment(Value = "Writes the server's MOTD (message of the day) in the chat of the user who joins")]
        public bool PrintMotdInChat { get; set; } = false;

        [XmlComment(Value = "Maximum amount of players that can join the server.")]
        public int MaxPlayers { get; set; } = 20;

        [XmlComment(Value = "Maximum length of a username.")]
        public int MaxUsernameLength { get; set; } = 15;

        [XmlComment(Value = "Specify in minutes how often /dekessler automatically runs. 0 = Disabled")]
        public float AutoDekessler { get; set; } = 0.5f;

        [XmlComment(Value = "Specify in minutes how often /nukeksc automatically runs. 0 = Disabled")]
        public float AutoNuke { get; set; } = 0.0f;

        [XmlComment(Value = "Enable use of Cheats in-game.")]
        public bool Cheats { get; set; } = true;

        [XmlComment(Value = "Allow players to sack kerbals")]
        public bool AllowSackKerbals { get; set; } = false;

        [XmlComment(Value = "Specify the Name that will appear when you send a message using the server's console.")]
        public string ConsoleIdentifier { get; set; } = "Server";

        [XmlComment(Value = "Specify the gameplay difficulty of the server. Values: Easy, Normal, Moderate, Hard, Custom")]
        public GameDifficulty GameDifficulty { get; set; } = GameDifficulty.Normal;

        [XmlComment(Value = "Specify the game Type. Values: Sandbox, Career, Science")]
        public GameMode GameMode { get; set; } = GameMode.Sandbox;

        [XmlComment(Value = "Enable mod control. WARNING: Only consider turning off mod control for private servers. " +
                            "The game will constantly complain about missing parts if there are missing mods. " +
                            "Read this wiki page: https://github.com/LunaMultiplayer/LunaMultiplayer/wiki/Mod-file to understand how it works")]
        public bool ModControl { get; set; } = true;

        [XmlComment(Value = "How many untracked asteroids to spawn into the universe. 0 = Disabled")]
        public int NumberOfAsteroids { get; set; } = 5;

        [XmlComment(Value = "How many untracked comets to spawn into the universe. 0 = Disabled")]
        public int NumberOfComets { get; set; } = 5;

        [XmlComment(Value = "Terrain quality. All clients will need to have this setting in their KSP to avoid terrain differences. Values: Low, Default, High, Ignore. Using 'Ignore' might create bugs")]
        public TerrainQuality TerrainQuality { get; set; } = TerrainQuality.High;

        [XmlComment(Value = "Specify the minimum distance in which vessels can interact with eachother at the launch pad and runway")]
        public float SafetyBubbleDistance { get; set; } = 100.0f;

        [XmlComment(Value = "Max number of parts that a vessel can have when spawning")]
        public int MaxVesselParts { get; set; } = 200;

        [XmlComment(Value = "Max amount of backups to keep. 0 = Disabled. BackupMinutesInterval in IntervalSettings can be used to change how often to backup, it needs to be above 0 for backups to happen.")]
        public int MaxBackups { get; set; } = 5;
    }
}
