using Lidgren.Network;
using LmpCommon.Message;
using Server.Client;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

namespace Server.Context
{
    public static class ServerContext
    {
        public static int PlayerCount => ClientRetriever.GetActiveClientCount();
        public static readonly ConcurrentDictionary<IPEndPoint, ClientStructure> Clients = new ConcurrentDictionary<IPEndPoint, ClientStructure>();

        public static volatile bool ServerRunning;
        public static volatile bool ServerStarting;
        public static volatile int Day;

        public static string Players => ClientRetriever.GetActivePlayerNames();
        public static bool UsePassword => !string.IsNullOrEmpty(GeneralSettings.SettingsStore.Password);

        public static Stopwatch ServerClock = new Stopwatch();
        public static string DataDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static string UniverseDirectory => Path.Combine(DataDirectory, "Universe");
        public static string ConfigDirectory => Path.Combine(DataDirectory, "Config");
        public static string BackupDirectory = Path.Combine(DataDirectory, "Backups");
        public static string ModFilePath => Path.Combine(ConfigDirectory, "LMPModControl.xml");
        public static string OldModFilePath => Path.Combine(DataDirectory, "LMPModControl.xml");

        // Configuration object
        public static NetPeerConfiguration Config { get; } = new NetPeerConfiguration("LMP")
        {
            SendBufferSize = 4194304, //4 MB
            ReceiveBufferSize = 4194304, //4 MB
            DefaultOutgoingMessageCapacity = 512000, //~500 KB
            SuppressUnreliableUnorderedAcks = true,
        };

        public static MasterServerMessageFactory MasterServerMessageFactory { get; } = new MasterServerMessageFactory();
        public static ServerMessageFactory ServerMessageFactory { get; } = new ServerMessageFactory();
        public static ClientMessageFactory ClientMessageFactory { get; } = new ClientMessageFactory();

        public static void Shutdown(string reason)
        {
            MessageQueuer.SendConnectionEndToAll(reason);
            Thread.Sleep(250);
            ServerStarting = false;
            ServerRunning = false;
        }
    }
}
