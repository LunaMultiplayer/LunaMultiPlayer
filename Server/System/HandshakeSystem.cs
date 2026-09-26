using LmpCommon.Enums;
using LmpCommon.Message.Data.Handshake;
using LmpCommon.Message.Data.PlayerConnection;
using LmpCommon.Message.Server;
using Server.Client;
using Server.Context;
using Server.Log;
using Server.Plugin;
using Server.Server;

namespace Server.System
{
    public partial class HandshakeSystem
    {
        public void HandleHandshakeRequest(ClientStructure client, HandshakeRequestMsgData data)
        {
            var valid = CheckServerFull(client, out var reason);
            valid &= valid && CheckUsernameLength(client, data.PlayerName, out reason);
            valid &= valid && CheckUsernameCharacters(client, data.PlayerName, out reason);
            valid &= valid && CheckPlayerIsAlreadyConnected(client, data.PlayerName, out reason);
            valid &= valid && CheckUsernameIsReserved(client, data.PlayerName, out reason);
            valid &= valid && CheckPlayerIsBanned(client, data.UniqueIdentifier, out reason);

            if (!valid)
            {
                LunaLog.Normal($"Client {data.PlayerName} ({data.UniqueIdentifier}) failed to handshake: {reason}. Disconnecting");
                client.DisconnectClient = true;
                ClientConnectionHandler.DisconnectClient(client, reason);
            }
            else
            {
                client.PlayerName = data.PlayerName;
                client.UniqueIdentifier = data.UniqueIdentifier;
                client.KspVersion = string.IsNullOrWhiteSpace(data.KspVersion) ? "Unknown" : data.KspVersion;
                client.LmpVersion = $"{data.MajorVersion}.{data.MinorVersion}.{data.BuildVersion}";
                client.Authenticated = true;

                PauseWhenEmptySystem.HandlePlayerAuthenticated();
                LmpPluginHandler.FireOnClientAuthenticated(client);

                LunaLog.Normal($"Client {data.PlayerName} ({data.UniqueIdentifier}) handshake successful, LMP Version: {client.LmpVersion}, KSP Version: {client.KspVersion}");

                HandshakeSystemSender.SendHandshakeReply(client, HandshakeReply.HandshookSuccessfully, "success");

                var msgData = ServerContext.ServerMessageFactory.CreateNewMessageData<PlayerConnectionJoinMsgData>();
                msgData.PlayerName = client.PlayerName;
                MessageQueuer.RelayMessage<PlayerConnectionSrvMsg>(client, msgData);

                LunaLog.Debug($"Online Players: {ServerContext.PlayerCount}, connected: {ClientRetriever.GetClients().Length}");
            }
        }
    }
}
