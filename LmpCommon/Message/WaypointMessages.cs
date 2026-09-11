using Lidgren.Network;
using LmpCommon.Enums;
using LmpCommon.Message.Base;
using LmpCommon.Message.Client.Base;
using LmpCommon.Message.Data.Waypoint;
using LmpCommon.Message.Server.Base;
using LmpCommon.Message.Types;
using System;
using System.Collections.Generic;

namespace LmpCommon.Message.Types
{
    public enum WaypointMessageType
    {
        ListRequest = 0,
        ListResponse = 1,
        WaypointCreate = 2,
        WaypointRemove = 3,
    }
}

namespace LmpCommon.Message.Data.Waypoint
{
    /// <summary>
    /// A custom map waypoint. Mirrors the data that KSP persists for custom waypoints
    /// (name, body, latitude, longitude and the navigation guid), which is everything
    /// needed to recreate the waypoint on another client.
    /// </summary>
    [Serializable]
    public class WaypointInfo
    {
        public string Name;
        public string CelestialName;
        public double Latitude;
        public double Longitude;
        /// <summary>
        /// The "navigationId" of the game waypoint as a string. This is the unique identifier of the waypoint.
        /// </summary>
        public string NavigationId;

        public WaypointInfo Clone()
        {
            if (MemberwiseClone() is WaypointInfo obj)
            {
                obj.Name = Name.Clone() as string;
                obj.CelestialName = CelestialName.Clone() as string;
                obj.NavigationId = NavigationId.Clone() as string;

                return obj;
            }

            return null;
        }

        public void Serialize(NetOutgoingMessage lidgrenMsg)
        {
            lidgrenMsg.Write(Name);
            lidgrenMsg.Write(CelestialName);
            lidgrenMsg.Write(Latitude);
            lidgrenMsg.Write(Longitude);
            lidgrenMsg.Write(NavigationId);
        }

        public void Deserialize(NetIncomingMessage lidgrenMsg)
        {
            Name = lidgrenMsg.ReadString();
            CelestialName = lidgrenMsg.ReadString();
            Latitude = lidgrenMsg.ReadDouble();
            Longitude = lidgrenMsg.ReadDouble();
            NavigationId = lidgrenMsg.ReadString();
        }

        public int GetByteCount()
        {
            return Name.GetByteCount() + CelestialName.GetByteCount() +
                sizeof(double) + sizeof(double) +
                NavigationId.GetByteCount();
        }
    }

    public abstract class WaypointBaseMsgData : MessageData
    {
        /// <inheritdoc />
        internal WaypointBaseMsgData() { }
        public override ushort SubType => (ushort)(int)WaypointMessageType;

        public virtual WaypointMessageType WaypointMessageType => throw new NotImplementedException();

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            //Nothing to implement here
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            //Nothing to implement here
        }

        internal override int InternalGetMessageSize()
        {
            return 0;
        }
    }

    public class WaypointListRequestMsgData : WaypointBaseMsgData
    {
        /// <inheritdoc />
        internal WaypointListRequestMsgData() { }
        public override WaypointMessageType WaypointMessageType => WaypointMessageType.ListRequest;

        public override string ClassName { get; } = nameof(WaypointListRequestMsgData);
    }

    public class WaypointListResponseMsgData : WaypointBaseMsgData
    {
        /// <inheritdoc />
        internal WaypointListResponseMsgData() { }
        public override WaypointMessageType WaypointMessageType => WaypointMessageType.ListResponse;

        public int WaypointsCount;
        public WaypointInfo[] Waypoints = new WaypointInfo[0];

        public override string ClassName { get; } = nameof(WaypointListResponseMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write(WaypointsCount);
            for (var i = 0; i < WaypointsCount; i++)
            {
                Waypoints[i].Serialize(lidgrenMsg);
            }
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            WaypointsCount = lidgrenMsg.ReadInt32();
            if (Waypoints.Length < WaypointsCount)
                Waypoints = new WaypointInfo[WaypointsCount];

            for (var i = 0; i < WaypointsCount; i++)
            {
                if (Waypoints[i] == null)
                    Waypoints[i] = new WaypointInfo();

                Waypoints[i].Deserialize(lidgrenMsg);
            }
        }

        internal override int InternalGetMessageSize()
        {
            var arraySize = 0;
            for (var i = 0; i < WaypointsCount; i++)
            {
                arraySize += Waypoints[i].GetByteCount();
            }

            return base.InternalGetMessageSize() + sizeof(int) + arraySize;
        }
    }

    public class WaypointCreateMsgData : WaypointBaseMsgData
    {
        /// <inheritdoc />
        internal WaypointCreateMsgData() { }
        public override WaypointMessageType WaypointMessageType => WaypointMessageType.WaypointCreate;

        public WaypointInfo Waypoint = new WaypointInfo();

        public override string ClassName { get; } = nameof(WaypointCreateMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            Waypoint.Serialize(lidgrenMsg);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            Waypoint.Deserialize(lidgrenMsg);
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + Waypoint.GetByteCount();
        }
    }

    public class WaypointRemoveMsgData : WaypointBaseMsgData
    {
        /// <inheritdoc />
        internal WaypointRemoveMsgData() { }
        public override WaypointMessageType WaypointMessageType => WaypointMessageType.WaypointRemove;

        public string NavigationId;

        public override string ClassName { get; } = nameof(WaypointRemoveMsgData);

        internal override void InternalSerialize(NetOutgoingMessage lidgrenMsg)
        {
            base.InternalSerialize(lidgrenMsg);

            lidgrenMsg.Write(NavigationId);
        }

        internal override void InternalDeserialize(NetIncomingMessage lidgrenMsg)
        {
            base.InternalDeserialize(lidgrenMsg);

            NavigationId = lidgrenMsg.ReadString();
        }

        internal override int InternalGetMessageSize()
        {
            return base.InternalGetMessageSize() + NavigationId.GetByteCount();
        }
    }
}

namespace LmpCommon.Message.Client
{
    public class WaypointCliMsg : CliMsgBase<WaypointBaseMsgData>
    {
        /// <inheritdoc />
        internal WaypointCliMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(WaypointCliMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)WaypointMessageType.ListRequest] = typeof(WaypointListRequestMsgData),
            [(ushort)WaypointMessageType.ListResponse] = typeof(WaypointListResponseMsgData),
            [(ushort)WaypointMessageType.WaypointCreate] = typeof(WaypointCreateMsgData),
            [(ushort)WaypointMessageType.WaypointRemove] = typeof(WaypointRemoveMsgData)
        };

        public override ClientMessageType MessageType => ClientMessageType.Waypoint;

        protected override int DefaultChannel => 11;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}

namespace LmpCommon.Message.Server
{
    public class WaypointSrvMsg : SrvMsgBase<WaypointBaseMsgData>
    {
        /// <inheritdoc />
        internal WaypointSrvMsg() { }

        /// <inheritdoc />
        public override string ClassName { get; } = nameof(WaypointSrvMsg);

        /// <inheritdoc />
        protected override Dictionary<ushort, Type> SubTypeDictionary { get; } = new Dictionary<ushort, Type>
        {
            [(ushort)WaypointMessageType.ListRequest] = typeof(WaypointListRequestMsgData),
            [(ushort)WaypointMessageType.ListResponse] = typeof(WaypointListResponseMsgData),
            [(ushort)WaypointMessageType.WaypointCreate] = typeof(WaypointCreateMsgData),
            [(ushort)WaypointMessageType.WaypointRemove] = typeof(WaypointRemoveMsgData)
        };

        public override ServerMessageType MessageType => ServerMessageType.Waypoint;

        protected override int DefaultChannel => 11;

        public override NetDeliveryMethod NetDeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
