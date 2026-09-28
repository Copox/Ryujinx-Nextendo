using NUnit.Framework;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.Tests.HLE
{
    public class VioletGamesyncRouteTests
    {
        private static readonly IPAddress Nextendo = IPAddress.Loopback;

        [TestCase(0x01008F6008C5E000UL, SocketType.Stream, ProtocolType.Tcp, false, false, false, 8463)]
        [TestCase(0x01008F6008C5E800UL, SocketType.Stream, ProtocolType.Tcp, false, true, false, 8463)]
        [TestCase(0x0100A3D008C5C000UL, SocketType.Stream, ProtocolType.Tcp, false, false, false, 8463)]
        [TestCase(0x0100A3D008C5C800UL, SocketType.Stream, ProtocolType.Tcp, false, true, false, 8463)]
        [TestCase(0x0100A3D008C5C000UL, SocketType.Dgram, ProtocolType.Udp, false, false, false, 7575)]
        [TestCase(0x0100A3D008C5C000UL, SocketType.Stream, ProtocolType.Tcp, true, false, false, 7575)]
        [TestCase(0x0100A3D008C5C000UL, SocketType.Stream, ProtocolType.Tcp, false, false, true, 7575)]
        [TestCase(0x0100C2500FC20000UL, SocketType.Stream, ProtocolType.Tcp, false, false, false, 7575)]
        [TestCase(0x01008F6008C5E000UL, SocketType.Dgram, ProtocolType.Udp, false, false, false, 7575)]
        [TestCase(0x01008F6008C5E000UL, SocketType.Stream, ProtocolType.Tcp, true, false, false, 7575)]
        [TestCase(0x01008F6008C5E000UL, SocketType.Stream, ProtocolType.Tcp, false, false, true, 7575)]
        public void OnlyVioletTcpForNextendoUsesDedicatedPort(ulong programId, SocketType socketType,
            ProtocolType protocolType, bool customServer, bool mapped, bool differentAddress, int expectedPort)
        {
            IPAddress address = differentAddress ? IPAddress.None : mapped ? Nextendo.MapToIPv6() : Nextendo;
            IPEndPoint endpoint = new(address, 7575);
            IPEndPoint routed = ManagedSocket.RouteVioletGamesync(endpoint, programId,
                socketType, protocolType, customServer, Nextendo, 8463);
            Assert.That(routed.Port, Is.EqualTo(expectedPort));
            Assert.That(routed.Address, Is.EqualTo(endpoint.Address));
        }

        [Test]
        public void DisabledRoutePreservesOriginalPort()
        {
            IPEndPoint endpoint = new(Nextendo, 7575);
            Assert.That(ManagedSocket.RouteVioletGamesync(endpoint, 0x01008F6008C5E000UL,
                SocketType.Stream, ProtocolType.Tcp, false, Nextendo, 0).Port, Is.EqualTo(7575));
        }
    }
}
