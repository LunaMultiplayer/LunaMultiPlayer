namespace LmpClient.Localization.Structures
{
    public class ScreenText
    {
        public string CraftUploaded { get; set; } = "Craft uploaded!";
        public string CraftSaved { get; set; } = "Craft saved!";
        public string CraftsUploading { get; set; } = "Uploading {0} crafts";
        public string CraftsUploaded { get; set; } = "Uploaded {0} crafts";
        public string CraftsReceived { get; set; } = "Received {0} crafts";
        public string CraftDeleted { get; set; } = "Craft deleted!";
        public string CraftPermissionDenied { get; set; } = "You don't have permission to modify other people's crafts!";
        public string CraftFolderPermissionDenied { get; set; } = "You don't have permission to create folders outside your own craft folder!";
        public string CraftOutsideFolderWarning { get; set; } = "You can only save vehicles in your own craft folder! The vehicle was removed.";
        public string CraftNotShared { get; set; } = "This vehicle cannot be shared: {0}.";
        public string CraftLibrarySynced { get; set; } = "Craft library synchronized, {0} crafts received";
        public string ModFileGenerated { get; set; } = "LMPModControl.xml file generated in your KSP folder";
        public string Disconected { get; set; } = "You have been disconnected!";
        public string Spectating { get; set; } = "This vessel is being controlled by";
        public string SafetyBubble { get; set; } = "Remember!! While you're inside the safety bubble you won't be seen by other players!!";
        public string CheckParts { get; set; } = "If you use mod or DLC parts that other players don't have you won't be seen by them!";
        public string CannotRecover { get; set; } = "Cannot recover vessel, the vessel is not yours.";
        public string CannotTerminate { get; set; } = "Cannot terminate vessel, the vessel is not yours.";
        public string SpectatingRemoved { get; set; } = "The vessel you were spectating was removed";
        public string WarpDisabled { get; set; } = "Cannot warp, warping is disabled on this server";
        public string WaitingSubspace { get; set; } = "Cannot warp, waiting subspace id from the server";
        public string CannotWarpWhileSpectating { get; set; } = "Cannot warp while spectating";
        public string ScreenshotInterval { get; set; } = "Interval between screenshots is $1 seconds. Cannot upload the screenshot at this moment";
        public string ScreenshotTaken { get; set; } = "Screenshot uploaded!";
        public string ImageSaved { get; set; } = "Image saved to GameData/LunaMultiplayer/Screenshots";
        public string IncreasedInterpolationOffset { get; set; } = "Warning! Your interpolation offset has been increased as it was too low for this server";
        public string SackingKerbalsNotAllowed { get; set; } = "This server does not allow firing kerbals";
        public string CannotLoadGames { get; set; } = "LMP does not allow loading savegames";
        public string KerbalNotYours { get; set; } = "Another player is using this kerbal";
        public string UnsafeToSync { get; set; } = "Cannot sync while in unstable orbit or spectating as you might crash! You can turn off this check in settings";
    }
}
