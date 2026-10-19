using NUnit.Framework;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Impl;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Types;
using System.Net.Sockets;

namespace Ryujinx.Tests.HLE
{
    // [Nextendo] RecvMMsg et SendMMsg rendent un SocketError (code Winsock sur tous les systemes).
    // Sa conversion ne doit pas dependre du systeme : sous macOS, ConvertError attend l errno natif
    // et rendait 10035 tel quel au jeu, ce qui cassait la connexion NPLN (Splatoon 3, 2321-4992).
    public class WinSockHelperTests
    {
        [TestCase(SocketError.Success, (int)LinuxError.SUCCESS)]
        [TestCase(SocketError.WouldBlock, (int)LinuxError.EAGAIN)]
        [TestCase(SocketError.InProgress, (int)LinuxError.EINPROGRESS)]
        [TestCase(SocketError.ConnectionReset, (int)LinuxError.ECONNRESET)]
        [TestCase(SocketError.ConnectionAborted, (int)LinuxError.ECONNABORTED)]
        [TestCase(SocketError.NotConnected, (int)LinuxError.ENOTCONN)]
        [TestCase(SocketError.TimedOut, (int)LinuxError.ETIMEDOUT)]
        [TestCase(SocketError.Interrupted, (int)LinuxError.EINTR)]
        public void ConvertsSocketErrorOnEverySystem(SocketError socketError, int expected)
        {
            Assert.AreEqual(expected, (int)WinSockHelper.ConvertSocketError(socketError));
        }
    }
}
