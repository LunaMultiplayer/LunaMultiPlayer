using Lidgren.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using LmpCommon.Time;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Message;
using Server.Message.Base;
using Server.System.Waypoint;
using Server.Plugin;
using System;
using System.Collections.Generic;

namespace Server.Server
{
    public class MessageReceiver
    {
        #region Handlers

        private static readonly Dictionary<ClientMessageType, ReaderBase> HandlerDictionary = new Dictionary
            <ClientMessageType, ReaderBase>
        {
            [ClientMessageType.Admin] = new AdminMsgReader(),
            [ClientMessageType.Handshake] = new HandshakeMsgReader(),
            [ClientMessageType.Chat] = new ChatMsgReader(),
            [ClientMessageType.PlayerStatus] = new PlayerStatusMsgReader(),
            [ClientMessageType.PlayerColor] = new PlayerColorMsgReader(),
            [ClientMessageType.Scenario] = new ScenarioDataMsgReader(),
            [ClientMessageType.Kerbal] = new KerbalMsgReader(),
            [ClientMessageType.Settings] = new SettingsMsgReader(),
            [ClientMessageType.Vessel] = new VesselMsgReader(),
            [ClientMessageType.CraftLibrary] = new CraftLibraryMsgReader(),
            [ClientMessageType.Flag] = new FlagSyncMsgReader(),
            [ClientMessageType.Motd] = new MotdMsgReader(),
            [ClientMessageType.Warp] = new WarpControlMsgReader(),
            [ClientMessageType.Lock] = new LockSystemMsgReader(),
            [ClientMessageType.Mod] = new ModDataMsgReader(),
            [ClientMessageType.Groups] = new GroupMsgReader(),
            [ClientMessageType.Facility] = new FacilityMsgReader(),
            [ClientMessageType.Screenshot] = new ScreenshotMsgReader(),
            [ClientMessageType.ShareProgress] = new ShareProgressMsgReader(),
            [ClientMessageType.Waypoint] = new WaypointMsgReader(),
        };

        #endregion

        public void ReceiveCallback(ClientStructure client, NetIncomingMessage msg)
        {
            if (client == null || msg.LengthBytes <= 1) return;

            if (client.ConnectionStatus == ConnectionStatus.Connected)
                client.LastReceiveTime = ServerContext.ServerClock.ElapsedMilliseconds;

            var message = DeserializeMessage(msg);
            if (message == null) return;

            LmpPluginHandler.FireOnMessageReceived(client, message);
            //A plugin has handled this message and requested suppression of the default behavior
            if (message.Handled) return;

            if (message.VersionMismatch)
            {
                MessageQueuer.SendConnectionEnd(client, $"Version mismatch: Your version ({message.Data.MajorVersion}.{message.Data.MinorVersion}.{message.Data.BuildVersion}) " +
                                                        $"does not match the server version: {LmpVersioning.CurrentVersion}.");
                return;
            }

            //Clients can only send HANDSHAKE until they are Authenticated.
            if (!client.Authenticated && message.MessageType != ClientMessageType.Handshake)
            {
                MessageQueuer.SendConnectionEnd(client, $"You must authenticate before sending a {message.MessageType} message");
                return;
            }

            //Handle the message
            try
            {
                HandlerDictionary[message.MessageType].HandleMessage(client, message);
            }
            catch (Exception e)
            {
                LunaLog.Error($"Error handling a message from {client.PlayerName}! {e}");
            }
        }

        private static IClientMessageBase DeserializeMessage(NetIncomingMessage msg)
        {
            try
            {
                return ServerContext.ClientMessageFactory.Deserialize(msg, LunaNetworkTime.UtcNow.Ticks) as IClientMessageBase;
            }
            catch (Exception e)
            {
                LunaLog.Error($"Error deserializing message! {e}");
                return null;
            }
        }
    }
}
