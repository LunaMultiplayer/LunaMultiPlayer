using LmpCommon.Xml;
using System;

namespace Server.Settings.Definition
{
    [Serializable]
    public class IntervalSettingsDefinition
    {
        [XmlComment(Value = "Interval in ms at which the client will send POSITION and FLIGHTSTATE updates of their vessel when other players are NEARBY. " +
                "Decrease it if your clients have good network connection and you plan to do dogfights, although in that case consider using interpolation aswell")]
        public int VesselUpdatesMsInterval { get; set; } = 50;

        [XmlComment(Value = "Interval in ms at which the client will send POSITION and FLIGHTSTATE updates for vessels that are uncontrolled and nearby them. " +
                            "This interval is also applied used to send position updates of HIS OWN vessel when NOBODY is around")]
        public int SecondaryVesselUpdatesMsInterval { get; set; } = 150;

        [XmlComment(Value = "Send/Receive tick clock. Keep this value low but at least above 2ms to avoid extreme CPU usage.")]
        public int SendReceiveThreadTickMs { get; set; } = 5;

        [XmlComment(Value = "Main thread polling in ms. Keep this value low but at least above 2ms to avoid extreme CPU usage.")]
        public int MainTimeTick { get; set; } = 5;

        [XmlComment(Value = "Interval in ms at which internal LMP structures (Subspaces, Vessels, Scenario files, ...) will be saved to a file")]
        public int SaveIntervalMs { get; set; } = 30000;

        [XmlComment(Value = "Interval in ms at which internal LMP structures (Subspaces, Vessels, Scenario files, ...) will be backed up for longer storage. Set to 0 to avoid backups")]
        public int BackupMinutesInterval { get; set; } = 60;

        [XmlComment(Value = "Interval to force a garbage collection and reduce the memory usage. Specify this value in minutes. 0 = deactivated. " +
                            "Combined with the runtime's System.GC.RetainVM=false / ConserveMemory=9 settings, each forced collection returns " +
                            "decommitted heap segments to the OS, so this also controls how often the working set drops back to its baseline. " +
                            "5 minutes is a good balance between a flat-looking memory graph on hosting panels and the (microsecond-scale) cost of running a Gen2.")]
        public int GcMinutesInterval { get; set; } = 5;

        [XmlComment(Value = "Interval at which a memory diagnostics line (managed heap vs working set, GC collection counts, allocation rate) " +
                            "is written to the server log. Useful for distinguishing real managed leaks from Server-GC working-set retention. " +
                            "Specify this value in minutes. 0 = deactivated. " +
                            "Memory diagnostics also require the server to be launched with the --memorydiag command-line flag; " +
                            "this interval setting only controls the cadence once the flag has enabled the feature.")]
        public int MemoryDiagnosticsMinutesInterval { get; set; } = 1;
    }
}
