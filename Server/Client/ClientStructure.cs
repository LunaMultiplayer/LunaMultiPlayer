using Lidgren.Network;
using LmpCommon;
using LmpCommon.Enums;
using LmpCommon.Message.Interface;
using Server.Context;
using Server.Plugin;
using Server.Server;
using Server.Settings.Structures;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Server.Client
{
    public class ClientStructure
    {
        public IPEndPoint Endpoint => Connection.RemoteEndPoint;

        public string UniqueIdentifier { get; set; }
        public string KspVersion { get; set; }
        public string LmpVersion { get; set; }

        public bool Authenticated { get; set; }

        public long BytesReceived { get; set; }
        public long BytesSent { get; set; }
        public NetConnection Connection { get; }

        public ConnectionStatus ConnectionStatus { get; set; } = ConnectionStatus.Connected;
        public bool DisconnectClient { get; set; }
        public long LastReceiveTime { get; set; } = ServerContext.ServerClock.ElapsedMilliseconds;
        public long LastSendTime { get; set; } = 0;
        public float[] PlayerColor { get; set; } = new float[3];
        public string PlayerName { get; set; } = "Unknown";
        public PlayerStatus PlayerStatus { get; set; } = new PlayerStatus();
        public ConcurrentQueue<IServerMessageBase> SendMessageQueue { get; } = new ConcurrentQueue<IServerMessageBase>();
        public int Subspace { get; set; } = int.MinValue; //Leave it as min value. When client connect we force them client side to go to latest subspace
        public float SubspaceRate { get; set; } = 1f;

        public DateTime ConnectionTime { get; } = DateTime.UtcNow;
        public int CraftLibraryTransferInProgress;

        public Task SendThread { get; }

        public ClientStructure(NetConnection playerConnection)
        {
            Connection = playerConnection;
            SendThread = MainServer.LongRunTaskFactory.StartNew(() => SendMessagesThreadAsync(MainServer.CancellationTokenSrc.Token), MainServer.CancellationTokenSrc.Token);
        }

        public override bool Equals(object obj)
        {
            var clientToCompare = obj as ClientStructure;
            return Endpoint.Equals(clientToCompare?.Endpoint);
        }

        public override int GetHashCode()
        {
            return Endpoint?.GetHashCode() ?? 0;
        }

        private const int MaxMessagesPerBatch = 128;

        private async Task SendMessagesThreadAsync(CancellationToken token)
        {
            while (ConnectionStatus == ConnectionStatus.Connected)
            {
                var sentCount = 0;
                while (sentCount < MaxMessagesPerBatch && SendMessageQueue.TryDequeue(out var message) && message != null)
                {
                    try
                    {
                        LidgrenServer.SendMessageToClient(this, message);
                        sentCount++;
                    }
                    catch (Exception e)
                    {
                        ClientException.HandleDisconnectException("Send network message error: ", this, e);
                        return;
                    }

                    LmpPluginHandler.FireOnMessageSent(this, message);
                }

                if (sentCount > 0)
                {
                    LidgrenServer.FlushSendQueue();
                    continue;
                }

                try
                {
                    await Task.Delay(IntervalSettings.SettingsStore.SendReceiveThreadTickMs, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
