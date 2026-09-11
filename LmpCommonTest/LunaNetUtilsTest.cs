using LmpCommon;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace LmpCommonTest
{
    [TestClass]
    public class LunaNetUtilsTest
    {

        [TestMethod]
        public void TestIsIPv6UniqueLocal()
        {
            Assert.IsTrue(IPAddress.Parse("fd94:75af:e2c6:4b56:0:242:ac11:2").IsIPv6UniqueLocal(), "IPAddress.Parse('fd94:75af:e2c6:4b56:0:242:ac11:2').IsIPv6UniqueLocal() should be true");
            Assert.IsTrue(IPAddress.Parse("fd00::1").IsIPv6UniqueLocal(), "IPAddress.Parse('fd00::1').IsIPv6UniqueLocal() should be true");
            Assert.IsFalse(IPAddress.Parse("2620:fe::fe").IsIPv6UniqueLocal(), "IPAddress.Parse('2620:fe::fe').IsIPv6UniqueLocal() should be false");
            Assert.IsFalse(IPAddress.Parse("fe80::2345:6789").IsIPv6UniqueLocal(), "IPAddress.Parse('fe80::2345:6789').IsIPv6UniqueLocal() should be false");
            Assert.IsFalse(IPAddress.Parse("192.0.2.0").IsIPv6UniqueLocal(), "IPAddress.Parse('192.0.2.0').IsIPv6UniqueLocal() should be false");
        }
    }
}