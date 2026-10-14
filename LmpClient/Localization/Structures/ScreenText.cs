namespace LmpClient.Localization.Structures
{
    public class ScreenText
    {
        public string CraftUploaded { get; set; } = "Craft uploaded!";
        public string CraftSaved { get; set; } = "Craft saved!";
        public string ModFileGenerated { get; set; } = "LMPModControl.xml file generated in your KSP folder";
        public string Disconected { get; set; } = "You have been disconnected!";
        public string Spectating { get; set; } = "This vessel is being controlled by";
        public string SafetyBubble { get; set; } = "Remember!! While you're inside the safety bubble you won't be seen by other players!!";
        public string CheckParts { get; set; } = "If you use mod or DLC parts that other players don't have you won't be seen by them!";
        public string CannotRecover { get; set; } = "Cannot recover vessel, the vessel is not yours.";
        public string CannotTerminate { get; set; } = "Cannot terminate vessel, the vessel is not yours.";
        public string TerminationDisabledByServer { get; set; } = "Vessel termination is disabled on this server. Vessels cannot be deleted from the Tracking Station.";
        public string SpectatingRemoved { get; set; } = "The vessel you were spectating was removed";
        public string WarpDisabled { get; set; } = "Cannot warp, warping is disabled on this server";
        public string WaitingSubspace { get; set; } = "Cannot warp, waiting subspace id from the server";
        public string CannotWarpWhileSpectating { get; set; } = "Cannot warp while spectating";
        public string ScreenshotInterval { get; set; } = "Interval between screenshots is $1 seconds. Cannot upload the screenshot at this moment";
        public string CraftLibraryInterval { get; set; } = "Interval between craft library requests is $1 seconds. Cannot process the request at this moment";
        public string ScreenshotTaken { get; set; } = "Screenshot uploaded!";
        public string ImageSaved { get; set; } = "Image saved to GameData/LunaMultiplayer/Screenshots";
        public string IncreasedInterpolationOffset { get; set; } = "Warning! Your interpolation offset has been increased as it was too low for this server";
        public string SackingKerbalsNotAllowed { get; set; } = "This server does not allow firing kerbals";
        public string CannotLoadGames { get; set; } = "LMP does not allow loading savegames";
        public string KerbalNotYours { get; set; } = "Another player is using this kerbal";
        public string UnsafeToSync { get; set; } = "Cannot sync while in unstable orbit or spectating as you might crash! You can turn off this check in settings";
    }
}
