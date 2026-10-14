using LibHac;
using LibHac.Common;
using LibHac.Fs;
using LibHac.Fs.Fsa;
using LibHac.FsSystem;
using LibHac.Ncm;
using LibHac.Tools.FsSystem.NcaUtils;
using Ryujinx.Common;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.SystemState;
using System;
using System.IO;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Settings
{
    [Service("set:sys")]
    class ISystemSettingsServer : IpcService
    {
        // [Nextendo, HOME Menu work]
        [CommandCmif(201)] // 10.1.0+
        // GetFieldTestingFlag() -> bool
        public ResultCode GetFieldTestingFlag(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        public ISystemSettingsServer(ServiceCtx context) { }

        [CommandCmif(174)] // 20.0.0+
        // Asked by the virtual game cards page.
        public ResultCode Unknown174(ServiceCtx context)
        {
            uint value = 0;

            context.ResponseData.Write(value);

            return ResultCode.Success;
        }

        [CommandCmif(57)]
        // SetRegionCode(u32)
        public ResultCode SetRegionCode(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] "Region" of the HOME Menu settings: the emulator's.
            int region = context.RequestData.ReadInt32();

            if (region is >= 0 and <= 6)
            {
                context.Device.System.State.SetRegion((Ryujinx.HLE.HOS.SystemState.RegionCode)region);
                Ryujinx.Common.Configuration.NextendoSystemSettings.SetRegion(region);
            }

            return ResultCode.Success;
        }

        [CommandCmif(0)]
        // SetLanguageCode(nn::settings::LanguageCode)
        public ResultCode SetLanguageCode(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] "Language" of the HOME Menu settings: the language of the
            // emulator, kept in its settings (the HOME Menu then restarts in it).
            string code = System.Text.Encoding.ASCII.GetString(context.RequestData.ReadBytes(8)).TrimEnd('\0');
            int index = System.Array.IndexOf(Ryujinx.HLE.HOS.SystemState.SystemStateMgr.LanguageCodes, code);

            if (index < 0)
            {
                return ResultCode.Success;
            }

            context.Device.System.State.SetLanguage((Ryujinx.HLE.HOS.SystemState.SystemLanguage)index);
            Ryujinx.Common.Configuration.NextendoSystemSettings.SetLanguage(index);

            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.ServiceSet, $"[home] system language: {code}");

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetFirmwareVersion() -> buffer<nn::settings::system::FirmwareVersion, 0x1a, 0x100>
        public ResultCode GetFirmwareVersion(ServiceCtx context)
        {
            return GetFirmwareVersion2(context);
        }

        [CommandCmif(4)]
        // GetFirmwareVersion2() -> buffer<nn::settings::system::FirmwareVersion, 0x1a, 0x100>
        public ResultCode GetFirmwareVersion2(ServiceCtx context)
        {
            ulong replyPos = context.Request.RecvListBuff[0].Position;

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(0x100L);

            byte[] firmwareData = GetFirmwareData(context.Device);

            if (firmwareData != null)
            {
                context.Memory.Write(replyPos, firmwareData);

                return ResultCode.Success;
            }

            const byte MajorFwVersion = 0x03;
            const byte MinorFwVersion = 0x00;
            const byte MicroFwVersion = 0x00;
            const byte Unknown = 0x00; //Build?

            const int RevisionNumber = 0x0A;

            const string Platform = "NX";
            const string UnknownHex = "7fbde2b0bba4d14107bf836e4643043d9f6c8e47";
            const string Version = "3.0.0";
            const string Build = "NintendoSDK Firmware for NX 3.0.0-10.0";

            // http://switchbrew.org/index.php?title=System_Version_Title
            using MemoryStream ms = new(0x100);

            BinaryWriter writer = new(ms);

            writer.Write(MajorFwVersion);
            writer.Write(MinorFwVersion);
            writer.Write(MicroFwVersion);
            writer.Write(Unknown);

            writer.Write(RevisionNumber);

            writer.Write(Encoding.ASCII.GetBytes(Platform));

            ms.Seek(0x28, SeekOrigin.Begin);

            writer.Write(Encoding.ASCII.GetBytes(UnknownHex));

            ms.Seek(0x68, SeekOrigin.Begin);

            writer.Write(Encoding.ASCII.GetBytes(Version));

            ms.Seek(0x80, SeekOrigin.Begin);

            writer.Write(Encoding.ASCII.GetBytes(Build));

            context.Memory.Write(replyPos, ms.ToArray());

            return ResultCode.Success;
        }

        [CommandCmif(21)]
        // GetEulaVersions() -> (s32 count, buffer<nn::settings::system::EulaVersion, 6>)
        public ResultCode GetEulaVersions(ServiceCtx context)
        {
            // Without an accepted EULA for its region, the HOME Menu stops on the "Important
            // information" screen and only offers to restart. Report the agreement as accepted
            // for every region, at a version newer than any the firmware can ask for.
            ulong position = context.Request.ReceiveBuff[0].Position;
            ulong size = context.Request.ReceiveBuff[0].Size;

            const int EntrySize = 0x30;
            int count = 0;

            for (int region = 0; region < 7 && (ulong)((count + 1) * EntrySize) <= size; region++)
            {
                ulong entry = position + (ulong)(count * EntrySize);

                context.Memory.Write(entry + 0x00, 0x7FFF0000u); // Version
                context.Memory.Write(entry + 0x04, region);      // RegionCode
                context.Memory.Write(entry + 0x08, 1);           // ClockType: network system clock
                context.Memory.Write(entry + 0x0C, 0u);
                context.Memory.Write(entry + 0x10, 1700000000L); // NetworkSystemClock (posix time)
                context.Memory.Write(entry + 0x18, 0L);          // SteadyClockTimePoint.TimePoint
                context.Memory.Write(entry + 0x20, 0L);          // SteadyClockTimePoint.ClockSourceId
                context.Memory.Write(entry + 0x28, 0L);

                count++;
            }

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(23)]
        // GetColorSetId() -> i32
        public ResultCode GetColorSetId(ServiceCtx context)
        {
            context.ResponseData.Write((int)SavedColorSet(context));

            return ResultCode.Success;
        }

        // Read once per emulated system: a restart of the HOME Menu makes a new one, with the
        // default theme again.
        private static object _colorSetReadFor;

        // The theme the user chose last; with none chosen yet, Basic Black when a custom HOME Menu
        // theme is installed (those are made for it: on Basic White their text comes out dark on
        // a dark background).
        private static ColorSet SavedColorSet(ServiceCtx context)
        {
            if (!ReferenceEquals(_colorSetReadFor, context.Device.System))
            {
                _colorSetReadFor = context.Device.System;

                try
                {
                    string baseDir = Ryujinx.Common.Configuration.AppDataManager.BaseDirPath;
                    string saved = System.IO.Path.Combine(baseDir, "nextendo_color_set.txt");

                    // a theme chosen in the themes page is made for Basic Black (the page offers
                    // Basic White and Basic Black only as "no theme"): an older choice of Basic
                    // White would leave its text dark on a dark background
                    if (Ryujinx.HLE.HOS.Themes.NextendoThemes.Chosen != null && !Ryujinx.HLE.HOS.Themes.NextendoThemes.InstalledWins)
                    {
                        context.Device.System.State.ThemeColor = ColorSet.BasicBlack;
                    }
                    else if (System.IO.File.Exists(saved) && int.TryParse(System.IO.File.ReadAllText(saved).Trim(), out int id))
                    {
                        context.Device.System.State.ThemeColor = (ColorSet)id;
                    }
                    else if (Ryujinx.HLE.HOS.Themes.NextendoThemes.Chosen != null ||
                             System.IO.Directory.Exists(System.IO.Path.Combine(baseDir, "sdcard", "atmosphere", "contents", "0100000000001000", "romfs", "lyt")))
                    {
                        context.Device.System.State.ThemeColor = ColorSet.BasicBlack;
                    }
                }
                catch (System.Exception)
                {
                    // the default stays
                }
            }

            Ryujinx.HLE.HOS.Themes.QlaunchThemePage.SetLanguage(context.Device.System.State.DesiredSystemLanguage);

            return context.Device.System.State.ThemeColor;
        }

        [CommandCmif(24)]
        // GetColorSetId() -> i32
        public ResultCode SetColorSetId(ServiceCtx context)
        {
            int colorSetId = context.RequestData.ReadInt32();

            // [Nextendo, HOME Menu work] The "Ryujinx | Nextendo" section is the themes page: its
            // first two entries are Basic White and Basic Black, picking them is no theme.
            if (Ryujinx.HLE.HOS.Themes.QlaunchSettingsPage.Active)
            {
                Ryujinx.HLE.HOS.Themes.QlaunchThemePage.TakePick();

                return ResultCode.Success;
            }

            context.Device.System.State.ThemeColor = (ColorSet)colorSetId;

            // [Nextendo, HOME Menu themes] An installed theme picked on the themes page: made for
            // Basic Black.
            if (Ryujinx.HLE.HOS.Themes.QlaunchThemePage.TakePick() && Ryujinx.HLE.HOS.Themes.NextendoThemes.Chosen != null)
            {
                colorSetId = (int)ColorSet.BasicBlack;
                context.Device.System.State.ThemeColor = ColorSet.BasicBlack;
            }

            // [Nextendo, HOME Menu work] The theme chosen in the HOME Menu settings is kept.
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_color_set.txt"), colorSetId.ToString());
            }
            catch (System.Exception)
            {
                // kept for this session only
            }

            return ResultCode.Success;
        }

        [CommandCmif(37)]
        // GetSettingsItemValueSize(buffer<nn::settings::SettingsName, 0x19>, buffer<nn::settings::SettingsItemKey, 0x19>) -> u64
        public ResultCode GetSettingsItemValueSize(ServiceCtx context)
        {
            ulong classPos = context.Request.PtrBuff[0].Position;
            ulong classSize = context.Request.PtrBuff[0].Size;

            ulong namePos = context.Request.PtrBuff[1].Position;
            ulong nameSize = context.Request.PtrBuff[1].Size;

            byte[] classBuffer = new byte[classSize];

            context.Memory.Read(classPos, classBuffer);

            byte[] nameBuffer = new byte[nameSize];

            context.Memory.Read(namePos, nameBuffer);

            string askedSetting = Encoding.ASCII.GetString(classBuffer).Trim('\0') + "!" + Encoding.ASCII.GetString(nameBuffer).Trim('\0');

            NxSettings.Settings.TryGetValue(askedSetting, out object nxSetting);

            if (nxSetting != null)
            {
                ulong settingSize;

                if (nxSetting is string stringValue)
                {
                    settingSize = (ulong)stringValue.Length + 1;
                }
                else if (nxSetting is int)
                {
                    settingSize = sizeof(int);
                }
                else if (nxSetting is bool)
                {
                    settingSize = 1;
                }
                else
                {
                    throw new NotImplementedException(nxSetting.GetType().Name);
                }

                context.ResponseData.Write(settingSize);
            }

            return ResultCode.Success;
        }

        [CommandCmif(38)]
        // GetSettingsItemValue(buffer<nn::settings::SettingsName, 0x19, 0x48>, buffer<nn::settings::SettingsItemKey, 0x19, 0x48>) -> (u64, buffer<unknown, 6, 0>)
        public ResultCode GetSettingsItemValue(ServiceCtx context)
        {
            ulong classPos = context.Request.PtrBuff[0].Position;
            ulong classSize = context.Request.PtrBuff[0].Size;

            ulong namePos = context.Request.PtrBuff[1].Position;
            ulong nameSize = context.Request.PtrBuff[1].Size;

            ulong replyPos = context.Request.ReceiveBuff[0].Position;
            ulong replySize = context.Request.ReceiveBuff[0].Size;

            byte[] classBuffer = new byte[classSize];

            context.Memory.Read(classPos, classBuffer);

            byte[] nameBuffer = new byte[nameSize];

            context.Memory.Read(namePos, nameBuffer);

            string askedSetting = Encoding.ASCII.GetString(classBuffer).Trim('\0') + "!" + Encoding.ASCII.GetString(nameBuffer).Trim('\0');

            NxSettings.Settings.TryGetValue(askedSetting, out object nxSetting);

            if (nxSetting != null)
            {
                byte[] settingBuffer = new byte[replySize];

                if (nxSetting is string stringValue)
                {
                    if ((ulong)(stringValue.Length + 1) > replySize)
                    {
                        Logger.Error?.Print(LogClass.ServiceSet, $"{askedSetting} String value size is too big!");
                    }
                    else
                    {
                        settingBuffer = Encoding.ASCII.GetBytes(stringValue + "\0");
                    }
                }

                if (nxSetting is int intValue)
                {
                    settingBuffer = BitConverter.GetBytes(intValue);
                }
                else if (nxSetting is bool boolValue)
                {
                    settingBuffer[0] = boolValue ? (byte)1 : (byte)0;
                }
                else
                {
                    throw new NotImplementedException(nxSetting.GetType().Name);
                }

                context.Memory.Write(replyPos, settingBuffer);

                Logger.Debug?.Print(LogClass.ServiceSet, $"{askedSetting} set value: {nxSetting} as {nxSetting.GetType()}");
            }
            else
            {
                Logger.Error?.Print(LogClass.ServiceSet, $"{askedSetting} not found!");
            }

            return ResultCode.Success;
        }

        [CommandCmif(39)]
        // GetTvSettings() -> nn::settings::system::TvSettings
        public ResultCode GetTvSettings(ServiceCtx context)
        {
            context.ResponseData.Write(0u);   // Flags
            context.ResponseData.Write(0);    // TvResolution: automatic
            context.ResponseData.Write(0);    // HdmiContentType
            context.ResponseData.Write(0);    // RgbRange
            context.ResponseData.Write(0);    // CmuMode
            context.ResponseData.Write(0u);   // TvUnderscan
            context.ResponseData.Write(1.0f); // TvGamma
            context.ResponseData.Write(1.0f); // ContrastRatio

            return ResultCode.Success;
        }

        [CommandCmif(47)]
        // GetQuestFlag() -> bool
        public ResultCode GetQuestFlag(ServiceCtx context)
        {
            // Not a kiosk ("Quest") unit.
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(60)]
        // IsUserSystemClockAutomaticCorrectionEnabled() -> bool
        public ResultCode IsUserSystemClockAutomaticCorrectionEnabled(ServiceCtx context)
        {
            // NOTE: When set to true, is automatically synced with the internet.
            context.ResponseData.Write(true);

            Logger.Stub?.PrintStub(LogClass.ServiceSet);

            return ResultCode.Success;
        }

        [CommandCmif(62)]
        // GetDebugModeFlag() -> bool
        public ResultCode GetDebugModeFlag(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            Logger.Stub?.PrintStub(LogClass.ServiceSet);

            return ResultCode.Success;
        }

        [CommandCmif(67)]
        // GetBatteryLot() -> nn::settings::system::BatteryLot (0x18 characters)
        public ResultCode GetBatteryLot(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] Shown in the console information of the settings.
            byte[] lot = new byte[0x18];

            Encoding.ASCII.GetBytes("QLAUNCH BY KAZU").CopyTo(lot, 0);
            context.ResponseData.Write(lot);

            return ResultCode.Success;
        }

        [CommandCmif(68)]
        // GetSerialNumber() -> buffer<nn::settings::system::SerialNumber, 0x16>
        public ResultCode GetSerialNumber(ServiceCtx context)
        {
            context.ResponseData.Write(Encoding.ASCII.GetBytes(NextendoSerialNumber()));

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] "NEXT" and ten random digits, drawn once per installation
        // and kept with its data (the serial of a console does not change).
        private static string NextendoSerialNumber()
        {
            string path = System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_serial.txt");

            try
            {
                if (System.IO.File.Exists(path))
                {
                    string kept = System.IO.File.ReadAllText(path).Trim();

                    if (kept.Length == 14 && kept.StartsWith("NEXT", StringComparison.Ordinal))
                    {
                        return kept;
                    }
                }

                string serial = "NEXT" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000_000).ToString("D9") + System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10);

                System.IO.File.WriteAllText(path, serial);

                return serial;
            }
            catch (Exception)
            {
                return "NEXT0000000000";
            }
        }

        [CommandCmif(75)]
        // GetInitialLaunchSettings() -> nn::settings::system::InitialLaunchSettings
        public ResultCode GetInitialLaunchSettings(ServiceCtx context)
        {
            // Flags: bit 0 completion, bit 8 user addition, bit 16 timestamp. All set: the console
            // has been through its first-boot setup, so the HOME Menu goes straight to its menu.
            // None on the first launch: the HOME Menu starts the initial setup of the firmware.
            context.ResponseData.Write(InitialSetupDone ? 0x00010101u : 0u);
            context.ResponseData.Write(0u);          // Reserved
            context.ResponseData.Write(0L);          // SteadyClockTimePoint.TimePoint
            context.ResponseData.Write(0L);          // SteadyClockTimePoint.ClockSourceId (low)
            context.ResponseData.Write(0L);          // SteadyClockTimePoint.ClockSourceId (high)

            return ResultCode.Success;
        }

        [CommandCmif(76)]
        // SetInitialLaunchSettings(nn::settings::system::InitialLaunchSettings)
        public ResultCode SetInitialLaunchSettings(ServiceCtx context)
        {
            if ((context.RequestData.ReadUInt32() & 1) != 0)
            {
                MarkInitialSetupDone();
            }

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The initial setup of the console (the firmware's starter)
        // runs once, on data never used before: an update of the emulator (from the HOME Menu or
        // the emulator) changes the program, never the data, so it does not bring it back. Data
        // is "used" when it holds the mark of a finished setup, or signs of play from before this
        // run: a game save, a user other than the default RyuPlayer, a theme chosen in the HOME
        // Menu. Only opening the emulator (its configuration, the default user) does not count:
        // the firmware is installed from there before the HOME Menu is ever started. Decided once
        // per run: the setup itself writes some of those files halfway.
        // StarterReady turns it off for a build that must not have it.
        private static readonly bool StarterReady = true;

        private static bool? _initialSetupDone;

        private static string InitialSetupPath => System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "nextendo_initial_setup.txt");

        public static bool InitialSetupDone => _initialSetupDone ??= DecideInitialSetupDone();

        private static bool DecideInitialSetupDone()
        {
            if (System.IO.File.Exists(InitialSetupPath))
            {
                return true;
            }

            if (!StarterReady)
            {
                return true;
            }

            string baseDir = Ryujinx.Common.Configuration.AppDataManager.BaseDirPath;
            System.DateTime started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

            if (UsedBefore(baseDir, started))
            {
                // Used before: kept as set up, without asking again.
                MarkInitialSetupDone();

                return true;
            }

            return false;
        }

        private static bool UsedBefore(string baseDir, System.DateTime started)
        {
            bool Before(string path) => (System.IO.File.Exists(path) ? System.IO.File.GetCreationTimeUtc(path) : System.IO.Directory.GetCreationTimeUtc(path)) < started;

            try
            {
                // a theme chosen in the HOME Menu
                string colorSet = System.IO.Path.Combine(baseDir, "nextendo_color_set.txt");

                if (System.IO.File.Exists(colorSet) && Before(colorSet))
                {
                    return true;
                }

                // a game played (a setup run on its data could take the user who owns its save
                // away): the time played the emulator keeps for each game, not for the system
                // programs (the HOME Menu has its own)
                string games = System.IO.Path.Combine(baseDir, "games");

                if (System.IO.Directory.Exists(games))
                {
                    foreach (string game in System.IO.Directory.EnumerateDirectories(games))
                    {
                        string metadata = System.IO.Path.Combine(game, "gui", "metadata.json");

                        if (!ulong.TryParse(System.IO.Path.GetFileName(game), System.Globalization.NumberStyles.HexNumber, null, out ulong titleId) ||
                            titleId < 0x0100000000010000 || !System.IO.File.Exists(metadata) || !Before(metadata))
                        {
                            continue;
                        }

                        using System.Text.Json.JsonDocument played = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(metadata));

                        if ((played.RootElement.TryGetProperty("last_played_utc", out System.Text.Json.JsonElement last) && last.ValueKind == System.Text.Json.JsonValueKind.String) ||
                            (played.RootElement.TryGetProperty("last_played", out last) && last.ValueKind == System.Text.Json.JsonValueKind.String))
                        {
                            return true;
                        }
                    }
                }

                // a user made by the player
                string profiles = System.IO.Path.Combine(baseDir, "system", "Profiles.json");

                if (System.IO.File.Exists(profiles) && Before(profiles))
                {
                    using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(profiles));

                    if (document.RootElement.TryGetProperty("profiles", out System.Text.Json.JsonElement list) && list.ValueKind == System.Text.Json.JsonValueKind.Array &&
                        System.Linq.Enumerable.Any(list.EnumerateArray(), p => !(p.TryGetProperty("name", out System.Text.Json.JsonElement name) && name.GetString() == "RyuPlayer")))
                    {
                        return true;
                    }
                }
            }
            catch (System.Exception)
            {
                return true; // not readable: taken as used, never a setup over a player's data
            }

            return false;
        }

        public static void MarkInitialSetupDone()
        {
            _initialSetupDone = true;

            try
            {
                System.IO.File.WriteAllText(InitialSetupPath, System.DateTimeOffset.UtcNow.ToString("O"));
            }
            catch (System.Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[setup] not kept: {e.Message}");
            }

            Logger.Notice.Print(LogClass.ServiceSet, "[setup] initial setup of the console done");
        }

        [CommandCmif(77)]
        // GetDeviceNickName() -> buffer<nn::settings::system::DeviceNickName, 0x16>
        public ResultCode GetDeviceNickName(ServiceCtx context)
        {
            ulong deviceNickNameBufferPosition = context.Request.ReceiveBuff[0].Position;
            ulong deviceNickNameBufferSize = context.Request.ReceiveBuff[0].Size;

            if (deviceNickNameBufferPosition == 0)
            {
                return ResultCode.NullDeviceNicknameBuffer;
            }

            if (deviceNickNameBufferSize != 0x80)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, "Wrong buffer size");
            }

            context.Memory.Write(deviceNickNameBufferPosition, Encoding.ASCII.GetBytes(context.Device.System.State.DeviceNickName + '\0'));

            return ResultCode.Success;
        }

        [CommandCmif(78)]
        // SetDeviceNickName(buffer<nn::settings::system::DeviceNickName, 0x15>)
        public ResultCode SetDeviceNickName(ServiceCtx context)
        {
            ulong deviceNickNameBufferPosition = context.Request.SendBuff[0].Position;
            ulong deviceNickNameBufferSize = context.Request.SendBuff[0].Size;

            byte[] deviceNickNameBuffer = new byte[deviceNickNameBufferSize];

            context.Memory.Read(deviceNickNameBufferPosition, deviceNickNameBuffer);

            context.Device.System.State.DeviceNickName = Encoding.ASCII.GetString(deviceNickNameBuffer);

            return ResultCode.Success;
        }

        [CommandCmif(79)] // 4.0.0+
        // GetProductModel() -> s32
        public ResultCode GetProductModel(ServiceCtx context)
        {
            context.ResponseData.Write(1); // ProductModel_Nx, the original console

            return ResultCode.Success;
        }

        [CommandCmif(90)]
        // GetMiiAuthorId() -> nn::util::Uuid
        public ResultCode GetMiiAuthorId(ServiceCtx context)
        {
            // NOTE: If miiAuthorId is null ResultCode.NullMiiAuthorIdBuffer is returned.
            //       Doesn't occur in our case.

            context.ResponseData.Write(Mii.Helper.GetDeviceId());

            return ResultCode.Success;
        }

        public byte[] GetFirmwareData(Switch device)
        {
            const ulong SystemVersionTitleId = 0x0100000000000809;

            string contentPath = device.System.ContentManager.GetInstalledContentPath(SystemVersionTitleId, StorageId.BuiltInSystem, NcaContentType.Data);

            if (string.IsNullOrWhiteSpace(contentPath))
            {
                return null;
            }

            string firmwareTitlePath = FileSystem.VirtualFileSystem.SwitchPathToSystemPath(contentPath);

            using IStorage firmwareStorage = new LocalStorage(firmwareTitlePath, FileAccess.Read);
            Nca firmwareContent = new(device.System.KeySet, firmwareStorage);

            if (!firmwareContent.CanOpenSection(NcaSectionType.Data))
            {
                return null;
            }

            IFileSystem firmwareRomFs = firmwareContent.OpenFileSystem(NcaSectionType.Data, device.System.FsIntegrityCheckLevel);

            using UniqueRef<IFile> firmwareFile = new();

            Result result = firmwareRomFs.OpenFile(ref firmwareFile.Ref, "/file".ToU8Span(), OpenMode.Read);
            if (result.IsFailure())
            {
                return null;
            }

            result = firmwareFile.Get.GetSize(out long fileSize);
            if (result.IsFailure())
            {
                return null;
            }

            byte[] data = new byte[fileSize];

            result = firmwareFile.Get.Read(out _, 0, data);
            if (result.IsFailure())
            {
                return null;
            }

            return data;
        }
        [CommandCmif(126)] // 4.0.0+
        // GetAppletLaunchFlags() -> u32
        public ResultCode GetAppletLaunchFlags(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(127)] // 4.0.0+
        // SetAppletLaunchFlags(u32)
        public ResultCode SetAppletLaunchFlags(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServiceSet);

            return ResultCode.Success;
        }
        [CommandCmif(185)] // 13.1.0+
        // GetHomeMenuSchemeModel() -> u32
        public ResultCode GetHomeMenuSchemeModel(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }
        [CommandCmif(71)]
        // GetSleepSettings() -> nn::settings::system::SleepSettings
        public ResultCode GetSleepSettings(ServiceCtx context)
        {
            context.ResponseData.Write(0u); // Flags
            context.ResponseData.Write(0);  // HandheldSleepPlan
            context.ResponseData.Write(0);  // ConsoleSleepPlan

            return ResultCode.Success;
        }

        [CommandCmif(99)]
        // GetBatteryPercentageFlag() -> bool
        public ResultCode GetBatteryPercentageFlag(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }
        [CommandCmif(17)]
        // GetAccountSettings() -> nn::settings::system::AccountSettings
        public ResultCode GetAccountSettings(ServiceCtx context)
        {
            context.ResponseData.Write(0u);

            return ResultCode.Success;
        }

        [CommandCmif(29)]
        // GetNotificationSettings() -> nn::settings::system::NotificationSettings
        public ResultCode GetNotificationSettings(ServiceCtx context)
        {
            context.ResponseData.Write(0u);
            context.ResponseData.Write(0u);
            context.ResponseData.Write(0L);
            context.ResponseData.Write(0L);

            return ResultCode.Success;
        }

        [CommandCmif(31)]
        // GetAccountNotificationSettings() -> (s32 count, buffer<AccountNotificationSettings, 6>)
        public ResultCode GetAccountNotificationSettings(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] Each user gets the notifications of the console (the
            // overlay shows none for a user without settings): friends online and friend requests,
            // for all friends. 0x18 bytes each: uid, flags (bit 0 online, bit 1 requests), then the
            // presence and invitation overlay permissions (3 = friends).
            ulong position = context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Position : 0;
            ulong room = context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0;
            int count = 0;

            foreach (Account.Acc.UserProfile profile in context.Device.System.AccountManager.GetAllUsers())
            {
                if ((ulong)(count + 1) * 0x18 > room)
                {
                    break;
                }

                byte[] entry = new byte[0x18];

                BitConverter.TryWriteBytes(entry.AsSpan(0, 8), profile.UserId.High);
                BitConverter.TryWriteBytes(entry.AsSpan(8, 8), profile.UserId.Low);
                BitConverter.TryWriteBytes(entry.AsSpan(0x10, 4), 3u);
                entry[0x14] = 3;
                entry[0x15] = 3;

                context.Memory.Write(position + (ulong)count * 0x18, entry);
                count++;
            }

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(63)]
        // GetPrimaryAlbumStorage() -> s32
        public ResultCode GetPrimaryAlbumStorage(ServiceCtx context)
        {
            context.ResponseData.Write(1 /* SdCard */);

            return ResultCode.Success;
        }

        [CommandCmif(95)]
        // GetAutoUpdateEnableFlag() -> bool
        public ResultCode GetAutoUpdateEnableFlag(ServiceCtx context)
        {
            context.ResponseData.Write(false);

            return ResultCode.Success;
        }

        [CommandCmif(124)] // 4.0.0+
        // GetErrorReportSharePermission() -> s32
        public ResultCode GetErrorReportSharePermission(ServiceCtx context)
        {
            context.ResponseData.Write(2 /* Denied */);

            return ResultCode.Success;
        }

        [CommandCmif(136)] // 4.0.0+
        // GetKeyboardLayout() -> s32
        public ResultCode GetKeyboardLayout(ServiceCtx context)
        {
            context.ResponseData.Write(0);

            return ResultCode.Success;
        }

        [CommandCmif(170)] // 7.0.0+
        // GetChineseTraditionalInputMethod() -> s32
        public ResultCode GetChineseTraditionalInputMethod(ServiceCtx context)
        {
            context.ResponseData.Write(1);

            return ResultCode.Success;
        }
    }
}
