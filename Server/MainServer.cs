using LmpCommon;
using LmpCommon.Time;
using Server.Client;
using Server.Command;
using Server.Context;
using Server.Events;
using Server.Exit;
using Server.Log;
using Server.Plugin;
using Server.Server;
using Server.Settings;
using Server.Settings.Structures;
using Server.System;
using Server.Upnp;
using Server.Utilities;
using Server.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.CommandLine;

namespace Server
{
    public class MainServer
    {
        public static readonly TaskFactory LongRunTaskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);

        private static readonly TaskCompletionSource<bool> QuitSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static readonly WinExitSignal ExitSignal = OperatingSystem.IsWindows() ? new WinExitSignal() : null;

        private static readonly List<Task> TaskContainer = new List<Task>();

        public static readonly CancellationTokenSource CancellationTokenSrc = new CancellationTokenSource();

        private static bool IsRestart = false;
        private static bool MemoryDiagnosticsEnabled;

        public static Task Main(string[] args)
        {
            // Memory diagnostics are an opt-in operator tool. Gating them on a CLI flag
            // (rather than just the IntervalSettings entry) keeps the production log clean
            // by default and means turning diagnostics on for a hosted server is a single
            // launcher edit, not an XML edit followed by a restart.
            MemoryDiagnosticsEnabled = HasFlag(args, "--memorydiag");

            //Verify the .NET runtime before anything else so we can give users a clear,
            //actionable message instead of failing later with a confusing error.
            DotNetRuntimeChecker.EnsureCorrectRuntimeOrExit();

            Option<DirectoryInfo> dataDirectoryOption = new("--data-directory", ["-d"])
            {
                Description = "The location to store the server runtime data",
                DefaultValueFactory = parseResult => new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
            };

            dataDirectoryOption.AcceptExistingOnly();

            RootCommand rootCommand = new("Luna Multiplayer Server");
            rootCommand.Options.Add(dataDirectoryOption);

            rootCommand.SetAction((parseResult, cancellationToken) =>
            {
                ServerContext.DataDirectory = parseResult.GetValue(dataDirectoryOption).FullName;
                return RunServerAsync(cancellationToken, args);
            });

            return rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration {ProcessTerminationTimeout = null}, CancellationTokenSrc.Token);
        }

        private static async Task RunServerAsync(CancellationToken cancellationToken, string[] args)
        {
            // Memory diagnostics are an opt-in operator tool. Gating them on a CLI flag
            // (rather than just the IntervalSettings entry) keeps the production log clean
            // by default and means turning diagnostics on for a hosted server is a single
            // launcher edit, not an XML edit followed by a restart.
            // Note: We're going to true it for now and return to HasFlag another time.
            var memoryDiagnosticsEnabled = HasFlag(args, "--memorydiag");

            //Verify the .NET runtime before anything else so we can give users a clear,
            //actionable message instead of failing later with a confusing error.
            DotNetRuntimeChecker.EnsureCorrectRuntimeOrExit();

            try
            {
                // Force culture to en-US to avoid 'System.Net.Sockets.resources' assembly load error.
                var ci = new CultureInfo("en-US");
                Thread.CurrentThread.CurrentCulture = ci;
                Thread.CurrentThread.CurrentUICulture = ci;

                Console.Title = $"LMP {LmpVersioning.CurrentVersion}";

                Console.OutputEncoding = Encoding.UTF8;

                LunaLog.Info("Remember! Quit the server by using 'Control + C' so a backup is properly made before closing!");
                LunaLog.Info("Documentation available at https://github.com/LunaMultiplayer/LunaMultiplayer/wiki");

                if (OperatingSystem.IsWindows())
                    ExitSignal.Exit += (sender, args) => _ = ExitAsync();
                else
                {
                    //Register the ctrl+c event and exit signal if we are on linux
                    Console.CancelKeyPress += (sender, args) => _ = ExitAsync();
                }

                //We disable quick edit as otherwise when you select some text for copy/paste then you can't write to the console and server freezes
                //This just happens on windows....
                if (OperatingSystem.IsWindows())
                    ConsoleUtil.DisableConsoleQuickEdit();

                //We cannot run more than 6 instances ofd servers + clients as otherwise the sync time will fail (30 seconds / 5 seconds = 6) but we use 3 for safety
                if (GetRunningInstances() > 3)
                    throw new HandledException("Cannot run more than 3 servers at a time!");

                //Start the server clock
                ServerContext.ServerClock.Start();

                ServerContext.ServerStarting = true;

                //Set day for log change
                ServerContext.Day = LunaNetworkTime.Now.Day;

                LunaLog.Normal($"Luna Server version: {LmpVersioning.CurrentVersion} ({AppContext.BaseDirectory})");
                LunaLog.Normal($"Server Data Directory: {ServerContext.DataDirectory}");

                // Truncate the craft create/remove audit file so each server run starts with a clean log.
                CraftCreationAndRemovalLog.Initialize();

                Universe.CheckUniverse();
                LoadSettingsAndGroups();
                VesselStoreSystem.LoadExistingVessels();
                var scenariosCreated = ScenarioSystem.GenerateDefaultScenarios();
                ScenarioStoreSystem.LoadExistingScenarios(scenariosCreated);
                LmpPluginHandler.LoadPlugins();
                WarpSystem.Reset();
                TimeSystem.Reset();

                LunaLog.Normal($"Starting '{GeneralSettings.SettingsStore.ServerName}' on Address {ConnectionSettings.SettingsStore.ListenAddress} Port {ConnectionSettings.SettingsStore.Port}... ");

                LidgrenServer.SetupLidgrenServer();
                await LmpPortMapper.OpenLmpPortAsync();
                await LmpPortMapper.OpenWebPortAsync();
                ServerContext.ServerRunning = true;
                WebServer.StartWebServer();

                //Do not add the command handler thread to the TaskContainer as it's a blocking task
                _ = LongRunTaskFactory.StartNew(CommandHandler.ThreadMainAsync, cancellationToken);

                TaskContainer.Add(LongRunTaskFactory.StartNew(WebServer.RefreshWebServerInformationAsync, cancellationToken));

                TaskContainer.Add(LongRunTaskFactory.StartNew(LmpPortMapper.RefreshUpnpPortAsync, cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(LogThread.RunLogThreadAsync, cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(ClientMainThread.ThreadMainAsync, cancellationToken));

                TaskContainer.Add(LongRunTaskFactory.StartNew(() => BackupSystem.PerformBackupsAsync(cancellationToken), cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(LidgrenServer.StartReceivingMessagesAsync, cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(LidgrenMasterServer.RegisterWithMasterServerAsync, cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(LidgrenMasterServer.CheckNATTypeAsync, cancellationToken));

                TaskContainer.Add(LongRunTaskFactory.StartNew(VersionChecker.RefreshLatestVersionAsync, cancellationToken));
                TaskContainer.Add(LongRunTaskFactory.StartNew(VersionChecker.DisplayNewVersionMsgAsync, cancellationToken));

                if (MemoryDiagnosticsEnabled)
                {
                    LunaLog.Normal("Memory diagnostics enabled (--memorydiag). [MemDiag] lines will be written to the log.");
                    TaskContainer.Add(LongRunTaskFactory.StartNew(() => MemoryDiagnosticsLogger.LogMemoryDiagnosticsAsync(CancellationTokenSrc.Token), CancellationTokenSrc.Token).Unwrap());
                }
                TaskContainer.Add(LongRunTaskFactory.StartNew(() => GcSystem.PerformGarbageCollectionAsync(cancellationToken), cancellationToken));
                while (ServerContext.ServerStarting)
                    await Task.Delay(500, cancellationToken);

                LunaLog.Normal("All systems up and running. Поехали!");
                LmpPluginHandler.FireOnServerStart();

                await QuitSignal.Task;

                LmpPluginHandler.FireOnServerStop();

                LunaLog.Normal("So long and thanks for all the fish!");

                if (IsRestart)
                {
                    //Start new server
                    var serverExePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\Server.exe";
                    var newProcLmpServer = new ProcessStartInfo { FileName = serverExePath };
                    Process.Start(newProcLmpServer);
                }
            }
            catch (Exception e)
            {
                LunaLog.Fatal(e is HandledException ? e.Message : $"Error in main server thread, Exception: {e}");
                Console.ReadLine(); //Avoid closing automatically
            }
        }

        private static void LoadSettingsAndGroups()
        {
            LunaLog.Debug("Loading groups...");
            GroupSystem.LoadGroups();
            LunaLog.Debug("Loading settings...");
            SettingsHandler.LoadSettings();
            SettingsHandler.ValidateDifficultySettings();
            DefaultSettingsChecker.WarnIfUsingDefaults();

            if (GeneralSettings.SettingsStore.ModControl)
            {
                LunaLog.Debug("Loading mod control...");
                ModFileSystem.LoadModFile();
            }

            var title = $"LMP {LmpVersioning.CurrentVersion} ({GeneralSettings.SettingsStore.ServerName})";
#if DEBUG
            title += " DEBUG";
#endif
            Console.Title = title;
        }

        /// <summary>
        /// Return the number of running instances.
        /// </summary>
        private static int GetRunningInstances() => Process.GetProcessesByName("LunaServer.exe").Length;

        /// <summary>
        /// Case-insensitive check for a single command-line flag. Kept tiny on purpose: the
        /// server has historically had no CLI surface and we don't want to grow one accidentally.
        /// If we ever need more than two or three flags, swap this for a dedicated parser.
        /// </summary>
        private static bool HasFlag(string[] args, string flag)
        {
            if (args == null) return false;
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Runs the exit logic
        /// </summary>
        private static async Task ExitAsync()
        {
            LunaLog.Normal("Exiting... Please wait until all threads are finished");
            ExitEvent.Exit();

            await CancellationTokenSrc.CancelAsync();
            await Task.WhenAll(TaskContainer);

            ServerContext.Shutdown("Server is shutting down");

            QuitSignal.TrySetResult(true);
        }

        /// <summary>
        /// Runs the restart logic
        /// </summary>
        public static async Task RestartAsync()
        {
            //Perform Backups
            await BackupSystem.PerformBackupsAsync(CancellationTokenSrc.Token);
            LunaLog.Normal("Restarting...  Please wait until all threads are finished");

            ServerContext.Shutdown("Server is restarting");
            await CancellationTokenSrc.CancelAsync();
            await Task.WhenAll(TaskContainer);

            IsRestart = true;

            QuitSignal.TrySetResult(true);
        }
    }
}
