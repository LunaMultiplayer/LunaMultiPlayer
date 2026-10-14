using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using LmpCommon.Message.Types;

namespace LmpCommon.Message.Data.Settings
{
    public class SettingsReplyMsgData : SettingsBaseMsgData
    {
        /// <inheritdoc />
        internal SettingsReplyMsgData() { }
        public override SettingsMessageType SettingsMessageType => SettingsMessageType.Reply;

        public WarpMode WarpMode;
        public GameMode GameMode;
        public TerrainQuality TerrainQuality;
        public bool AllowCheats;
        public bool AllowAdmin;
        public bool AllowSackKerbals;
        public int MaxNumberOfAsteroids;
        public int MaxNumberOfComets;
        public string ConsoleIdentifier;
        public GameDifficulty GameDifficulty;
        public float SafetyBubbleDistance;
        public int MaxVesselParts;
        public int VesselUpdatesMsInterval;
        public int SecondaryVesselUpdatesMsInterval;
        public bool AllowOtherLaunchSites;
        public bool AllowStockVessels;
        public bool CanRevert;
        public bool AutoHireCrews;
        public bool BypassEntryPurchaseAfterResearch;
        public bool IndestructibleFacilities;
        public bool MissingCrewsRespawn;
        public float ReentryHeatScale;
        public float ResourceAbundance;
        public float FundsGainMultiplier;
        public float FundsLossMultiplier;
        public float RepGainMultiplier;
        public float RepLossMultiplier;
        public float RepLossDeclined;
        public float ScienceGainMultiplier;
        public float StartingFunds;
        public float StartingReputation;
        public float StartingScience;
        public float RespawnTimer;
        public bool EnableCommNet;
        public bool EnableKerbalExperience;
        public bool ImmediateLevelUp;
        public bool ResourceTransferObeyCrossfeed;
        public float BuildingImpactDamageMult;
        public bool PartUpgradesInCareerAndSandbox;
        public bool EnableFullSASInSandbox;
        public bool RequireSignalForControl;
        public float DsnModifier;
        public float RangeModifier;
        public float OcclusionMultiplierVac;
        public float OcclusionMultiplierAtm;
        public bool EnableGroundStations;
        public bool PlasmaBlackout;
        public bool ActionGroupsAlways;
        public bool GKerbalLimits;
        public bool GPartLimits;
        public bool PressurePartLimits;
        public float KerbalGToleranceMult;
        public bool AllowNegativeCurrency;
        public int MinScreenshotIntervalMs;
        public int MaxScreenshotWidth;
        public int MaxScreenshotHeight;
        public int MinCraftLibraryRequestIntervalMs;
        public bool PrintMotdInChat;
        public bool AllowVesselTermination;

        public override string ClassName { get; } = nameof(SettingsReplyMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write((int)WarpMode);
            lidgrenMsg.Write((int)GameMode);
            lidgrenMsg.Write((int)TerrainQuality);
            lidgrenMsg.Write(AllowCheats);
            lidgrenMsg.Write(AllowAdmin);
            lidgrenMsg.Write(AllowSackKerbals);
            lidgrenMsg.Write(MaxNumberOfAsteroids);
            lidgrenMsg.Write(MaxNumberOfComets);
            lidgrenMsg.Write(ConsoleIdentifier);
            lidgrenMsg.Write((int)GameDifficulty);
            lidgrenMsg.Write(SafetyBubbleDistance);
            lidgrenMsg.Write(MaxVesselParts);
            lidgrenMsg.Write(VesselUpdatesMsInterval);
            lidgrenMsg.Write(SecondaryVesselUpdatesMsInterval);
            lidgrenMsg.Write(AllowOtherLaunchSites);
            lidgrenMsg.Write(AllowStockVessels);
            lidgrenMsg.Write(CanRevert);
            lidgrenMsg.Write(AutoHireCrews);
            lidgrenMsg.Write(BypassEntryPurchaseAfterResearch);
            lidgrenMsg.Write(IndestructibleFacilities);
            lidgrenMsg.Write(MissingCrewsRespawn);
            lidgrenMsg.Write(ReentryHeatScale);
            lidgrenMsg.Write(ResourceAbundance);
            lidgrenMsg.Write(FundsGainMultiplier);
            lidgrenMsg.Write(FundsLossMultiplier);
            lidgrenMsg.Write(RepGainMultiplier);
            lidgrenMsg.Write(RepLossMultiplier);
            lidgrenMsg.Write(RepLossDeclined);
            lidgrenMsg.Write(ScienceGainMultiplier);
            lidgrenMsg.Write(StartingFunds);
            lidgrenMsg.Write(StartingReputation);
            lidgrenMsg.Write(StartingScience);
            lidgrenMsg.Write(RespawnTimer);
            lidgrenMsg.Write(EnableCommNet);
            lidgrenMsg.Write(EnableKerbalExperience);
            lidgrenMsg.Write(ImmediateLevelUp);
            lidgrenMsg.Write(ResourceTransferObeyCrossfeed);
            lidgrenMsg.Write(BuildingImpactDamageMult);
            lidgrenMsg.Write(PartUpgradesInCareerAndSandbox);
            lidgrenMsg.Write(EnableFullSASInSandbox);
            lidgrenMsg.Write(RequireSignalForControl);
            lidgrenMsg.Write(DsnModifier);
            lidgrenMsg.Write(RangeModifier);
            lidgrenMsg.Write(OcclusionMultiplierVac);
            lidgrenMsg.Write(OcclusionMultiplierAtm);
            lidgrenMsg.Write(EnableGroundStations);
            lidgrenMsg.Write(PlasmaBlackout);
            lidgrenMsg.Write(ActionGroupsAlways);
            lidgrenMsg.Write(GKerbalLimits);
            lidgrenMsg.Write(GPartLimits);
            lidgrenMsg.Write(PressurePartLimits);
            lidgrenMsg.Write(KerbalGToleranceMult);
            lidgrenMsg.Write(AllowNegativeCurrency);
            lidgrenMsg.Write(MinScreenshotIntervalMs);
            lidgrenMsg.Write(MaxScreenshotWidth);
            lidgrenMsg.Write(MaxScreenshotHeight);
            lidgrenMsg.Write(MinCraftLibraryRequestIntervalMs);
            lidgrenMsg.Write(PrintMotdInChat);
            lidgrenMsg.Write(AllowVesselTermination);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            WarpMode = (WarpMode)lidgrenMsg.ReadInt32();
            GameMode = (GameMode)lidgrenMsg.ReadInt32();
            TerrainQuality = (TerrainQuality)lidgrenMsg.ReadInt32();
            AllowCheats = lidgrenMsg.ReadBoolean();
            AllowAdmin = lidgrenMsg.ReadBoolean();
            AllowSackKerbals = lidgrenMsg.ReadBoolean();
            MaxNumberOfAsteroids = lidgrenMsg.ReadInt32();
            MaxNumberOfComets = lidgrenMsg.ReadInt32();
            ConsoleIdentifier = lidgrenMsg.ReadString();
            GameDifficulty = (GameDifficulty)lidgrenMsg.ReadInt32();
            SafetyBubbleDistance = lidgrenMsg.ReadFloat();
            MaxVesselParts = lidgrenMsg.ReadInt32();
            VesselUpdatesMsInterval = lidgrenMsg.ReadInt32();
            SecondaryVesselUpdatesMsInterval = lidgrenMsg.ReadInt32();
            AllowOtherLaunchSites = lidgrenMsg.ReadBoolean();
            AllowStockVessels = lidgrenMsg.ReadBoolean();
            CanRevert = lidgrenMsg.ReadBoolean();
            AutoHireCrews = lidgrenMsg.ReadBoolean();
            BypassEntryPurchaseAfterResearch = lidgrenMsg.ReadBoolean();
            IndestructibleFacilities = lidgrenMsg.ReadBoolean();
            MissingCrewsRespawn = lidgrenMsg.ReadBoolean();
            ReentryHeatScale = lidgrenMsg.ReadFloat();
            ResourceAbundance = lidgrenMsg.ReadFloat();
            FundsGainMultiplier = lidgrenMsg.ReadFloat();
            FundsLossMultiplier = lidgrenMsg.ReadFloat();
            RepGainMultiplier = lidgrenMsg.ReadFloat();
            RepLossMultiplier = lidgrenMsg.ReadFloat();
            RepLossDeclined = lidgrenMsg.ReadFloat();
            ScienceGainMultiplier = lidgrenMsg.ReadFloat();
            StartingFunds = lidgrenMsg.ReadFloat();
            StartingReputation = lidgrenMsg.ReadFloat();
            StartingScience = lidgrenMsg.ReadFloat();
            RespawnTimer = lidgrenMsg.ReadFloat();
            EnableCommNet = lidgrenMsg.ReadBoolean();
            EnableKerbalExperience = lidgrenMsg.ReadBoolean();
            ImmediateLevelUp = lidgrenMsg.ReadBoolean();
            ResourceTransferObeyCrossfeed = lidgrenMsg.ReadBoolean();
            BuildingImpactDamageMult = lidgrenMsg.ReadFloat();
            PartUpgradesInCareerAndSandbox = lidgrenMsg.ReadBoolean();
            EnableFullSASInSandbox = lidgrenMsg.ReadBoolean();
            RequireSignalForControl = lidgrenMsg.ReadBoolean();
            DsnModifier = lidgrenMsg.ReadFloat();
            RangeModifier = lidgrenMsg.ReadFloat();
            OcclusionMultiplierVac = lidgrenMsg.ReadFloat();
            OcclusionMultiplierAtm = lidgrenMsg.ReadFloat();
            EnableGroundStations = lidgrenMsg.ReadBoolean();
            PlasmaBlackout = lidgrenMsg.ReadBoolean();
            ActionGroupsAlways = lidgrenMsg.ReadBoolean();
            GKerbalLimits = lidgrenMsg.ReadBoolean();
            GPartLimits = lidgrenMsg.ReadBoolean();
            PressurePartLimits = lidgrenMsg.ReadBoolean();
            KerbalGToleranceMult = lidgrenMsg.ReadFloat();
            AllowNegativeCurrency = lidgrenMsg.ReadBoolean();
            MinScreenshotIntervalMs = lidgrenMsg.ReadInt32();
            MaxScreenshotWidth = lidgrenMsg.ReadInt32();
            MaxScreenshotHeight = lidgrenMsg.ReadInt32();
            MinCraftLibraryRequestIntervalMs = lidgrenMsg.ReadInt32();
            PrintMotdInChat = lidgrenMsg.ReadBoolean();
            AllowVesselTermination = lidgrenMsg.ReadBoolean();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + sizeof(WarpMode) + sizeof(GameMode) + sizeof(TerrainQuality) + sizeof(GameDifficulty) +
                sizeof(bool) * 25 + sizeof(int) * 9 + sizeof(float) * 19 + ConsoleIdentifier.GetByteCount();
        }
    }
}
