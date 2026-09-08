using Server.Events;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Server.Log
{
    /// <summary> 
    /// Dedicated audit log that records every time a craft is created (first time the server sees a
    /// vessel's proto) or removed, so server operators can troubleshoot missing/re-appearing ships.
    ///
    /// The file lives at <c>logs/audit/CraftCreationAndRemoval.txt</c> and is truncated at server
    /// start (via the static constructor) so each server run produces a fresh audit trail. It sits
    /// in an <c>audit</c> subfolder (rather than directly in <c>logs/</c>) on purpose: it is a
    /// per-run audit trail, not a rotating server log, so it must be kept out of the
    /// <see cref="LogExpire"/> sweep. That sweep enumerates <c>logs/</c> with
    /// <see cref="System.IO.SearchOption.TopDirectoryOnly"/> and would otherwise repeatedly try to
    /// delete this file once it aged past the expiry threshold — failing every time because the
    /// stream below is held open for the server's lifetime.
    ///
    /// Writes go through a single <see cref="StreamWriter"/> that is kept open for the server's
    /// lifetime and disposed on <see cref="ExitEvent.ServerClosing"/>. Keeping the stream open
    /// avoids an <c>open/write/close</c> round-trip on every vessel create/remove event — the
    /// previous implementation used <c>File.AppendAllText</c> per event, which created steady
    /// FileStream/StreamWriter allocations under load.
    ///
    /// This class deliberately depends only on <see cref="LunaLog.LogFolder"/> (for the path),
    /// <see cref="Server.System.FileHandler"/> (for the initial directory bootstrap), and
    /// <see cref="ExitEvent"/> (for clean shutdown) to keep its single responsibility: write
    /// audit entries. It does NOT extend <see cref="LmpCommon.BaseLogger"/> because we do not
    /// want these entries echoed to the console or the general <c>lmpserver_*.log</c> file.
    /// </summary>
    public static class CraftCreationAndRemovalLog
    {
        /// <summary>
        /// Dedicated audit subfolder under <see cref="LunaLog.LogFolder"/>. Living in a subfolder
        /// keeps this file out of <see cref="LogExpire"/>'s top-directory-only expiry sweep while
        /// still grouping it with the other logs an operator would collect for troubleshooting.
        /// </summary>
        private static readonly string LogDirectory = Path.Combine(LunaLog.LogFolder, "audit");

        private static readonly string LogFilePath = Path.Combine(LogDirectory, "CraftCreationAndRemoval.txt");

        /// <summary>
        /// Serializes all access to <see cref="_writer"/> and the close hook. <see cref="StreamWriter"/>
        /// is not thread-safe and log events can arrive from any of the server's message-handler
        /// tasks at once, so a single write lock is the simplest correct synchronization.
        /// </summary>
        private static readonly object WriteLock = new object();

        /// <summary>
        /// Persistent writer opened in the static constructor and disposed on server shutdown.
        /// <c>null</c> if the file could not be opened, in which case every subsequent
        /// <see cref="LogCreated"/>/<see cref="LogRemoved"/> call becomes a no-op (matching the
        /// advisory semantics of the previous implementation).
        /// </summary>
        private static StreamWriter _writer;

        static CraftCreationAndRemovalLog()
        {
            try
            {
                if (!System.FileHandler.FolderExists(LogDirectory))
                    System.FileHandler.FolderCreate(LogDirectory);

                // FileMode.Create truncates any pre-existing file so each server run gets a fresh
                // audit trail. FileShare.Read lets operators tail the file live while the server
                // is running.
                var stream = new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { NewLine = Environment.NewLine };

                _writer.WriteLine("# Craft Creation and Removal audit log");
                _writer.WriteLine($"# Server started at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                _writer.WriteLine("# Format: [Timestamp UTC] Vessel <GUID> (<Vessel Name>) created/removed by player <Player> (<Reason>)");
                _writer.Flush();

                ExitEvent.ServerClosing += CloseLog;
            }
            catch (Exception e)
            {
                // If we can't prepare the file (permissions, disk full, etc.) surface it in the
                // main log but don't crash the server - the audit log is advisory.
                LunaLog.Error($"Failed to initialize CraftCreationAndRemoval.txt: {e.Message}");
                _writer = null;
            }
        }

        /// <summary>
        /// Ensures the static constructor has run. Call this once at server startup so the audit
        /// file is truncated even if no craft events happen before the first log write.
        /// </summary>
        public static void Initialize()
        {
            // Touching a static member forces the static constructor to execute.
            _ = LogFilePath;
        }

        /// <summary>
        /// Record that a brand-new vessel was registered on the server.
        /// </summary>
        public static void LogCreated(Guid vesselId, string vesselName, string playerName, string reason)
        {
            WriteLine("created", vesselId, vesselName, playerName, reason);
        }

        /// <summary>
        /// Record that a vessel was removed from the server.
        /// </summary>
        public static void LogRemoved(Guid vesselId, string vesselName, string playerName, string reason)
        {
            WriteLine("removed", vesselId, vesselName, playerName, reason);
        }

        /// <summary>
        /// Pulls the <c>name = ...</c> field out of a raw KSP vessel config-node string without
        /// having to parse the whole node. Returns <c>null</c> if no name can be found.
        /// We scope to the top of the text and match "name" as a standalone field so we don't
        /// accidentally grab "moduleName" or part-level names.
        /// </summary>
        private static readonly Regex VesselNameRegex = new Regex(
            @"(?:^|\n)\s*name\s*=\s*(?<value>.*?)\s*(?:\r|\n|$)",
            RegexOptions.Compiled);

        public static string ExtractVesselName(string vesselConfigNodeText)
        {
            if (string.IsNullOrEmpty(vesselConfigNodeText)) return null;

            // Only scan the top of the file where the vessel-level fields live; past that we'd
            // start hitting part-level "name = <part identifier>" lines and pick up garbage.
            var scanLength = Math.Min(vesselConfigNodeText.Length, 2048);
            var header = vesselConfigNodeText.Substring(0, scanLength);

            var match = VesselNameRegex.Match(header);
            return match.Success ? match.Groups["value"].Value : null;
        }

        private static void WriteLine(string action, Guid vesselId, string vesselName, string playerName, string reason)
        {
            var safeName = string.IsNullOrWhiteSpace(vesselName) ? "Unknown" : vesselName.Trim();
            var safePlayer = string.IsNullOrWhiteSpace(playerName) ? "Unknown" : playerName.Trim();
            var safeReason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason.Trim();

            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Vessel {vesselId} ({safeName}) {action} by player {safePlayer} ({safeReason})";

            lock (WriteLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(line);
                    // Flush per entry so the on-disk file is usable for live troubleshooting
                    // (e.g. tailing it while chasing a "where did my ship go?" report).
                    _writer.Flush();
                }
                catch (Exception e)
                {
                    LunaLog.Error($"Failed to append to CraftCreationAndRemoval.txt: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Flush and release the underlying stream. Invoked from <see cref="ExitEvent.ServerClosing"/>
        /// so a clean shutdown (Ctrl+C on Linux, console-close handler on Windows) doesn't leave
        /// buffered entries unwritten.
        /// </summary>
        private static void CloseLog()
        {
            lock (WriteLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception e)
                {
                    LunaLog.Error($"Failed to close CraftCreationAndRemoval.txt: {e.Message}");
                }
                finally
                {
                    _writer = null;
                }
            }
        }
    }
}
