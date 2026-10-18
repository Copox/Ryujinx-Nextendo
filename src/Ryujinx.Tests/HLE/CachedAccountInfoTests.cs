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
        public void CountryIsWrittenAtSdkOffset0x20()
        {
            // SDK : GetCountry = strnlen(this + 0x20, 4). Mario Kart 8 Deluxe en tire le drapeau.
            byte[] data = CachedAccountInfo.Create("fr");
            Assert.That(CachedAccountInfo.CountryOffset, Is.EqualTo(0x20));
            Assert.That(Encoding.ASCII.GetString(data, 0, 10), Is.EqualTo("2000-01-01"));
            Assert.That(data[0x20], Is.EqualTo((byte)'F'));
            Assert.That(data[0x21], Is.EqualTo((byte)'R'));
            Assert.That(data[0x22], Is.EqualTo(0));
            Assert.That(data.Where((b, i) => i >= 10 && i != 0x20 && i != 0x21).All(b => b == 0), Is.True);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("FRA")]
        [TestCase("F1")]
        [TestCase("É1")]
        public void InvalidCountryLeavesTheFieldEmpty(string country)
        {
            byte[] data = CachedAccountInfo.Create(country);
            Assert.That(data.Skip(10).All(b => b == 0), Is.True);
        }

        [Test]
        public void CountryIsReadFromTheSiteAccount()
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(
                "{\"account\":{\"name\":\"x\",\"country\":\"mx\"}}");
            Assert.That(Ryujinx.Common.Configuration.NextendoCountry.Parse(doc.RootElement), Is.EqualTo("MX"));

            using System.Text.Json.JsonDocument none = System.Text.Json.JsonDocument.Parse(
                "{\"account\":{\"country\":\"\"}}");
            Assert.That(Ryujinx.Common.Configuration.NextendoCountry.Parse(none.RootElement), Is.EqualTo(""));

            using System.Text.Json.JsonDocument bad = System.Text.Json.JsonDocument.Parse("{\"country\":\"FR\"}");
            Assert.That(Ryujinx.Common.Configuration.NextendoCountry.Parse(bad.RootElement), Is.EqualTo(""));
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
