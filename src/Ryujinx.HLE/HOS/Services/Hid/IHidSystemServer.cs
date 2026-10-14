using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid.HidServer;
using Ryujinx.HLE.HOS.Services.Hid.Types;
using Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad;

namespace Ryujinx.HLE.HOS.Services.Hid
{
    [Service("hid:sys")]
    partial class IHidSystemServer : IpcService
    {
        public IHidSystemServer(ServiceCtx context) { }

        [CommandCmif(303)]
        // ApplyNpadSystemCommonPolicy(u64)
        public ResultCode ApplyNpadSystemCommonPolicy(ServiceCtx context)
        {
            ulong commonPolicy = context.RequestData.ReadUInt64();

            Logger.Stub?.PrintStub(LogClass.ServiceHid, new { commonPolicy });

            return ResultCode.Success;
        }

        [CommandCmif(306)]
        // GetLastActiveNpad() -> u32 NpadIdType
        public ResultCode GetLastActiveNpad(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The controller in use: the first one connected.
            NpadIdType active = NpadIdType.Player1;

            foreach (PlayerIndex index in new[] { PlayerIndex.Player1, PlayerIndex.Player2, PlayerIndex.Player3, PlayerIndex.Player4, PlayerIndex.Player5, PlayerIndex.Player6, PlayerIndex.Player7, PlayerIndex.Player8, PlayerIndex.Handheld })
            {
                if (context.Device.Hid.SharedMemory.Npads[(int)index].InternalState.StyleSet != 0)
                {
                    active = HidUtils.GetNpadIdTypeFromIndex(index);

                    break;
                }
            }

            context.ResponseData.Write((uint)active);

            return ResultCode.Success;
        }

        [CommandCmif(307)]
        // GetNpadSystemExtStyle() -> u64
        public ResultCode GetNpadSystemExtStyle(ServiceCtx context)
        {
            foreach (PlayerIndex playerIndex in context.Device.Hid.Npads.GetSupportedPlayers())
            {
                if (HidUtils.GetNpadIdTypeFromIndex(playerIndex) > NpadIdType.Handheld)
                {
                    return ResultCode.InvalidNpadIdType;
                }
            }

            context.ResponseData.Write((ulong)context.Device.Hid.Npads.SupportedStyleSets);

            return ResultCode.Success;
        }

        [CommandCmif(314)] // 9.0.0+
        // GetAppletFooterUiType(u32) -> u8
        public ResultCode GetAppletFooterUiType(ServiceCtx context)
        {
            ResultCode resultCode = GetAppletFooterUiTypeImpl(context, out AppletFooterUiType appletFooterUiType);

            context.ResponseData.Write((byte)appletFooterUiType);

            return resultCode;
        }

        private ResultCode GetAppletFooterUiTypeImpl(ServiceCtx context, out AppletFooterUiType appletFooterUiType)
        {
            NpadIdType npadIdType = (NpadIdType)context.RequestData.ReadUInt32();

            if (npadIdType > NpadIdType.Player8 && npadIdType != NpadIdType.Handheld)
            {
                appletFooterUiType = AppletFooterUiType.None;

                return ResultCode.InvalidNpadIdType;
            }

            PlayerIndex playerIndex = HidUtils.GetIndexFromNpadIdType(npadIdType);

            appletFooterUiType = context.Device.Hid.SharedMemory.Npads[(int)playerIndex].InternalState.AppletFooterUiType;

            return ResultCode.Success;
        }
        private Ryujinx.HLE.HOS.Services.StubEvent _joyDetachOnBluetoothOffEvent;

        [CommandCmif(751)]
        // AcquireJoyDetachOnBluetoothOffEventHandle() -> handle<copy>
        public ResultCode AcquireJoyDetachOnBluetoothOffEventHandle(ServiceCtx context)
        {
            _joyDetachOnBluetoothOffEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _joyDetachOnBluetoothOffEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The commands the HOME Menu asks about its controllers.
        // The state comes from the emulated controllers themselves.
        private static ref Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad.NpadInternalState NpadState(ServiceCtx context, uint npadId)
        {
            PlayerIndex index = HidUtils.GetIndexFromNpadIdType((NpadIdType)npadId);

            return ref context.Device.Hid.SharedMemory.Npads[(int)index].InternalState;
        }

        [CommandCmif(315)] // 9.0.0+
        // GetAppletDetailedUiType(u32 npad_id) -> nn::hid::system::AppletDetailedUiType: the picture
        // of the controller drawn by the Controllers page.
        public ResultCode GetAppletDetailedUiType(ServiceCtx context)
        {
            uint npadId = context.RequestData.ReadUInt32();
            NpadStyleTag style = NpadState(context, npadId).StyleSet;

            uint type = (NpadIdType)npadId == NpadIdType.Handheld && style != 0 ? 0x04000000u // Joy-Con on the console
                : (style & NpadStyleTag.FullKey) != 0 ? 0x0C000000u   // Pro Controller
                : (style & NpadStyleTag.JoyDual) != 0 ? 0x05000000u   // a pair of Joy-Con
                : (style & NpadStyleTag.JoyLeft) != 0 ? 0x08000000u   // one left Joy-Con, held sideways
                : (style & NpadStyleTag.JoyRight) != 0 ? 0x0A000000u  // one right Joy-Con, held sideways
                : 0u;

            context.ResponseData.Write(type);

            return ResultCode.Success;
        }

        [CommandCmif(316)] // 10.0.0+
        // GetNpadInterfaceType(u32 npad_id) -> u8 (1 Bluetooth, 2 Rail, 3 Usb, 4 Embedded)
        public ResultCode GetNpadInterfaceType(ServiceCtx context)
        {
            uint npadId = context.RequestData.ReadUInt32();

            context.ResponseData.Write((byte)((NpadIdType)npadId == NpadIdType.Handheld ? 2 : 1));

            return ResultCode.Success;
        }

        [CommandCmif(317)] // 10.0.0+
        // GetNpadLeftRightInterfaceType(u32 npad_id) -> u8, u8
        public ResultCode GetNpadLeftRightInterfaceType(ServiceCtx context)
        {
            uint npadId = context.RequestData.ReadUInt32();
            byte type = (byte)((NpadIdType)npadId == NpadIdType.Handheld ? 2 : 1);

            context.ResponseData.Write(type);
            context.ResponseData.Write(type);

            return ResultCode.Success;
        }

        [CommandCmif(318)] // 10.0.0+
        // HasBattery(u32 npad_id) -> bool: every wireless controller has one.
        public ResultCode HasBattery(ServiceCtx context)
        {
            uint npadId = context.RequestData.ReadUInt32();

            context.ResponseData.Write(NpadState(context, npadId).StyleSet != 0);

            return ResultCode.Success;
        }

        [CommandCmif(319)] // 10.0.0+
        // HasLeftRightBattery(u32 npad_id) -> bool, bool: the two Joy-Con of a pair.
        public ResultCode HasLeftRightBattery(ServiceCtx context)
        {
            uint npadId = context.RequestData.ReadUInt32();
            NpadStyleTag style = NpadState(context, npadId).StyleSet;
            bool pair = (style & (NpadStyleTag.JoyDual | NpadStyleTag.Handheld)) != 0;

            context.ResponseData.Write(pair || (style & NpadStyleTag.JoyLeft) != 0);
            context.ResponseData.Write(pair || (style & NpadStyleTag.JoyRight) != 0);

            return ResultCode.Success;
        }

        [CommandCmif(310)] // 6.0.0+
        // GetMaskedSupportedNpadStyleSet(u64 aruid) -> u32
        public ResultCode GetMaskedSupportedNpadStyleSet(ServiceCtx context)
        {
            context.RequestData.ReadUInt64();

            context.ResponseData.Write((uint)context.Device.Hid.Npads.SupportedStyleSets);

            return ResultCode.Success;
        }

        [CommandCmif(523)] // 9.0.0+
        // IsJoyConRailEnabled() -> bool
        public ResultCode IsJoyConRailEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }

        [CommandCmif(525)] // 11.0.0+
        // IsJoyConAttachedOnAllRail() -> bool: the Joy-Con are on the console in handheld mode.
        public ResultCode IsJoyConAttachedOnAllRail(ServiceCtx context)
        {

            context.ResponseData.Write(NpadState(context, (uint)NpadIdType.Handheld).StyleSet != 0);

            return ResultCode.Success;
        }

        private Ryujinx.HLE.HOS.Services.StubEvent _connectionTriggerTimeoutEvent;
        private Ryujinx.HLE.HOS.Services.StubEvent _deviceRegisteredEvent;

        [CommandCmif(544)] // 3.0.0+
        // AcquireConnectionTriggerTimeoutEvent() -> handle<copy>
        public ResultCode AcquireConnectionTriggerTimeoutEvent(ServiceCtx context)
        {
            _connectionTriggerTimeoutEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _connectionTriggerTimeoutEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(546)] // 3.0.0+
        // AcquireDeviceRegisteredEventForControllerSupport() -> handle<copy>
        public ResultCode AcquireDeviceRegisteredEventForControllerSupport(ServiceCtx context)
        {
            _deviceRegisteredEvent ??= new Ryujinx.HLE.HOS.Services.StubEvent(context);
            _deviceRegisteredEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(850)]
        // IsUsbFullKeyControllerEnabled() -> bool
        public ResultCode IsUsbFullKeyControllerEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
    }
}
