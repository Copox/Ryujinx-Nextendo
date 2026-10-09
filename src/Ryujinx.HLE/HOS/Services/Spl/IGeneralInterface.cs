using Ryujinx.HLE.FileSystem;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Services.Spl.Types;

namespace Ryujinx.HLE.HOS.Services.Spl
{
    [Service("spl:")]
    [Service("spl:es")]
    [Service("spl:fs")]
    [Service("spl:manu")]
    [Service("spl:mig")]
    [Service("spl:ssl")]
    class IGeneralInterface : IpcService
    {
        public IGeneralInterface(ServiceCtx context) { }

        // [Nextendo, HOME Menu work] The part of the crypto service homebrew uses to read system
        // content themselves (NXThemes Installer reads the HOME Menu this way to learn its
        // version and to extract its layouts). A console derives the NCA header key from public
        // sources through these two calls; the emulator already holds that key.
        private static readonly byte[] _headerKekSource =
        [
            0x1F, 0x12, 0x91, 0x3A, 0x4A, 0xCB, 0xF0, 0x0D, 0x4C, 0xDE, 0x3A, 0xF6, 0xD5, 0x23, 0x88, 0x2A,
        ];

        private static readonly byte[] _headerKeySource =
        [
            0x5A, 0x3E, 0xD8, 0x4F, 0xDE, 0xC0, 0xD8, 0x26, 0x31, 0xF7, 0xE2, 0x5D, 0x19, 0x7B, 0xF5, 0xD0,
            0x1C, 0x9B, 0x7B, 0xFA, 0xF6, 0x28, 0x18, 0x3D, 0x71, 0xF6, 0x4D, 0x73, 0xF1, 0x50, 0xB9, 0xD2,
        ];

        // An access key stands for (key source, generation): handed out by GenerateAesKek, read
        // back by GenerateAesKey.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.UInt128, (byte[] Source, int Generation)> _accessKeys = new();

        [CommandCmif(2)]
        // GenerateAesKek(bytes<0x10> key_source, u32 generation, u32 option) -> bytes<0x10> access_key
        public ResultCode GenerateAesKek(ServiceCtx context)
        {
            byte[] source = context.RequestData.ReadBytes(0x10);
            int generation = context.RequestData.ReadInt32();

            byte[] access = new byte[0x10];

            System.Random.Shared.NextBytes(access);

            _accessKeys[System.Runtime.InteropServices.MemoryMarshal.Read<System.UInt128>(access)] = (source, generation);

            context.ResponseData.Write(access);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // GenerateAesKey(bytes<0x10> access_key, bytes<0x10> key_source) -> bytes<0x10> key
        public ResultCode GenerateAesKey(ServiceCtx context)
        {
            byte[] access = context.RequestData.ReadBytes(0x10);
            byte[] source = context.RequestData.ReadBytes(0x10);
            byte[] key = new byte[0x10];

            LibHac.Common.Keys.KeySet keys = context.Device.System.KeySet;

            if (_accessKeys.TryGetValue(System.Runtime.InteropServices.MemoryMarshal.Read<System.UInt128>(access), out var origin))
            {
                System.ReadOnlySpan<byte> kekSource = origin.Source;

                if (System.MemoryExtensions.SequenceEqual(kekSource, System.MemoryExtensions.AsSpan(_headerKekSource)))
                {
                    // The NCA header key, in two halves.
                    System.ReadOnlySpan<byte> header = System.Runtime.InteropServices.MemoryMarshal.AsBytes(
                        System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref keys.HeaderKey, 1));

                    if (System.MemoryExtensions.SequenceEqual(System.MemoryExtensions.AsSpan(source), System.MemoryExtensions.AsSpan(_headerKeySource, 0, 0x10)))
                    {
                        header[..0x10].CopyTo(key);
                    }
                    else if (System.MemoryExtensions.SequenceEqual(System.MemoryExtensions.AsSpan(source), System.MemoryExtensions.AsSpan(_headerKeySource, 0x10, 0x10)))
                    {
                        header.Slice(0x10, 0x10).CopyTo(key);
                    }
                }
                else
                {
                    // A key area key: the content key of an NCA, decrypted with the key area key
                    // of its kind (application, ocean, system) and generation.
                    int kind = -1;

                    if (System.MemoryExtensions.SequenceEqual(kekSource, keys.KeyAreaKeyApplicationSource.DataRo)) kind = 0;
                    else if (System.MemoryExtensions.SequenceEqual(kekSource, keys.KeyAreaKeyOceanSource.DataRo)) kind = 1;
                    else if (System.MemoryExtensions.SequenceEqual(kekSource, keys.KeyAreaKeySystemSource.DataRo)) kind = 2;

                    // spl counts key generations from 1 (0 and 1 both meaning the first master key).
                    int revision = System.Math.Max(0, origin.Generation - 1);

                    if (kind >= 0 && revision < keys.KeyAreaKeys.Length)
                    {
                        System.ReadOnlySpan<byte> areaKey = keys.KeyAreaKeys[revision][kind].DataRo;

                        using System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create();

                        aes.Key = areaKey.ToArray();
                        aes.DecryptEcb(source, key, System.Security.Cryptography.PaddingMode.None);
                    }
                }
            }

            context.ResponseData.Write(key);

            return ResultCode.Success;
        }

        [CommandCmif(0)]
        // GetConfig(u32 config_item) -> u64 config_value
        public ResultCode GetConfig(ServiceCtx context)
        {
            ConfigItem configItem = (ConfigItem)context.RequestData.ReadUInt32();

            // NOTE: Nintendo explicitly blacklists package2 hash here, amusingly.
            //       This is not blacklisted in safemode, but we're never in safe mode...
            if (configItem == ConfigItem.Package2Hash)
            {
                return ResultCode.InvalidArguments;
            }

            // TODO: This should call svcCallSecureMonitor using arg 0xC3000002.
            //       Since it's currently not implemented we can use a private method for now.
            SmcResult result = SmcGetConfig(context, out ulong configValue, configItem);

            // Nintendo has some special handling here for hardware type/is_retail.
            if (result == SmcResult.InvalidArgument)
            {
                switch (configItem)
                {
                    case ConfigItem.HardwareType:
                        configValue = (ulong)HardwareType.Icosa;
                        result = SmcResult.Success;
                        break;
                    case ConfigItem.HardwareState:
                        configValue = (ulong)HardwareState.Development;
                        result = SmcResult.Success;
                        break;
                    default:
                        break;
                }
            }

            context.ResponseData.Write(configValue);

            if (result == SmcResult.Success)
                return ResultCode.Success;

            return (ResultCode)((int)result << 9) | ResultCode.ModuleId;
        }

        private SmcResult SmcGetConfig(ServiceCtx context, out ulong configValue, ConfigItem configItem)
        {
            configValue = default;

#pragma warning disable IDE0059 // Remove unnecessary value assignment
            SystemVersion version = context.Device.System.ContentManager.GetCurrentFirmwareVersion();
#pragma warning restore IDE0059
            MemorySize memorySize = context.Device.Configuration.MemoryConfiguration.KernelMemorySize;

            switch (configItem)
            {
                case ConfigItem.DisableProgramVerification:
                    configValue = 0;
                    break;
                case ConfigItem.DramId:
                    if (memorySize == MemorySize.MemorySize8GiB)
                    {
                        configValue = (ulong)DramId.IowaSamsung8GiB;
                    }
                    else if (memorySize == MemorySize.MemorySize6GiB)
                    {
                        configValue = (ulong)DramId.IcosaSamsung6GiB;
                    }
                    else
                    {
                        configValue = (ulong)DramId.IcosaSamsung4GiB;
                    }

                    break;
                case ConfigItem.SecurityEngineInterruptNumber:
                    return SmcResult.NotImplemented;
                case ConfigItem.FuseVersion:
                    return SmcResult.NotImplemented;
                case ConfigItem.HardwareType:
                    configValue = (ulong)HardwareType.Icosa;
                    break;
                case ConfigItem.HardwareState:
                    configValue = (ulong)HardwareState.Production;
                    break;
                case ConfigItem.IsRecoveryBoot:
                    configValue = 0;
                    break;
                case ConfigItem.DeviceId:
                    return SmcResult.NotImplemented;
                case ConfigItem.BootReason:
                    // This was removed in firmware 4.0.0.
                    return SmcResult.InvalidArgument;
                case ConfigItem.MemoryMode:
                    configValue = (ulong)context.Device.Configuration.MemoryConfiguration;
                    break;
                case ConfigItem.IsDevelopmentFunctionEnabled:
                    configValue = 0;
                    break;
                case ConfigItem.KernelConfiguration:
                    return SmcResult.NotImplemented;
                case ConfigItem.IsChargerHiZModeEnabled:
                    return SmcResult.NotImplemented;
                case ConfigItem.QuestState:
                    return SmcResult.NotImplemented;
                case ConfigItem.RegulatorType:
                    return SmcResult.NotImplemented;
                case ConfigItem.DeviceUniqueKeyGeneration:
                    return SmcResult.NotImplemented;
                case ConfigItem.Package2Hash:
                    return SmcResult.NotImplemented;
                default:
                    return SmcResult.InvalidArgument;
            }

            return SmcResult.Success;
        }
    }
}
