using LmpCommon.Enums;
using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class WarpSettingsDefinition
    {
        [XmlComment(Value = "Specify the warp Type. Values: None, Subspace")]
        public WarpMode WarpMode { get; set; } = WarpMode.Subspace;
        [XmlComment(Value = "Tells the server to pause the time whilst off. If set to false the server will keep track of time and affect crafts")]
        public bool PauseTimeWhileShutdown { get; set; } = false;
    }
}
