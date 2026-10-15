using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.HLE.HOS.Services.Account.Acc.AsyncContext;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    // nn::account::nas::IAuthorizationRequest: Nintendo Account OAuth for a third-party service
    // (Plants vs. Zombies: Battle for Neighborville signs in to EA with it).
    class IAuthorizationRequest : IpcService
    {
        private const int StateOffset = 0x100;
        private const int StateSize = 0x80;

        private readonly ManagerServer _managerServer;
        private readonly ulong _parametersPosition;
        private readonly byte[] _state = new byte[StateSize];
        private readonly byte[] _code;

        public IAuthorizationRequest(ManagerServer managerServer, byte[] parameters, ulong parametersPosition)
        {
            _managerServer = managerServer;
            _parametersPosition = parametersPosition;

            if (parameters.Length >= StateOffset + StateSize)
            {
                Array.Copy(parameters, StateOffset, _state, 0, StateSize);

                int scopeEnd = Array.IndexOf(parameters, (byte)0, 0, StateOffset);
                string scope = Encoding.ASCII.GetString(parameters, 0, scopeEnd < 0 ? StateOffset : scopeEnd);

                Logger.Info?.Print(LogClass.ServiceAcc, $"[Nextendo] Nintendo Account authorization requested, scope='{scope}'");
            }

            _code = Encoding.ASCII.GetBytes("nextendo-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant());
        }

        [CommandCmif(0)]
        // GetSessionId() -> nn::account::detail::Uuid
        public ResultCode GetSessionId(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x10]);

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // InvokeWithoutInteractionAsync() -> object<nn::account::detail::IAsyncContext>
        public ResultCode InvokeWithoutInteractionAsync(ServiceCtx context)
        {
            KEvent asyncEvent = new(context.Device.System.KernelContext);
            AsyncExecution asyncExecution = new(asyncEvent);

            asyncExecution.Initialize(1000, _ => Task.CompletedTask);

            MakeObject(context, new IAsyncContext(asyncExecution));

            return ResultCode.Success;
        }

        [CommandCmif(19)]
        // IsAuthorized() -> bool
        public ResultCode IsAuthorized(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(20)]
        // GetAuthorizationCode() -> (u32 size, buffer<bytes, 6>)
        public ResultCode GetAuthorizationCode(ServiceCtx context)
        {
            return WriteString(context, _code);
        }

        [CommandCmif(21)]
        // GetIdToken() -> (u32 size, buffer<bytes, 6>)
        public ResultCode GetIdToken(ServiceCtx context)
        {
            return WriteString(context, _managerServer.GetIdToken(context));
        }

        [CommandCmif(22)]
        // GetState() -> buffer<nn::account::nas::State, 0x1a>
        public ResultCode GetState(ServiceCtx context)
        {
            // The service echoes the caller's state; read it live in case the caller rewrote it.
            if (_parametersPosition != 0)
            {
                context.Memory.Read(_parametersPosition + StateOffset, _state);
            }

            (ulong position, ulong size) = context.Request.GetBufferType0x22();

            if (position != 0)
            {
                context.Memory.Write(position, _state.AsSpan(0, (int)Math.Min(size, (ulong)StateSize)));
            }

            return ResultCode.Success;
        }

        private static ResultCode WriteString(ServiceCtx context, byte[] value)
        {
            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong size = Math.Min((ulong)value.Length, context.Request.ReceiveBuff[0].Size);

            context.Memory.Write(position, value.AsSpan(0, (int)size));
            context.ResponseData.Write((uint)size);

            return ResultCode.Success;
        }
    }
}
