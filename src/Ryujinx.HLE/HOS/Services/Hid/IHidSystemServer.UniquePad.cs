using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Hid.Types;
using Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Common;
using Ryujinx.HLE.HOS.Services.Hid.Types.SharedMemory.Npad;
using System;
using System.Collections.Generic;

namespace Ryujinx.HLE.HOS.Services.Hid
{
    // [Nextendo, HOME Menu work] The "unique pads": the physical controllers the settings of the
    // HOME Menu work on (button configuration, stick calibration). Each emulated controller of
    // Ryujinx (a keyboard or a real gamepad bound to a player) is one, its id the player + 1.
    partial class IHidSystemServer
    {
        private static readonly NpadIdType[] _padNpads =
        [
            NpadIdType.Player1, NpadIdType.Player2, NpadIdType.Player3, NpadIdType.Player4,
            NpadIdType.Player5, NpadIdType.Player6, NpadIdType.Player7, NpadIdType.Player8,
            NpadIdType.Handheld,
        ];

        private Ryujinx.HLE.HOS.Services.StubEvent _uniquePadConnectionEvent;

        private static List<ulong> UniquePads(ServiceCtx context)
        {
            List<ulong> ids = new();

            for (int i = 0; i < _padNpads.Length; i++)
            {
                if (NpadState(context, (uint)_padNpads[i]).StyleSet != 0)
                {
                    ids.Add((ulong)i + 1);
                }
            }

            return ids;
        }

        private static bool PadOf(ServiceCtx context, ulong id, out NpadIdType npad)
        {
            npad = default;

            if (id == 0 || id > (ulong)_padNpads.Length)
            {
                return false;
            }

            npad = _padNpads[id - 1];

            return NpadState(context, (uint)npad).StyleSet != 0;
        }

        private static void WriteIds(ServiceCtx context, List<ulong> ids)
        {
            int written = 0;

            if (context.Request.RecvListBuff.Count > 0)
            {
                ulong position = context.Request.RecvListBuff[0].Position;
                int room = (int)(context.Request.RecvListBuff[0].Size / 8);

                for (; written < ids.Count && written < room; written++)
                {
                    context.Memory.Write(position + (ulong)written * 8, ids[written]);
                }

                context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((ulong)written * 8);
            }

            context.ResponseData.Write((long)written);
        }

        [CommandCmif(321)] // 3.0.0+
        // GetUniquePadsFromNpad(u32 npad_id) -> (s64 count, buffer<UniquePadId, 0xa>)
        public ResultCode GetUniquePadsFromNpad(ServiceCtx context)
        {
            NpadIdType npad = (NpadIdType)context.RequestData.ReadUInt32();
            int index = Array.IndexOf(_padNpads, npad);

            WriteIds(context, index >= 0 && NpadState(context, (uint)npad).StyleSet != 0 ? [(ulong)index + 1] : []);

            return ResultCode.Success;
        }

        [CommandCmif(702)]
        // AcquireUniquePadConnectionEventHandle() -> handle<copy>
        public ResultCode AcquireUniquePadConnectionEventHandle(ServiceCtx context)
        {
            // Signalled once: the page reads the controllers already there.
            if (_uniquePadConnectionEvent == null)
            {
                _uniquePadConnectionEvent = new Ryujinx.HLE.HOS.Services.StubEvent(context);
                _uniquePadConnectionEvent.Signal();
            }

            _uniquePadConnectionEvent.ReplyWithHandle(context);

            return ResultCode.Success;
        }

        [CommandCmif(703)]
        // GetUniquePadIds() -> (s64 count, buffer<UniquePadId, 0xa>)
        public ResultCode GetUniquePadIds(ServiceCtx context)
        {
            WriteIds(context, UniquePads(context));

            return ResultCode.Success;
        }

        [CommandCmif(805)]
        // GetUniquePadBluetoothAddress(UniquePadId) -> nn::bluetooth::Address
        public ResultCode GetUniquePadBluetoothAddress(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();

            context.ResponseData.Write(new byte[] { 0x98, 0xB6, 0xE9, 0x00, 0x00, (byte)id });

            return PadOf(context, id, out _) ? ResultCode.Success : ResultCode.InvalidNpadIdType;
        }

        [CommandCmif(806)]
        // DisconnectUniquePad(UniquePadId)
        public ResultCode DisconnectUniquePad(ServiceCtx context) => ResultCode.Success;

        [CommandCmif(807)]
        // GetUniquePadType(UniquePadId) -> u64 (0 embedded, 1 full key, 2 right Joy-Con, 3 left Joy-Con)
        public ResultCode GetUniquePadType(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();

            if (!PadOf(context, id, out NpadIdType npad))
            {
                context.ResponseData.Write(0UL);

                return ResultCode.InvalidNpadIdType;
            }

            NpadStyleTag style = NpadState(context, (uint)npad).StyleSet;

            ulong type = npad == NpadIdType.Handheld ? 0UL
                : (style & NpadStyleTag.JoyRight) != 0 ? 2UL
                : (style & NpadStyleTag.JoyLeft) != 0 ? 3UL
                : 1UL;

            context.ResponseData.Write(type);

            return ResultCode.Success;
        }

        [CommandCmif(808)]
        // GetUniquePadInterface(UniquePadId) -> u64 (0 embedded, 1 rail, 2 Bluetooth, 3 USB)
        public ResultCode GetUniquePadInterface(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();
            bool known = PadOf(context, id, out NpadIdType npad);

            context.ResponseData.Write(npad == NpadIdType.Handheld ? 0UL : 3UL);

            return known ? ResultCode.Success : ResultCode.InvalidNpadIdType;
        }

        [CommandCmif(809)]
        // GetUniquePadSerialNumber(UniquePadId) -> nn::hid::system::UniquePadSerialNumber (0x10)
        public ResultCode GetUniquePadSerialNumber(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();
            byte[] serial = new byte[0x10];

            System.Text.Encoding.ASCII.GetBytes($"NEXTPAD{id:D7}").CopyTo(serial, 0);
            context.ResponseData.Write(serial);

            return PadOf(context, id, out _) ? ResultCode.Success : ResultCode.InvalidNpadIdType;
        }

        [CommandCmif(810)]
        // GetUniquePadControllerNumber(UniquePadId) -> u64 (the player number, 0 for the console)
        public ResultCode GetUniquePadControllerNumber(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();
            bool known = PadOf(context, id, out NpadIdType npad);

            context.ResponseData.Write(npad == NpadIdType.Handheld ? 0UL : id);

            return known ? ResultCode.Success : ResultCode.InvalidNpadIdType;
        }

        [CommandCmif(326)] // 10.0.0+
        // GetUniquePadAppletDetailedUiType(UniquePadId) -> nn::hid::system::AppletDetailedUiType
        public ResultCode GetUniquePadAppletDetailedUiType(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();
            uint type = 0;

            if (PadOf(context, id, out NpadIdType npad))
            {
                NpadStyleTag style = NpadState(context, (uint)npad).StyleSet;

                type = npad == NpadIdType.Handheld ? 0x04000000u
                    : (style & NpadStyleTag.FullKey) != 0 ? 0x0C000000u
                    : (style & NpadStyleTag.JoyDual) != 0 ? 0x05000000u
                    : (style & NpadStyleTag.JoyLeft) != 0 ? 0x08000000u
                    : 0x0A000000u;
            }

            context.ResponseData.Write(type);

            return ResultCode.Success;
        }

        [CommandCmif(1250)] // 10.0.0+
        // IsCustomButtonConfigSupported(UniquePadId) -> bool
        public ResultCode IsCustomButtonConfigSupported(ServiceCtx context)
        {
            ulong id = context.RequestData.ReadUInt64();

            context.ResponseData.Write(PadOf(context, id, out _));

            return ResultCode.Success;
        }

        // ---- Button mapping (nn::hid::system, numbers as in libnx) ----

        private static int PlayerOf(ulong id) => id is >= 1 and <= 9 ? (int)id - 1 : -1;

        private static byte[] ReadIn(ServiceCtx context, int size)
        {
            byte[] data = new byte[size];

            if (context.Request.SendBuff.Count > 0)
            {
                context.Memory.Read(context.Request.SendBuff[0].Position, data.AsSpan(0, (int)Math.Min((ulong)size, context.Request.SendBuff[0].Size)));
            }

            return data;
        }

        private static void WriteOut(ServiceCtx context, byte[] data)
        {
            if (context.Request.ReceiveBuff.Count > 0)
            {
                context.Memory.Write(context.Request.ReceiveBuff[0].Position, data.AsSpan(0, (int)Math.Min((ulong)data.Length, context.Request.ReceiveBuff[0].Size)));
            }
        }

        [CommandCmif(1252)]
        // IsDefaultButtonConfigFull(buffer<HidcfgButtonConfigFull, 0x15>) -> bool
        public ResultCode IsDefaultButtonConfigFull(ServiceCtx context)
        {
            context.ResponseData.Write(ReadIn(context, NextendoButtonConfig.FullSize).AsSpan().SequenceEqual(NextendoButtonConfig.Default()));

            return ResultCode.Success;
        }

        [CommandCmif(1256)]
        // IsButtonConfigStorageFullEmpty(s32 index) -> bool
        public ResultCode IsButtonConfigStorageFullEmpty(ServiceCtx context)
        {
            context.ResponseData.Write(NextendoButtonConfig.GetStorage(context.RequestData.ReadInt32()).Config == null);

            return ResultCode.Success;
        }

        [CommandCmif(1268)]
        // DeleteButtonConfigStorageFull(s32 index)
        public ResultCode DeleteButtonConfigStorageFull(ServiceCtx context)
        {
            NextendoButtonConfig.SetStorage(context.RequestData.ReadInt32(), null, null);

            return ResultCode.Success;
        }

        [CommandCmif(1271)]
        // IsUsingCustomButtonConfig(UniquePadId) -> bool
        public ResultCode IsUsingCustomButtonConfig(ServiceCtx context)
        {
            int player = PlayerOf(context.RequestData.ReadUInt64());

            context.ResponseData.Write(player >= 0 && NextendoButtonConfig.IsCustom(player));

            return ResultCode.Success;
        }

        [CommandCmif(1272)]
        // IsAnyCustomButtonConfigEnabled() -> bool
        public ResultCode IsAnyCustomButtonConfigEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(NextendoButtonConfig.AnyCustom());

            return ResultCode.Success;
        }

        [CommandCmif(1273)]
        // SetAllCustomButtonConfigEnabled(nn::applet::AppletResourceUserId, bool)
        public ResultCode SetAllCustomButtonConfigEnabled(ServiceCtx context)
        {
            context.RequestData.ReadUInt64();
            NextendoButtonConfig.SetEnabled(context.RequestData.ReadBoolean());

            return ResultCode.Success;
        }

        [CommandCmif(1274)]
        // SetDefaultButtonConfig(UniquePadId)
        public ResultCode SetDefaultButtonConfig(ServiceCtx context)
        {
            int player = PlayerOf(context.RequestData.ReadUInt64());

            if (player >= 0)
            {
                NextendoButtonConfig.Set(player, null);
            }

            return ResultCode.Success;
        }

        [CommandCmif(1275)]
        // SetAllDefaultButtonConfig()
        public ResultCode SetAllDefaultButtonConfig(ServiceCtx context)
        {
            NextendoButtonConfig.ResetAll();

            return ResultCode.Success;
        }

        [CommandCmif(1277)]
        // SetHidButtonConfigFull(UniquePadId, buffer<HidcfgButtonConfigFull, 0x15>)
        public ResultCode SetHidButtonConfigFull(ServiceCtx context)
        {
            int player = PlayerOf(context.RequestData.ReadUInt64());

            if (player >= 0)
            {
                NextendoButtonConfig.Set(player, ReadIn(context, NextendoButtonConfig.FullSize));
            }

            return ResultCode.Success;
        }

        [CommandCmif(1281)]
        // GetHidButtonConfigFull(UniquePadId) -> buffer<HidcfgButtonConfigFull, 0x16>
        public ResultCode GetHidButtonConfigFull(ServiceCtx context)
        {
            int player = PlayerOf(context.RequestData.ReadUInt64());

            WriteOut(context, player >= 0 ? NextendoButtonConfig.Get(player) : NextendoButtonConfig.Default());

            return ResultCode.Success;
        }

        [CommandCmif(1285)]
        // GetButtonConfigStorageFull(s32 index) -> (buffer<HidcfgButtonConfigFull, 0x16>, buffer<HidcfgStorageName, 0x1a>)
        public ResultCode GetButtonConfigStorageFull(ServiceCtx context)
        {
            (byte[] config, string name) = NextendoButtonConfig.GetStorage(context.RequestData.ReadInt32());

            WriteOut(context, config ?? NextendoButtonConfig.Default());

            if (context.Request.RecvListBuff.Count > 0)
            {
                byte[] text = new byte[Math.Min(0x81, (int)context.Request.RecvListBuff[0].Size)];

                System.Text.Encoding.UTF8.GetBytes(name ?? "").AsSpan(0, Math.Min(System.Text.Encoding.UTF8.GetByteCount(name ?? ""), text.Length - 1)).CopyTo(text);
                context.Memory.Write(context.Request.RecvListBuff[0].Position, text);
                context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((ulong)text.Length);
            }

            return ResultCode.Success;
        }

        [CommandCmif(1289)]
        // SetButtonConfigStorageFull(s32 index, buffer<HidcfgButtonConfigFull, 0x15>, buffer<HidcfgStorageName, 0x19>)
        public ResultCode SetButtonConfigStorageFull(ServiceCtx context)
        {
            int index = context.RequestData.ReadInt32();
            string name = "";

            if (context.Request.PtrBuff.Count > 0)
            {
                byte[] text = new byte[context.Request.PtrBuff[0].Size];

                context.Memory.Read(context.Request.PtrBuff[0].Position, text);
                name = System.Text.Encoding.UTF8.GetString(text).TrimEnd(' ');
            }

            NextendoButtonConfig.SetStorage(index, ReadIn(context, NextendoButtonConfig.FullSize), name);

            return ResultCode.Success;
        }

        // ---- Stick calibration: the stages follow the stick the player really moves ----

        private enum CalibrationStage : ulong
        {
            ReleaseFromRight = 0,
            ReleaseFromBottom = 1,
            ReleaseFromLeft = 2,
            ReleaseFromTop = 3,
            Rotate = 4,
            Update = 5,
            Completed = 6,
            Clear = 7,
            ClearCompleted = 8,
        }

        private sealed class Calibration
        {
            public CalibrationStage Stage;
            public bool Held;
            public int Sectors;
        }

        private static readonly Dictionary<(ulong, uint), Calibration> _calibrations = new();

        private const int StickMax = 0x7FFF;

        private static (ulong Id, uint Position) ReadStick(ServiceCtx context)
        {
            // The arguments: the stick (u32, padded) then the id, or the other way round; the id
            // is the one that names a controller.
            ulong a = context.RequestData.ReadUInt64();
            ulong b = context.RequestData.BaseStream.Length - context.RequestData.BaseStream.Position >= 8 ? context.RequestData.ReadUInt64() : 0;

            return a is >= 1 and <= 9 && b <= 1 ? (a, (uint)b) : (b, (uint)a);
        }

        private static AnalogStickState StickOf(ServiceCtx context, ulong id, uint position)
        {
            if (!PadOf(context, id, out NpadIdType npad))
            {
                return default;
            }

            ref NpadInternalState state = ref NpadState(context, (uint)npad);
            NpadStyleTag style = state.StyleSet;

            NpadCommonState common;

            if (npad == NpadIdType.Handheld)
            {
                common = state.Handheld.GetCurrentEntryRef();
            }
            else if ((style & NpadStyleTag.FullKey) != 0)
            {
                common = state.FullKey.GetCurrentEntryRef();
            }
            else if ((style & NpadStyleTag.JoyDual) != 0)
            {
                common = state.JoyDual.GetCurrentEntryRef();
            }
            else if ((style & NpadStyleTag.JoyLeft) != 0)
            {
                common = state.JoyLeft.GetCurrentEntryRef();
            }
            else
            {
                common = state.JoyRight.GetCurrentEntryRef();
            }

            return position == 0 ? common.AnalogStickL : common.AnalogStickR;
        }

        private static Calibration Advance(ServiceCtx context, ulong id, uint position)
        {
            lock (_calibrations)
            {
                if (!_calibrations.TryGetValue((id, position), out Calibration calibration))
                {
                    calibration = new Calibration { Stage = CalibrationStage.Completed };
                    _calibrations[(id, position)] = calibration;
                }

                AnalogStickState stick = StickOf(context, id, position);
                CalibrationStage before = calibration.Stage;

                double x = (double)stick.X / StickMax, y = (double)stick.Y / StickMax;
                double length = Math.Sqrt(x * x + y * y);
                bool released = length < 0.2;

                switch (calibration.Stage)
                {
                    case <= CalibrationStage.ReleaseFromTop:
                        bool toward = calibration.Stage switch
                        {
                            CalibrationStage.ReleaseFromRight => x > 0.7,
                            CalibrationStage.ReleaseFromBottom => y < -0.7,
                            CalibrationStage.ReleaseFromLeft => x < -0.7,
                            _ => y > 0.7,
                        };

                        // The stage moves on as soon as the stick reaches the edge (the page then
                        // changes the colour of the arrow and waits for the stick to be released).
                        if (toward && !calibration.Held)
                        {
                            calibration.Held = true;
                            calibration.Stage++;
                        }
                        else if (released)
                        {
                            calibration.Held = false;
                        }

                        break;
                    case CalibrationStage.Rotate:
                        if (length > 0.7)
                        {
                            int sector = (int)((Math.Atan2(y, x) + Math.PI) / (2 * Math.PI) * 8) & 7;

                            calibration.Sectors |= 1 << sector;
                        }

                        if (calibration.Sectors == 0xFF)
                        {
                            calibration.Stage = CalibrationStage.Update;
                        }

                        break;
                    case CalibrationStage.Update:
                        calibration.Stage = CalibrationStage.Completed;
                        break;
                    case CalibrationStage.Clear:
                        calibration.Stage = CalibrationStage.ClearCompleted;
                        break;
                }

                if (before != calibration.Stage)
                {
                    Logger.Info?.Print(LogClass.ServiceHid, $"[home] stick calibration of pad {id} ({position}): {before} -> {calibration.Stage}");
                }

                return calibration;
            }
        }

        [CommandCmif(821)]
        // StartAnalogStickManualCalibration(UniquePadId, AnalogStickPosition)
        public ResultCode StartAnalogStickManualCalibration(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);

            lock (_calibrations)
            {
                _calibrations[(id, position)] = new Calibration { Stage = CalibrationStage.ReleaseFromRight };
            }

            Logger.Info?.Print(LogClass.ServiceHid, $"[home] stick calibration started: pad {id}, stick {position}");

            return ResultCode.Success;
        }

        [CommandCmif(822)]
        // RetryCurrentAnalogStickManualCalibrationStage(UniquePadId, AnalogStickPosition)
        public ResultCode RetryCurrentAnalogStickManualCalibrationStage(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);

            lock (_calibrations)
            {
                if (_calibrations.TryGetValue((id, position), out Calibration calibration))
                {
                    calibration.Held = false;
                    calibration.Sectors = 0;
                }
            }

            return ResultCode.Success;
        }

        [CommandCmif(823)]
        // CancelAnalogStickManualCalibration(UniquePadId, AnalogStickPosition)
        public ResultCode CancelAnalogStickManualCalibration(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);

            lock (_calibrations)
            {
                _calibrations.Remove((id, position));
            }

            return ResultCode.Success;
        }

        [CommandCmif(824)]
        // ResetAnalogStickManualCalibration(UniquePadId, AnalogStickPosition): back to the factory values
        public ResultCode ResetAnalogStickManualCalibration(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);

            lock (_calibrations)
            {
                _calibrations[(id, position)] = new Calibration { Stage = CalibrationStage.Clear };
            }

            return ResultCode.Success;
        }

        [CommandCmif(825)]
        // GetAnalogStickState(UniquePadId, AnalogStickPosition) -> nn::hid::AnalogStickState
        public ResultCode GetAnalogStickState(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);
            AnalogStickState stick = StickOf(context, id, position);

            context.ResponseData.Write(stick.X);
            context.ResponseData.Write(stick.Y);

            return ResultCode.Success;
        }

        [CommandCmif(826)]
        // GetAnalogStickManualCalibrationStage(UniquePadId, AnalogStickPosition) -> u64
        public ResultCode GetAnalogStickManualCalibrationStage(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);

            context.ResponseData.Write((ulong)Advance(context, id, position).Stage);

            return ResultCode.Success;
        }

        [CommandCmif(827)]
        // IsAnalogStickButtonPressed(UniquePadId, AnalogStickPosition) -> bool
        public ResultCode IsAnalogStickButtonPressed(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);
            bool pressed = false;

            if (PadOf(context, id, out NpadIdType npad))
            {
                ref NpadInternalState state = ref NpadState(context, (uint)npad);
                NpadButton buttons = npad == NpadIdType.Handheld ? state.Handheld.GetCurrentEntryRef().Buttons : state.FullKey.GetCurrentEntryRef().Buttons;

                pressed = (buttons & (position == 0 ? NpadButton.StickL : NpadButton.StickR)) != 0;
            }

            context.ResponseData.Write(pressed);

            return ResultCode.Success;
        }

        [CommandCmif(828)]
        // IsAnalogStickInReleasePosition(UniquePadId, AnalogStickPosition) -> bool
        public ResultCode IsAnalogStickInReleasePosition(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);
            AnalogStickState stick = StickOf(context, id, position);

            context.ResponseData.Write(Math.Abs(stick.X) < StickMax / 5 && Math.Abs(stick.Y) < StickMax / 5);

            return ResultCode.Success;
        }

        [CommandCmif(829)]
        // IsAnalogStickInCircumference(UniquePadId, AnalogStickPosition) -> bool
        public ResultCode IsAnalogStickInCircumference(ServiceCtx context)
        {
            (ulong id, uint position) = ReadStick(context);
            AnalogStickState stick = StickOf(context, id, position);
            double length = Math.Sqrt((double)stick.X * stick.X + (double)stick.Y * stick.Y) / StickMax;

            context.ResponseData.Write(length > 0.7);

            return ResultCode.Success;
        }
    }
}
