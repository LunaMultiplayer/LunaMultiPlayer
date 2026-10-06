using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class CraftSettingsDefinition
    {
        [XmlComment(Value = "Maximum crafts kept per user per type (VAB,SPH and Subassembly). 0 keeps the whole library, which is what the automatic craft sync expects. Any value above 0 deletes the oldest crafts of a player as he uploads new ones")]
        public int MaxCraftsPerUser { get; set; } = 0;

        [XmlComment(Value = "Maximum crafts folders kept. When more players have crafts, the folder of the player that did not upload the longest time ago is removed. 0 keeps every folder")]
        public int MaxCraftFolders { get; set; } = 50;
    }
}
