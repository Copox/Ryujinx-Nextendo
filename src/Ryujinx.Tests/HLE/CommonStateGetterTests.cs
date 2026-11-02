using NUnit.Framework;
using Ryujinx.HLE.HOS;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Services;
using AmResultCode = Ryujinx.HLE.HOS.Services.Am.ResultCode;
using Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Ryujinx.Tests.HLE
{
    public class CommonStateGetterTests
    {
        private static MethodInfo Command(int id)
        {
            MethodInfo[] commands = typeof(ICommonStateGetter).GetMethods()
                .Where(method => method.GetCustomAttributes<CommandCmifAttribute>().Any(command => command.Id == id))
                .ToArray();
            Assert.That(commands.Length, Is.EqualTo(1), $"qlaunch must find one registered CMIF command {id}.");
            return commands[0];
        }

        [Test]
        public void Firmware21Command610IsRegistered()
        {
            Assert.That(Command(610).ReturnType, Is.EqualTo(typeof(AmResultCode)));
        }

        [TestCase(0UL)]
        [TestCase(0xBCAAAAAB00000000UL)]
        [TestCase(ulong.MaxValue)]
        public void Command610ConsumesOneUInt64AndReturnsNoPayload(ulong argument)
        {
            using MemoryStream input = new();
            using BinaryWriter inputWriter = new(input);
            inputWriter.Write(argument);
            inputWriter.Write(0x123456789ABCDEF0UL);
            input.Position = 0;
            using BinaryReader reader = new(input);
            using MemoryStream output = new();
            using BinaryWriter writer = new(output);
            ServiceCtx context = new(null, null, null, null, new IpcMessage(), new IpcMessage(), reader, writer);

            // This compatibility command is stateless and must not need a running console or account.
            ICommonStateGetter service = (ICommonStateGetter)RuntimeHelpers.GetUninitializedObject(typeof(ICommonStateGetter));
            object result = Command(610).Invoke(service, new object[] { context });

            Assert.That(result, Is.EqualTo(AmResultCode.Success));
            Assert.That(input.Position, Is.EqualTo(sizeof(ulong)));
            Assert.That(reader.ReadUInt64(), Is.EqualTo(0x123456789ABCDEF0UL));
            Assert.That(output.Length, Is.Zero);
            Assert.That(context.Response.ObjectIds, Is.Empty);
            Assert.That(context.Response.HandleDesc, Is.Null);
        }

        [Test]
        public void Firmware22Command611IsRegistered()
        {
            Assert.That(Command(611).ReturnType, Is.EqualTo(typeof(AmResultCode)));
        }

        [TestCase((byte)0)]
        [TestCase((byte)1)]
        [TestCase(byte.MaxValue)]
        public void Command611ConsumesOneByteAndReturnsNoPayload(byte argument)
        {
            using MemoryStream input = new(new byte[] { argument, 0x5a });
            using BinaryReader reader = new(input);
            using MemoryStream output = new();
            using BinaryWriter writer = new(output);
            ServiceCtx context = new(null, null, null, null, new IpcMessage(), new IpcMessage(), reader, writer);
            ICommonStateGetter service = (ICommonStateGetter)RuntimeHelpers.GetUninitializedObject(typeof(ICommonStateGetter));

            Assert.That(Command(611).Invoke(service, new object[] { context }), Is.EqualTo(AmResultCode.Success));
            Assert.That(input.Position, Is.EqualTo(1));
            Assert.That(reader.ReadByte(), Is.EqualTo(0x5a));
            Assert.That(output.Length, Is.Zero);
            Assert.That(context.Response.ObjectIds, Is.Empty);
            Assert.That(context.Response.HandleDesc, Is.Null);
        }
    }
}
