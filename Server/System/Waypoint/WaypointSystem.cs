using LmpCommon.Message.Data.Waypoint;
using LmpCommon.Message.Interface;
using LmpCommon.Message.Server;
using LmpCommon.Message.Types;
using LmpCommon.Xml;
using Server.Client;
using Server.Context;
using Server.Message.Base;
using Server.Server;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Server.System.Waypoint
{
    public static class WaypointSystem
    {
        /// <summary>
        /// The serializer is not thread safe so we need a lock
        /// </summary>
        private static readonly object FileLock = new object();

        public static readonly string WaypointsPath = Path.Combine(ServerContext.UniverseDirectory, "Waypoints");
        private static readonly string WaypointsFilePath = Path.Combine(WaypointsPath, "Waypoints.xml");

        /// <summary>
        /// All the custom waypoints of the universe, keyed by the waypoint navigation id
        /// </summary>
        public static ConcurrentDictionary<string, WaypointInfo> Waypoints { get; } = new ConcurrentDictionary<string, WaypointInfo>();

        public static void CreateWaypoint(WaypointInfo waypoint)
        {
            if (waypoint == null || string.IsNullOrEmpty(waypoint.NavigationId)) return;

            Waypoints.AddOrUpdate(waypoint.NavigationId, waypoint, (key, existingVal) => waypoint);

            var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WaypointCreateMsgData>();
            msgData.Waypoint = waypoint;

            MessageQueuer.SendToAllClients<WaypointSrvMsg>(msgData);
            _ = Task.Run(() => SaveWaypoints());
        }

        public static void RemoveWaypoint(string navigationId)
        {
            if (string.IsNullOrEmpty(navigationId)) return;

            if (Waypoints.TryRemove(navigationId, out _))
            {
                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WaypointRemoveMsgData>();
                msgData.NavigationId = navigationId;

                MessageQueuer.SendToAllClients<WaypointSrvMsg>(msgData);
                _ = Task.Run(() => SaveWaypoints());
            }
        }

        public static void SaveWaypoints()
        {
            lock (FileLock)
            {
                if (FileHandler.FolderExists(WaypointsPath))
                    LunaXmlSerializer.WriteToXmlFile(Waypoints.Values.ToList(), WaypointsFilePath);
            }
        }

        public static void LoadWaypoints()
        {
            lock (FileLock)
            {
                if (File.Exists(WaypointsFilePath))
                {
                    var values = LunaXmlSerializer.ReadXmlFromPath<List<WaypointInfo>>(WaypointsFilePath);
                    foreach (var value in values)
                    {
                        if (!string.IsNullOrEmpty(value.NavigationId))
                            Waypoints.TryAdd(value.NavigationId, value);
                    }
                }
            }
        }
    }

    public class WaypointMsgReader : ReaderBase
    {
        public override void HandleMessage(ClientStructure client, IClientMessageBase message)
        {
            var data = (WaypointBaseMsgData)message.Data;
            switch (data.WaypointMessageType)
            {
                case WaypointMessageType.ListRequest:

                    var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<WaypointListResponseMsgData>();
                    msgData.Waypoints = WaypointSystem.Waypoints.Values.ToArray();
                    msgData.WaypointsCount = msgData.Waypoints.Length;
                    MessageQueuer.SendToClient<WaypointSrvMsg>(client, msgData);

                    //We don't use this message anymore so we can recycle it
                    message.Recycle();
                    break;
                case WaypointMessageType.WaypointCreate:
                    WaypointSystem.CreateWaypoint(((WaypointCreateMsgData)data).Waypoint);
                    break;
                case WaypointMessageType.WaypointRemove:
                    WaypointSystem.RemoveWaypoint(((WaypointRemoveMsgData)data).NavigationId);
                    break;
            }
        }
    }
}
