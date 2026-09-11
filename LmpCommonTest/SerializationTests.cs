using Lidgren.Network;
using LmpCommon.Message;
using LmpCommon.Message.Client;
using LmpCommon.Message.Data.Chat;
using LmpCommon.Message.Data.Kerbal;
using LmpCommon.Message.Data.Vessel;
using LmpCommon.Message.Data.Waypoint;
using LmpCommon.Message.Server;
using LmpCommon.Xml;
using LmpCommonTest.Properties;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LmpCommonTest
{
    [TestClass]
    public class SerializationTests
    {
        private static readonly ServerMessageFactory Factory = new ServerMessageFactory();
        private static readonly ClientMessageFactory CliFactory = new ClientMessageFactory();
        private static readonly Random Rnd = new Random();
        private static readonly NetClient Client = new NetClient(new NetPeerConfiguration("TESTS"));

        [TestMethod]
        public void TestSerializeDeserializeVesselUpdateMsg()
        {
            var msgData = Factory.CreateNewMessageData<VesselUpdateMsgData>();
            msgData.VesselId = Guid.NewGuid();
            msgData.Name = "Name";
            msgData.Type = "Type";
            msgData.DistanceTraveled = 222;
            msgData.Situation = "Situation";
            msgData.Landed = true;
            msgData.LandedAt = "LandedAt";
            msgData.DisplayLandedAt = "DisplayLandedAt";
            msgData.Splashed = false;
            msgData.MissionTime = Rnd.NextDouble();
            msgData.LaunchTime = Rnd.NextDouble();
            msgData.LastUt = Rnd.NextDouble();
            msgData.Persistent = false;
            msgData.RefTransformId = (uint)Rnd.Next();
            msgData.AutoClean = false;
            msgData.AutoCleanReason = string.Empty;
            msgData.WasControllable = true;
            msgData.Stage = 0;
            msgData.Com[0] = 0;
            msgData.Com[1] = 0;
            msgData.Com[2] = 0;
            msgData.BodyName = "Kerbin";

            var msg = Factory.CreateNew<VesselCliMsg>(msgData);

            //Serialize
            var expectedDataSize = msg.GetMessageSize();
            var lidgrenMsgSend = Client.CreateMessage(expectedDataSize);
            msg.Serialize(lidgrenMsgSend);
            var realSize = lidgrenMsgSend.LengthBytes;

            //Usually the expected size will be a bit more as Lidgren writes the size of the strings in a base128 int (so it uses less bytes)
            Assert.IsTrue(expectedDataSize >= realSize);

            //Simulate sending
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;

            msg.Recycle();

            //Deserialize
            var msgDes = Factory.Deserialize(lidgrenMsgRecv, Environment.TickCount);

        }

        [TestMethod]
        public void TestSerializeDeserializeChatChannelMsg()
        {
            var msgData = Factory.CreateNewMessageData<ChatMsgData>();
            msgData.Text = "T";

            var msg = Factory.CreateNew<ChatCliMsg>(msgData);

            //Serialize
            var expectedDataSize = msg.GetMessageSize();
            var lidgrenMsgSend = Client.CreateMessage(expectedDataSize);
            msg.Serialize(lidgrenMsgSend);
            var realSize = lidgrenMsgSend.LengthBytes;

            //Usually the expected size will be a bit more as Lidgren writes the size of the strings in a base128 int (so it uses less bytes)
            Assert.IsTrue(expectedDataSize >= realSize);

            //Simulate sending
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;

            msg.Recycle();

            //Deserialize
            var msgDes = Factory.Deserialize(lidgrenMsgRecv, Environment.TickCount);
        }

        [TestMethod]
        public void TestSerializeCompressThreadSafety()
        {
            const int iterations = 2000;

            var msgData = Factory.CreateNewMessageData<KerbalProtoMsgData>();
            msgData.Kerbal.KerbalName = "TEST";
            msgData.Kerbal.KerbalData = Encoding.UTF8.GetBytes(Resources.Jebediah_Kerman);
            msgData.Kerbal.NumBytes = msgData.Kerbal.KerbalData.Length;

            var msg = Factory.CreateNew<KerbalCliMsg>(msgData);

            Parallel.For(0, iterations, _ =>
            {
                try
                {
                    msg.Serialize(Client.CreateMessage());
                }
                catch (Exception ex)
                {
                    throw new AggregateException("Serialize failed under parallel load", ex);
                }
            });
        }

        [TestMethod]
        public void TestSerializeDeserializeWaypointCreateMsg()
        {
            var msgData = Factory.CreateNewMessageData<WaypointCreateMsgData>();
            msgData.Waypoint.Name = "Test Waypoint";
            msgData.Waypoint.CelestialName = "Kerbin";
            msgData.Waypoint.Latitude = Rnd.NextDouble() * 90;
            msgData.Waypoint.Longitude = Rnd.NextDouble() * 180;
            msgData.Waypoint.NavigationId = Guid.NewGuid().ToString();

            var msg = Factory.CreateNew<WaypointCliMsg>(msgData);

            //Serialize
            var expectedDataSize = msg.GetMessageSize();
            var lidgrenMsgSend = Client.CreateMessage(expectedDataSize);
            msg.Serialize(lidgrenMsgSend);
            var realSize = lidgrenMsgSend.LengthBytes;

            //Usually the expected size will be a bit more as Lidgren writes the size of the strings in a base128 int (so it uses less bytes)
            Assert.IsTrue(expectedDataSize >= realSize);

            //Simulate sending
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;

            msg.Recycle();

            //Deserialize
            var msgDes = CliFactory.Deserialize(lidgrenMsgRecv, Environment.TickCount) as WaypointCliMsg;
            Assert.IsNotNull(msgDes);
            var deserializedData = msgDes.Data as WaypointCreateMsgData;
            Assert.IsNotNull(deserializedData);

            Assert.AreEqual(msgData.Waypoint.Name, deserializedData.Waypoint.Name);
            Assert.AreEqual(msgData.Waypoint.CelestialName, deserializedData.Waypoint.CelestialName);
            Assert.AreEqual(msgData.Waypoint.Latitude, deserializedData.Waypoint.Latitude);
            Assert.AreEqual(msgData.Waypoint.Longitude, deserializedData.Waypoint.Longitude);
            Assert.AreEqual(msgData.Waypoint.NavigationId, deserializedData.Waypoint.NavigationId);
        }

        [TestMethod]
        public void TestSerializeDeserializeWaypointListResponseMsg()
        {
            var msgData = Factory.CreateNewMessageData<WaypointListResponseMsgData>();
            msgData.Waypoints = new[]
            {
                new WaypointInfo { Name = "Waypoint 1", CelestialName = "Kerbin", Latitude = 1.5, Longitude = -2.5, NavigationId = Guid.NewGuid().ToString() },
                new WaypointInfo { Name = "Waypoint 2", CelestialName = "Eve", Latitude = -0.5, Longitude = 90.25, NavigationId = Guid.NewGuid().ToString() },
            };
            msgData.WaypointsCount = msgData.Waypoints.Length;

            var msg = Factory.CreateNew<WaypointSrvMsg>(msgData);

            //Serialize
            var expectedDataSize = msg.GetMessageSize();
            var lidgrenMsgSend = Client.CreateMessage(expectedDataSize);
            msg.Serialize(lidgrenMsgSend);
            var realSize = lidgrenMsgSend.LengthBytes;

            Assert.IsTrue(expectedDataSize >= realSize);

            //Simulate sending
            var data = lidgrenMsgSend.ReadBytes(lidgrenMsgSend.LengthBytes);
            var lidgrenMsgRecv = Client.CreateIncomingMessage(NetIncomingMessageType.Data, data);
            lidgrenMsgRecv.LengthBytes = lidgrenMsgSend.LengthBytes;

            msg.Recycle();

            //Deserialize
            var msgDes = Factory.Deserialize(lidgrenMsgRecv, Environment.TickCount) as WaypointSrvMsg;
            Assert.IsNotNull(msgDes);
            var deserializedData = msgDes.Data as WaypointListResponseMsgData;
            Assert.IsNotNull(deserializedData);

            Assert.AreEqual(2, deserializedData.WaypointsCount);
            Assert.AreEqual(msgData.Waypoints[0].NavigationId, deserializedData.Waypoints[0].NavigationId);
            Assert.AreEqual(msgData.Waypoints[1].Name, deserializedData.Waypoints[1].Name);
            Assert.AreEqual(msgData.Waypoints[1].Longitude, deserializedData.Waypoints[1].Longitude);
        }

        [TestMethod]
        public void TestWaypointInfoXmlRoundTrip()
        {
            var waypoints = new List<WaypointInfo>
            {
                new WaypointInfo { Name = "Waypoint 1", CelestialName = "Kerbin", Latitude = 1.5, Longitude = -2.5, NavigationId = Guid.NewGuid().ToString() },
                new WaypointInfo { Name = "Waypoint 2", CelestialName = "Eve", Latitude = -12.5, Longitude = 12.5, NavigationId = Guid.NewGuid().ToString() },
            };

            var path = Path.Combine(Path.GetTempPath(), $"LmpWaypointTest_{Guid.NewGuid():N}.xml");
            try
            {
                LunaXmlSerializer.WriteToXmlFile(waypoints, path);
                var deserialized = LunaXmlSerializer.ReadXmlFromPath<List<WaypointInfo>>(path);

                Assert.AreEqual(2, deserialized.Count);
                Assert.AreEqual(waypoints[0].NavigationId, deserialized[0].NavigationId);
                Assert.AreEqual(waypoints[0].Name, deserialized[0].Name);
                Assert.AreEqual(waypoints[0].CelestialName, deserialized[0].CelestialName);
                Assert.AreEqual(waypoints[0].Latitude, deserialized[0].Latitude);
                Assert.AreEqual(waypoints[0].Longitude, deserialized[0].Longitude);
                Assert.AreEqual(waypoints[1].NavigationId, deserialized[1].NavigationId);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void TestXmlSerializationUsesUtf8Declaration()
        {
            var serialized = LunaXmlSerializer.SerializeToXml(new XmlSerializationTestData { Value = "Test" });

            StringAssert.StartsWith(serialized, "<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        }

        public class XmlSerializationTestData
        {
            public string Value { get; set; }
        }
    }
}
