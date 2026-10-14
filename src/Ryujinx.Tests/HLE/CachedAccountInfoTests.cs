using NUnit.Framework;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services.Account;
using Ryujinx.HLE.HOS.Services.Account.Acc.AccountService;
using System.IO;
using System.Linq;
using System.Text;

namespace Ryujinx.Tests.HLE
{
    public class CachedAccountInfoTests
    {
        [Test]
        public void BirthdayIsTerminatedAtSdkOffsetZero()
        {
            byte[] data = CachedAccountInfo.Create();
            Assert.That(data.Length, Is.EqualTo(0x68));
            Assert.That(Encoding.ASCII.GetString(data, 0, 10), Is.EqualTo("2000-01-01"));
            Assert.That(data.Skip(10).All(b => b == 0), Is.True);
        }

        [Test]
        public void MixedPointerAndResourceBuffersReturnUserBaseViaPointer()
        {
            IpcMessage request = new();
            IpcMessage response = new();
            // The received C descriptor is not dereferenceable client memory.
            request.RecvListBuff.Add(new IpcRecvListBuffDesc(0, 0));
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            writer.Write(0x800u);
            writer.Write(0x9000u);
            writer.Write(0u);
            stream.Position = 0;
            request.ReceiveBuff.Add(new IpcBuffDesc(new BinaryReader(stream)));
            Assert.That(CachedAccountInfo.PrepareResponse(request, response, 0x4000, 0xf000),
                Is.EqualTo(ResultCode.Success));
            Assert.That(response.PtrBuff.Count, Is.EqualTo(1));
            Assert.That(response.PtrBuff[0].Position, Is.EqualTo(0x4000));
            Assert.That(response.PtrBuff[0].Size, Is.EqualTo(0x68));
            Assert.That(request.ReceiveBuff[0].Position, Is.EqualTo(0x9000));
        }

        [Test]
        public void MissingPointerIsRejected()
        {
            Assert.That(CachedAccountInfo.PrepareResponse(new(), new(), 0x4000, 0xf000),
                Is.EqualTo(ResultCode.InvalidBufferSize));
        }

        [Test]
        public void SmallServerScratchBufferIsRejected()
        {
            IpcMessage request = new();
            request.RecvListBuff.Add(new IpcRecvListBuffDesc(0, 0));
            Assert.That(CachedAccountInfo.PrepareResponse(request, new(), 0x4000, 0x67),
                Is.EqualTo(ResultCode.InvalidBufferSize));
        }
    }
}
