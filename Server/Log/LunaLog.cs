using LmpCommon;
using LmpCommon.Enums;
using Server.Events;
using Server.Context;
using Server.Settings.Structures;
using Server.System;
using System;
using System.IO;

namespace Server.Log
{
    /// <summary>
    /// Server-side logger that mirrors every console line into a daily <c>lmpserver_*.log</c>
    /// file under <see cref="LogFolder"/>.
    ///
    /// Writes go through a single persistent <see cref="StreamWriter"/> kept open for the
    /// lifetime of the process (or until the file is rotated by reassigning
    /// <see cref="LogFilename"/>). The previous implementation funneled every log line
    /// through <see cref="FileHandler.AppendToFile"/>, which performed a full
    /// <c>open → write → fsync → close</c> cycle per call. On Linux container hosts that
    /// pattern dirties one or more page-cache pages on every line and inflates the cgroup
    /// RSS that hosting panels report. Keeping the stream open and flushing per line gives
    /// us identical "operator can tail the log live" semantics with a fraction of the
    /// page-cache churn and zero per-line FileStream/StreamWriter allocations.
    ///
    /// Thread safety: every public log method ultimately calls <see cref="AfterPrint"/> on
    /// <see cref="Singleton"/>, and that method takes <see cref="WriteLock"/> before
    /// touching the writer. <see cref="StreamWriter"/> is not thread-safe and the server
    /// runs many message-handler tasks concurrently, so a single write lock is the
    /// simplest correct synchronization.
    /// </summary>
    public class LunaLog : BaseLogger
    {
        private static readonly BaseLogger Singleton = new LunaLog();
        public static string LogFolder = Path.Combine(ServerContext.DataDirectory, "logs");

        /// <summary>
        /// Serializes access to writer lifecycle and writes.
        /// </summary>
        private static readonly object WriteLock = new object();

        /// <summary>
        /// Path currently bound to <see cref="_writer"/>.
        /// </summary>
        private static string _currentPath;

        /// <summary>
        /// Persistent writer for the current log file; null means console-only fallback.
        /// </summary>
        private static StreamWriter _writer;

        private static string _logFilename = Path.Combine(LogFolder, $"lmpserver_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

        /// <summary>
        /// Active log path. Reassigning rotates the underlying writer.
        /// </summary>
        public static string LogFilename
        {
            get => _logFilename;
            set => SwitchToFile(value);
        }

        static LunaLog()
        {
            try
            {
                if (!FileHandler.FolderExists(LogFolder))
                    FileHandler.FolderCreate(LogFolder);
            }
            catch (Exception e)
            {
                // Keep server startup resilient: file logging is best-effort.
                Console.Error.WriteLine($"LunaLog: failed to ensure log folder '{LogFolder}': {e.Message}");
            }

            // Open after folder check; failure falls back to console-only.
            OpenWriter(_logFilename);

            // Flush and close on clean shutdown.
            ExitEvent.ServerClosing += CloseLog;
        }

        #region Overrides

        protected override LogLevels LogLevel => LogSettings.SettingsStore.LogLevel;
        protected override bool UseUtcTime => true;

        protected override void AfterPrint(string line)
        {
            base.AfterPrint(line);

            lock (WriteLock)
            {
                if (_writer == null) return;

                try
                {
                    _writer.WriteLine(line);
                    // Flush per line so operators can tail the file live.
                    _writer.Flush();
                }
                catch (Exception e)
                {
                    // Avoid recursive logging on write failure.
                    Console.Error.WriteLine($"LunaLog: failed to write to {_currentPath}: {e.Message}");
                }
            }
        }

        #endregion

        #region Public methods

        public new static void NetworkVerboseDebug(string message)
        {
            Singleton.NetworkVerboseDebug(message);
        }

        public new static void NetworkDebug(string message)
        {
            Singleton.NetworkDebug(message);
        }

        public new static void Debug(string message)
        {
            Singleton.Debug(message);
        }

        public new static void Warning(string message)
        {
            Singleton.Warning(message);
        }

        public new static void Info(string message)
        {
            Singleton.Info(message);
        }

        public new static void Normal(string message)
        {
            Singleton.Normal(message);
        }

        public new static void Error(string message)
        {
            Singleton.Error(message);
        }

        public new static void Fatal(string message)
        {
            Singleton.Fatal(message);
        }

        public new static void ChatMessage(string message)
        {
            Singleton.ChatMessage(message);
        }

        #endregion

        #region Writer lifecycle

        /// <summary>
        /// Atomically rotates to <paramref name="newPath"/> when needed.
        /// </summary>
        private static void SwitchToFile(string newPath)
        {
            lock (WriteLock)
            {
                if (string.Equals(_currentPath, newPath, StringComparison.Ordinal) && _writer != null)
                {
                    _logFilename = newPath;
                    return;
                }

                CloseWriterLocked();
                _logFilename = newPath;
                OpenWriterLocked(newPath);
            }
        }

        /// <summary>
        /// Opens <paramref name="path"/> for append, acquiring <see cref="WriteLock"/>.
        /// </summary>
        private static void OpenWriter(string path)
        {
            lock (WriteLock)
            {
                OpenWriterLocked(path);
            }
        }

        /// <summary>
        /// Opens writer internals; caller must hold <see cref="WriteLock"/>.
        /// Uses append/readwrite semantics and stderr-only failure reporting.
        /// </summary>
        private static void OpenWriterLocked(string path)
        {
            try
            {
                var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(stream) { NewLine = Environment.NewLine };
                _currentPath = path;
            }
            catch (Exception e)
            {
                _writer = null;
                _currentPath = null;
                Console.Error.WriteLine($"LunaLog: failed to open log file '{path}': {e.Message}. File logging disabled.");
            }
        }

        /// <summary>
        /// Flushes and disposes the current writer; caller must hold <see cref="WriteLock"/>.
        /// </summary>
        private static void CloseWriterLocked()
        {
            if (_writer == null) return;

            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"LunaLog: failed to close log file '{_currentPath}': {e.Message}");
            }
            finally
            {
                _writer = null;
                _currentPath = null;
            }
        }

        /// <summary>
        /// Shutdown hook that flushes and closes file logging.
        /// </summary>
        private static void CloseLog()
        {
            lock (WriteLock)
            {
                CloseWriterLocked();
            }
        }

        #endregion
    }
}
