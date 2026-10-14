using Ryujinx.Common.Logging;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ryujinx.Horizon.Sdk.Sf.Cmif
{
    abstract class ServiceDispatchTableBase
    {
        private const uint MaxCmifVersion = 1;

        private static readonly HipcCommandProcessor _stubProcessor = new([new CommandArg(CommandArgType.OutArgument, 0x10, 8)]);

        public abstract Result ProcessMessage(ref ServiceDispatchContext context, ReadOnlySpan<byte> inRawData);

        protected static Result ProcessMessageImpl(ref ServiceDispatchContext context, ReadOnlySpan<byte> inRawData, IReadOnlyDictionary<int, CommandHandler> entries, string objectName)
        {
            if (inRawData.Length < Unsafe.SizeOf<CmifInHeader>())
            {
                Logger.Warning?.Print(LogClass.KernelIpc, $"Request message size 0x{inRawData.Length:X} is invalid");

                return SfResult.InvalidHeaderSize;
            }

            CmifInHeader inHeader = MemoryMarshal.Cast<byte, CmifInHeader>(inRawData)[0];

            if (inHeader.Magic != CmifMessage.CmifInHeaderMagic || inHeader.Version > MaxCmifVersion)
            {
                Logger.Warning?.Print(LogClass.KernelIpc, $"Request message header magic value 0x{inHeader.Magic:X} is invalid");

                return SfResult.InvalidInHeader;
            }

            ReadOnlySpan<byte> inMessageRawData = inRawData[Unsafe.SizeOf<CmifInHeader>()..];
            uint commandId = inHeader.CommandId;

            Span<CmifOutHeader> outHeader = Span<CmifOutHeader>.Empty;

            if (!entries.TryGetValue((int)commandId, out CommandHandler commandHandler))
            {
                if (HorizonStatic.Options.IgnoreMissingServices)
                {
                    // If ignore missing services is enabled, just pretend that everything is fine.
                    PrepareForStubReply(ref context, out Span<byte> outRawData);
                    CommandHandler.GetCmifOutHeaderPointer(ref outHeader, ref outRawData);
                    outRawData.Clear();
                    outHeader[0] = new CmifOutHeader { Magic = CmifMessage.CmifOutHeaderMagic, Result = Result.Success };

                    Logger.Warning?.Print(LogClass.Service, $"Missing service {objectName} (command ID: {commandId}) ignored [in={inMessageRawData.Length}] raw={Convert.ToHexString(inMessageRawData[..Math.Min(48, inMessageRawData.Length)])} {DescribeBuffers(ref context)}");

                    return Result.Success;
                }
                else if (HorizonStatic.Options.ThrowOnInvalidCommandIds)
                {
                    throw new NotImplementedException($"{objectName} command ID: {commandId} is not implemented");
                }

                return SfResult.UnknownCommandId;
            }

            Logger.Trace?.Print(LogClass.KernelIpc, $"{objectName}.{commandHandler.MethodName} called");

            long asked = System.Diagnostics.Stopwatch.GetTimestamp();

            Result commandResult = commandHandler.Invoke(ref outHeader, ref context, inMessageRawData);

            // [Nextendo] See IpcService: a slow answer is a freeze on screen.
            if (System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalMilliseconds >= 120)
            {
                Logger.Notice.Print(LogClass.KernelIpc, $"[slow] {objectName} {commandId}: {System.Diagnostics.Stopwatch.GetElapsedTime(asked).TotalMilliseconds:F0} ms");
            }

            if (commandResult.Module is SfResult.ModuleId or
                HipcResult.ModuleId)
            {
                Logger.Warning?.Print(LogClass.KernelIpc, $"{commandHandler.MethodName} returned error {commandResult}");
            }

            if (SfResult.RequestContextChanged(commandResult))
            {
                return commandResult;
            }

            if (outHeader.IsEmpty)
            {
                commandResult.AbortOnSuccess();

                return commandResult;
            }

            outHeader[0] = new CmifOutHeader { Magic = CmifMessage.CmifOutHeaderMagic, Result = commandResult };

            return Result.Success;
        }

        // What buffers an unknown request carries: they say what the command returns.
        private static string DescribeBuffers(scoped ref ServiceDispatchContext context)
        {
            HipcMetadata meta = context.Request.Meta;
            HipcMessageData data = context.Request.Data;
            System.Text.StringBuilder text = new();

            text.Append($"statics={meta.SendStaticsCount} in={meta.SendBuffersCount} out={meta.ReceiveBuffersCount} recvlist={meta.ReceiveStaticsCount} pid={meta.SendPid}");

            for (int i = 0; i < meta.SendStaticsCount && i < data.SendStatics.Length; i++)
            {
                text.Append($" static[{i}]=0x{data.SendStatics[i].Size:X}");
            }

            for (int i = 0; i < meta.ReceiveBuffersCount && i < data.ReceiveBuffers.Length; i++)
            {
                text.Append($" out[{i}]=0x{data.ReceiveBuffers[i].Size:X}");
            }

            for (int i = 0; i < data.ReceiveList.Length && i < 4; i++)
            {
                text.Append($" recv[{i}]={System.Convert.ToHexString(System.Runtime.InteropServices.MemoryMarshal.AsBytes(data.ReceiveList.Slice(i, 1)))}");
            }

            return text.ToString();
        }

        private static void PrepareForStubReply(scoped ref ServiceDispatchContext context, out Span<byte> outRawData)
        {
            // Through a domain the reply carries a domain header in front of the CMIF one: let
            // the session's processor lay the message out, as it does for a real command. The
            // stand-in command returns 0x10 bytes of zeroes.
            if (context.Processor != null)
            {
                context.Processor.SetImplementationProcessor(_stubProcessor);
                context.Processor.PrepareForReply(ref context, out outRawData, context.Processor.GetRuntimeMetadata());

                return;
            }

            HipcMessageData response = HipcMessage.WriteResponse(context.OutMessageBuffer, 0, 0x20 / sizeof(uint), 0, 0);
            outRawData = MemoryMarshal.Cast<uint, byte>(response.DataWords);
        }
    }
}
