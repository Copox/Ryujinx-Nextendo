using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Services.Am.AppletAE.AllSystemAppletProxiesService.SystemAppletProxy
{
    // The HOME Menu's handle on a game it starts: created for a title of the library, started,
    // brought back to the foreground, told to exit.
    class IApplicationAccessor : IpcService
    {
        private readonly ulong _applicationId;
        private readonly ulong _callerPid;
        private readonly KEvent _stateChangedEvent;

        private int _stateChangedEventHandle;
        private RunningApplication _application;

        private readonly bool _systemProgram;

        public IApplicationAccessor(ServiceCtx context, ulong applicationId, ulong callerPid, bool systemProgram = false)
        {
            _applicationId = applicationId;
            _systemProgram = systemProgram;
            _callerPid = callerPid;
            _stateChangedEvent = new KEvent(context.Device.System.KernelContext);
        }

        [CommandCmif(0)]
        // GetAppletStateChangedEvent() -> handle<copy>
        public ResultCode GetAppletStateChangedEvent(ServiceCtx context)
        {
            if (_stateChangedEventHandle == 0)
            {
                if (context.Process.HandleTable.GenerateHandle(_stateChangedEvent.ReadableEvent, out _stateChangedEventHandle) != Result.Success)
                {
                    throw new InvalidOperationException("Out of handles!");
                }
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(_stateChangedEventHandle);

            return ResultCode.Success;
        }

        [CommandCmif(1)]
        // IsCompleted() -> bool
        public ResultCode IsCompleted(ServiceCtx context)
        {
            context.ResponseData.Write(_application != null && _application.Exited);

            return ResultCode.Success;
        }

        [CommandCmif(10)]
        // Start()
        public ResultCode Start(ServiceCtx context)
        {
            HomeMenuTitle title = _systemProgram ? RealAppletManager.SystemProgram(context.Device, _applicationId) : HomeMenuLibrary.Find(_applicationId);

            if (title == null)
            {
                Logger.Error?.Print(LogClass.ServiceAm, $"HOME Menu: title {_applicationId:X16} is not in the library.");

                return ResultCode.NotAvailable;
            }

            _application = RealAppletManager.StartApplication(context.Device, title, _callerPid, () => _stateChangedEvent.ReadableEvent.Signal());

            return ResultCode.Success;
        }

        [CommandCmif(20)]
        // RequestExit()
        public ResultCode RequestExit(ServiceCtx context)
        {
            if (_application != null)
            {
                RealAppletManager.RequestApplicationExit(context.Device, _application);
            }

            return ResultCode.Success;
        }

        [CommandCmif(25)]
        // Terminate()
        public ResultCode Terminate(ServiceCtx context)
        {
            if (_application != null)
            {
                _application.TerminateRequested = true;
            }

            return ResultCode.Success;
        }

        [CommandCmif(30)]
        // GetResult()
        public ResultCode GetResult(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // RequestForApplicationToGetForeground()
        public ResultCode RequestForApplicationToGetForeground(ServiceCtx context)
        {
            if (_application != null)
            {
                RealAppletManager.BringApplicationToForeground(context.Device, _application);
            }

            return ResultCode.Success;
        }

        [CommandCmif(110)]
        // TerminateAllLibraryApplets()
        public ResultCode TerminateAllLibraryApplets(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(111)]
        // AreAnyLibraryAppletsLeft() -> bool
        public ResultCode AreAnyLibraryAppletsLeft(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(120)]
        // GetApplicationId() -> nn::ncm::ApplicationId
        public ResultCode GetApplicationId(ServiceCtx context)
        {
            context.ResponseData.Write(_applicationId);

            return ResultCode.Success;
        }

        [CommandCmif(121)]
        // PushLaunchParameter(u32 kind, object<nn::am::service::IStorage>)
        public ResultCode PushLaunchParameter(ServiceCtx context)
        {
            // The emulator builds the launch parameters of an application itself (preselected
            // user): what the HOME Menu pushes says the same thing.
            return ResultCode.Success;
        }

        [CommandCmif(122)]
        // GetApplicationControlProperty() -> buffer<nn::ns::ApplicationControlProperty, 6>
        public ResultCode GetApplicationControlProperty(ServiceCtx context)
        {
            byte[] nacp = new byte[0x4000];
            byte[] known = HomeMenuLibrary.Find(_applicationId)?.Nacp;

            if (known != null)
            {
                Array.Copy(known, nacp, Math.Min(known.Length, nacp.Length));
            }

            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong size = Math.Min(context.Request.ReceiveBuff[0].Size, (ulong)nacp.Length);

            context.Memory.Write(position, nacp.AsSpan(0, (int)size));

            return ResultCode.Success;
        }

        [CommandCmif(123)]
        // GetApplicationLaunchProperty() -> nn::arp::ApplicationLaunchProperty
        public ResultCode GetApplicationLaunchProperty(ServiceCtx context)
        {
            context.ResponseData.Write(_applicationId);
            context.ResponseData.Write(0u);       // version
            context.ResponseData.Write((byte)4);  // base game storage: built-in user
            context.ResponseData.Write((byte)0);  // update storage: none
            context.ResponseData.Write((ushort)0);

            return ResultCode.Success;
        }

        [CommandCmif(130)]
        // SetUsers(bool allUsers, buffer<nn::account::Uid, 5>)
        public ResultCode SetUsers(ServiceCtx context)
        {
            // [Nextendo] who the HOME Menu starts the application for (study)
            string users = string.Join(",", System.Linq.Enumerable.Select(context.Request.SendBuff, d => System.Convert.ToHexString(context.Memory.GetSpan(d.Position, (int)System.Math.Min(d.Size, 0x80)))));
            Ryujinx.Common.Logging.Logger.Notice.Print(Ryujinx.Common.Logging.LogClass.ServiceAm, $"[home] SetUsers {System.Convert.ToHexString(((System.IO.MemoryStream)context.RequestData.BaseStream).ToArray())} [{users}]");

            return ResultCode.Success;
        }

        [CommandCmif(131)]
        // CheckRightsEnvironmentAvailable() -> bool
        public ResultCode CheckRightsEnvironmentAvailable(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(132)]
        // GetNsRightsEnvironmentHandle() -> u64
        public ResultCode GetNsRightsEnvironmentHandle(ServiceCtx context)
        {
            context.ResponseData.Write(0UL);

            return ResultCode.Success;
        }

        [CommandCmif(140)]
        // GetDesirableUids() -> (s32 count, buffer<nn::account::Uid, 6>)
        public ResultCode GetDesirableUids(ServiceCtx context)
        {
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        [CommandCmif(150)]
        // ReportApplicationExitTimeout()
        public ResultCode ReportApplicationExitTimeout(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(160)]
        // SetApplicationAttribute(buffer<nn::am::service::ApplicationAttribute, 5>)
        public ResultCode SetApplicationAttribute(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(170)]
        // HasSaveDataAccessPermission(nn::ncm::ApplicationId) -> bool
        public ResultCode HasSaveDataAccessPermission(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }
    }
}
