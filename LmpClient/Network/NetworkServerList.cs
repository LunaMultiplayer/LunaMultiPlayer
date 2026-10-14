using Lidgren.Network;
using LmpClient.Systems.Ping;
using LmpCommon;
using LmpCommon.Message.Data.MasterServer;
using LmpCommon.Message.MasterServer;
using LmpCommon.Time;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace LmpClient.Network
{
    public class NetworkServerList
    {
        public static string Password { get; set; } = string.Empty;
        public static ConcurrentDictionary<long, ServerInfo> Servers { get; } = new ConcurrentDictionary<long, ServerInfo>();

        private static bool receivedNATIntroductionSuccessResponse = false;

        private static int _serversVersion;

        /// <summary>
        /// Monotonic counter bumped every time the <see cref="Servers"/> collection or any of its
        /// entries changes. Consumers (e.g. the server list window) can compare against a cached value
        /// to detect whether they need to rebuild their view instead of doing it every frame.
        /// </summary>
        public static int ServersVersion => Volatile.Read(ref _serversVersion);

        /// <summary>
        /// Signals that the server list (or one of its entries) has changed.
        /// Safe to call from any thread.
        /// </summary>
        public static void NotifyServersChanged() => Interlocked.Increment(ref _serversVersion);

        /// <summary>
        /// Sends a request for the server list to the master servers
        /// </summary>
        public static void RequestServers()
        {
            Servers.Clear();
            NotifyServersChanged();
            var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<MsRequestServersMsgData>();
            var requestMsg = NetworkMain.MstSrvMsgFactory.CreateNew<MainMstSrvMsg>(msgData);
            NetworkSender.QueueOutgoingMessage(requestMsg);
        }

        /// <summary>
        /// Handles a server list response from the master servers
        /// </summary>
        public static void HandleServersList(NetIncomingMessage msg)
        {
            try
            {
                var msgDeserialized = NetworkMain.MstSrvMsgFactory.Deserialize(msg, LunaNetworkTime.UtcNow.Ticks);

                //Sometimes we receive other type of unconnected messages.
                //Therefore we assert that the received message data is of MsReplyServersMsgData
                if (msgDeserialized.Data is MsReplyServersMsgData data)
                {
                    //Filter servers with different version
                    if (!LmpVersioning.IsCompatible(data.ServerVersion))
                        return;

                    var server = new ServerInfo
                    {
                        Id = data.Id,
                        InternalEndpoint = data.InternalEndpoint,
                        InternalEndpoint6 = data.InternalEndpoint6,
                        ExternalEndpoint = data.ExternalEndpoint,
                        Description = data.Description,
                        Country = data.Country,
                        Website = data.Website,
                        WebsiteText = data.WebsiteText,
                        Password = data.Password,
                        Cheats = data.Cheats,
                        ServerName = data.ServerName,
                        MaxPlayers = data.MaxPlayers,
                        WarpMode = data.WarpMode,
                        TerrainQuality = data.TerrainQuality,
                        PlayerCount = data.PlayerCount,
                        GameMode = data.GameMode,
                        ModControl = data.ModControl,
                        DedicatedServer = data.DedicatedServer,
                        RainbowEffect = data.RainbowEffect,
                        VesselUpdatesSendMsInterval = data.VesselUpdatesSendMsInterval,
                        ServerVersion = data.ServerVersion
                    };

                    Array.Copy(data.Color, server.Color, 3);

                    Servers.AddOrUpdate(data.Id, server, (l, info) => MergeServerInfos(info, server));
                    NotifyServersChanged();
                    PingSystem.QueuePing(data.Id);
                }
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: Invalid server list reply msg: {e}");
            }
        }

        /// <summary>
        /// This merges two ServerInfo objects.
        /// It copies over data from the old object if it is empty/null/invalid in the new object.
        /// </summary>
        /// <returns>A merged ServerInfo object</returns>
        private static ServerInfo MergeServerInfos(ServerInfo existing, ServerInfo fresh)
        {
            // Some masterservers have broken country detection, so keep the existing value if any.
            if (string.IsNullOrEmpty(fresh.Country) && !string.IsNullOrEmpty(existing.Country))
                fresh.Country = existing.Country;

            return fresh;
        }

        /// <summary>
        /// Send a request to the master server to introduce us and do the NAT punchtrough to the selected server
        /// </summary>
        public static void IntroduceToServer(long serverId)
        {
            if (Servers.TryGetValue(serverId, out var serverInfo))
            {
                var amListeningOnIPv6 =
                    NetworkMain.ClientConnection.Socket.AddressFamily == AddressFamily.InterNetworkV6;

                // Ignore the loopback addresses when checking if the server is on the local network
                // The server will itself never use one of these as an internal address, so it must have been a non-local address to begin with.
                bool isValidInternalEndpoint = !(IPAddress.IsLoopback(serverInfo.InternalEndpoint.Address) ||
                           (amListeningOnIPv6 && IPAddress.IsLoopback(serverInfo.InternalEndpoint6.Address)));

                if ((ServerIsInLocalLan(serverInfo.ExternalEndpoint) || (amListeningOnIPv6 && ServerIsInLocalLan(serverInfo.InternalEndpoint6))) && isValidInternalEndpoint)
                {
                    LunaLog.Log("Server is in LAN. Skipping NAT punch");
                    var endpoints = new List<IPEndPoint>();
                    if (amListeningOnIPv6 && !serverInfo.InternalEndpoint6.Address.Equals(IPAddress.IPv6Loopback))
                        endpoints.Add(serverInfo.InternalEndpoint6);
                    if (!serverInfo.InternalEndpoint.Address.Equals(IPAddress.Loopback))
                        endpoints.Add(serverInfo.InternalEndpoint);
                    NetworkConnection.ConnectToServer(endpoints.ToArray(), Password);
                }
                else
                {
                    try
                    {
                        receivedNATIntroductionSuccessResponse = false;

                        var msgData = NetworkMain.CliMsgFactory.CreateNewMessageData<MsIntroductionMsgData>();
                        msgData.Id = serverId;
                        msgData.Token = MainSystem.UniqueIdentifier;

                        var localPort = NetworkMain.ClientConnection.Port;
                        msgData.InternalEndpoint = new IPEndPoint(LunaNetUtils.GetOwnInternalIPv4Address(), localPort);
                        // Only send IPv6 address if actually listening on IPv6, otherwise send loopback with means "none".
                        msgData.InternalEndpoint6 = amListeningOnIPv6
                            ? new IPEndPoint(LunaNetUtils.GetOwnInternalIPv6Address(), localPort)
                            : new IPEndPoint(IPAddress.IPv6Loopback, localPort);

                        var introduceMsg = NetworkMain.MstSrvMsgFactory.CreateNew<MainMstSrvMsg>(msgData);

                        MainSystem.Singleton.Status = "Requesting NAT introduction";
                        LunaLog.Log($"[LMP]: Sending NAT introduction request to master servers. " +
                                    $"Token: {MainSystem.UniqueIdentifier}, " +
                                    $"Internal Endpoint: {msgData.InternalEndpoint}, " +
                                    $"Internal Endpoint v6: {msgData.InternalEndpoint6}");
                        NetworkSender.QueueOutgoingMessage(introduceMsg);

                        // If the NAT introduction didn't succeed after 10s, show a
                        Base.SystemBase.TaskFactory.StartNew(() => {
                            Thread.Sleep(10000);
                            if (!receivedNATIntroductionSuccessResponse)
                            {
                                LunaLog.Log("[LMP]: NAT introduction did not succeed after 10 seconds");
                                MainSystem.Singleton.Status = "Error: NAT introduction timeout";
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        LunaLog.LogError($"[LMP]: Error connecting to server: {e}");
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the server is running in a local LAN
        /// </summary>
        private static bool ServerIsInLocalLan(IPEndPoint serverEndPoint)
        {
            if (serverEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var ownNetwork = LunaNetUtils.GetOwnInternalIPv6Network();
                if (ownNetwork == null)
                    return false;

                // For IPv6, we strip both addresses down to the subnet portion (likely the first 64 bits) and compare them.
                // Because we only receive Global Unique Addresses from GetOwnInternalIPv6Network() (which are globally
                // unique, as the name suggests and the RFCs define), those being equal should mean both are on the same network.
                var ownBytes = ownNetwork.Address.GetAddressBytes();
                var serverBytes = serverEndPoint.Address.GetAddressBytes();
                // TODO IPv6: We currently assume an on-link prefix length of 64 bits, which is the most common case
                // and standardized as per the RFCs. UnicastIPAddressInformation.PrefixLength is not implemented yet,
                // and also wouldn't be reliable (hosts often assign their address as /128). A possible solution could be
                // checking whether serverEndPoint matches any configured on-link/no-gateway route.
                Array.Resize(ref ownBytes, 8);
                Array.Resize(ref serverBytes, 8);
                if (ownBytes == serverBytes)
                    return true;
            }
            else
            {
                return Equals(LunaNetUtils.GetOwnExternalIpAddress(), serverEndPoint.Address);
            }
            return false;
        }

        /// <summary>
        /// We received a NAT punchtrough response from the server, so connect to it
        /// </summary>
        public static void HandleNatIntroductionSuccess(NetIncomingMessage msg)
        {
            if (MainSystem.UniqueIdentifier == msg.ReadString())
            {
                LunaLog.Log($"[LMP]: Nat introduction success against {msg.SenderEndPoint}. Token: {MainSystem.UniqueIdentifier}");
                receivedNATIntroductionSuccessResponse = true;
                NetworkConnection.ConnectToServer(new []{ msg.SenderEndPoint }, Password);
            }
            else
            {
                LunaLog.LogError($"[LMP]: Incorrect client identifier in NAT introduction success response from {msg.SenderEndPoint}. Token: {MainSystem.UniqueIdentifier}");
            }
        }
    }
}
