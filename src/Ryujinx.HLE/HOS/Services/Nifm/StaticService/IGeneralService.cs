using Ryujinx.Common;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LanPlay;
using Ryujinx.HLE.HOS.Services.Nifm.StaticService.GeneralService;
using Ryujinx.HLE.HOS.Services.Nifm.StaticService.Types;
using Ryujinx.HLE.HOS.Services.Sockets.Bsd.Proxy;
using System;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

namespace Ryujinx.HLE.HOS.Services.Nifm.StaticService
{
    class IGeneralService : DisposableIpcService
    {
        private readonly GeneralServiceDetail _generalServiceDetail;

        private IPInterfaceProperties _targetPropertiesCache = null;
        private UnicastIPAddressInformation _targetAddressInfoCache = null;
        private string _cacheChosenInterface = null;

        public IGeneralService(ServiceCtx context)
        {
            _generalServiceDetail = new GeneralServiceDetail
            {
                ClientId = GeneralServiceManager.Count,
                IsAnyInternetRequestAccepted = !context.Device.DirtyHacks.IsEnabled(DirtyHack.NifmServiceDisableIsAnyInternetRequestAccepted), // NOTE: Why not accept any internet request?
            };

            NetworkChange.NetworkAddressChanged += LocalInterfaceCacheHandler;

            GeneralServiceManager.Add(_generalServiceDetail);
        }

        [CommandCmif(1)]
        // GetClientId() -> buffer<nn::nifm::ClientId, 0x1a, 4>
        public ResultCode GetClientId(ServiceCtx context)
        {
            ulong position = context.Request.RecvListBuff[0].Position;

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(sizeof(int));

            context.Memory.Write(position, _generalServiceDetail.ClientId);

            return ResultCode.Success;
        }

        [CommandCmif(4)]
        // CreateRequest(u32 version) -> object<nn::nifm::detail::IRequest>
        public ResultCode CreateRequest(ServiceCtx context)
        {
            uint version = context.RequestData.ReadUInt32();

            MakeObject(context, new IRequest(context.Device.System, version));

            // Doesn't occur in our case.
            // return ResultCode.ObjectIsNull;

            Logger.Stub?.PrintStub(LogClass.ServiceNifm, new { version });

            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // CreateScanRequest() -> object<nn::nifm::detail::IScanRequest>
        public ResultCode CreateScanRequest(ServiceCtx context)
        {
            MakeObject(context, new IScanRequest(context));

            return ResultCode.Success;
        }

        // [Nextendo, HOME Menu work] The one network known, the one the computer is on, under
        // an id that stays the same as long as its name does.
        private static UInt128 ProfileId(string name)
        {
            byte[] hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("nextendo-profile:" + name));

            return System.Runtime.InteropServices.MemoryMarshal.Read<UInt128>(hash);
        }

        [CommandCmif(6)]
        // EnumerateNetworkInterfaces(u32 filter) -> (s32 count, buffer<nn::nifm::NetworkInterfaceInfo, 0xa>):
        // the wireless adapter of the computer, with its MAC address (shown in the Internet page).
        public ResultCode EnumerateNetworkInterfaces(ServiceCtx context)
        {
            context.RequestData.ReadUInt32();

            byte[] mac = null;

            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && adapter.GetPhysicalAddress().GetAddressBytes().Length == 6)
                {
                    mac = adapter.GetPhysicalAddress().GetAddressBytes();

                    break;
                }
            }

            int count = 0;

            if (mac != null && context.Request.RecvListBuff.Count > 0 && context.Request.RecvListBuff[0].Size >= 8)
            {
                byte[] info = new byte[8];

                info[0] = 1; // IEEE 802.11
                mac.CopyTo(info, 1);
                info[7] = 1;

                context.Memory.Write(context.Request.RecvListBuff[0].Position, info);
                context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize(8u);
                count = 1;
            }

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(7)]
        // EnumerateNetworkProfiles(u8 type) -> (s32 count, buffer<nn::nifm::detail::sf::NetworkProfileBasicInfo, 6>)
        public ResultCode EnumerateNetworkProfiles(ServiceCtx context)
        {
            byte type = context.RequestData.ReadByte();
            int count = 0;

            HostNetwork.Reading host = HostNetwork.Named;

            if ((type & 1) != 0 && host.Link != HostNetwork.Link.None && context.Request.ReceiveBuff.Count > 0 && context.Request.ReceiveBuff[0].Size >= 0x75)
            {
                bool wireless = host.Link == HostNetwork.Link.Wireless && host.Name.Length > 0;
                string name = wireless ? host.Name : "Ethernet";
                byte[] info = new byte[0x75];
                byte[] raw = System.Text.Encoding.UTF8.GetBytes(name);

                System.Runtime.InteropServices.MemoryMarshal.Write(info, ProfileId(name));
                raw.AsSpan(0, Math.Min(raw.Length, 0x3F)).CopyTo(info.AsSpan(0x10));
                info[0x50] = 0;                        // a profile the user made
                info[0x51] = (byte)(wireless ? 1 : 2); // IEEE 802.11 or Ethernet

                if (wireless)
                {
                    info[0x52] = (byte)Math.Min(raw.Length, 0x20); // the SSID: its length, then its bytes
                    raw.AsSpan(0, Math.Min(raw.Length, 0x20)).CopyTo(info.AsSpan(0x53));
                    info[0x73] = host.Authentication;
                    info[0x74] = host.Encryption;
                }

                context.Memory.Write(context.Request.ReceiveBuff[0].Position, info);
                count = 1;
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"[nifm] profiles: type {type}, buffer 0x{(context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0):X}, {count} written");

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(22)]
        // IsAnyForegroundRequestAccepted() -> bool
        public ResultCode IsAnyForegroundRequestAccepted(ServiceCtx context)
        {
            context.ResponseData.Write(HostNetwork.Current.Link != HostNetwork.Link.None);

            return ResultCode.Success;
        }

        [CommandCmif(33)] // 2.0.0+
        // ConfirmSystemAvailability(): the network service may be used.
        public ResultCode ConfirmSystemAvailability(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(8)]
        // GetNetworkProfile(nn::util::Uuid) -> buffer<nn::nifm::detail::sf::NetworkProfileData, 0x1a, 0x17c>:
        // the one profile known is the network the computer is on.
        public ResultCode GetNetworkProfile(ServiceCtx context)
        {
            context.RequestData.ReadBytes(0x10);

            return GetCurrentNetworkProfile(context);
        }

        [CommandCmif(9)]
        // SetNetworkProfile(buffer<nn::nifm::detail::sf::NetworkProfileData, 0x19, 0x17c>) -> nn::util::Uuid:
        // kept as it is (the network of the computer is not changed from here).
        public ResultCode SetNetworkProfile(ServiceCtx context)
        {
            UInt128 id = 0;

            if (context.Request.PtrBuff.Count > 0 && context.Request.PtrBuff[0].Size >= 0x10)
            {
                id = context.Memory.Read<UInt128>(context.Request.PtrBuff[0].Position);
            }

            if (id == 0)
            {
                HostNetwork.Reading host = HostNetwork.Named;

                id = ProfileId(host.Link == HostNetwork.Link.Wireless && host.Name.Length > 0 ? host.Name : "Ethernet");
            }

            context.ResponseData.Write(id);

            return ResultCode.Success;
        }

        [CommandCmif(5)]
        // GetCurrentNetworkProfile() -> buffer<nn::nifm::detail::sf::NetworkProfileData, 0x1a, 0x17c>
        public ResultCode GetCurrentNetworkProfile(ServiceCtx context)
        {
            ulong networkProfileDataPosition = context.Request.RecvListBuff[0].Position;

            (IPInterfaceProperties interfaceProperties, UnicastIPAddressInformation unicastAddress) = GetLocalInterface(context);

            if (interfaceProperties == null || unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((uint)Unsafe.SizeOf<NetworkProfileData>());

            NetworkProfileData networkProfile = new()
            {
                Uuid = ProfileId(HostNetwork.Named.Link == HostNetwork.Link.Wireless && HostNetwork.Named.Name.Length > 0 ? HostNetwork.Named.Name : "Ethernet"),
            };

            networkProfile.IpSettingData.IpAddressSetting = new IpAddressSetting(interfaceProperties, unicastAddress);
            networkProfile.IpSettingData.DnsSetting = new DnsSetting(interfaceProperties);

            // [Nextendo, HOME Menu work] The profile is the network the computer is on: its
            // wireless network by name, or its cable.
            HostNetwork.Reading host = HostNetwork.Named;
            bool wireless = host.Link == HostNetwork.Link.Wireless && host.Name.Length > 0;
            byte[] name = System.Text.Encoding.UTF8.GetBytes(wireless ? host.Name : "Ethernet");

            name.AsSpan(0, System.Math.Min(name.Length, 63)).CopyTo(networkProfile.Name.AsSpan());

            networkProfile.Unknown[0] = 0;                        // a profile the user made
            networkProfile.Unknown[1] = (byte)(wireless ? 1 : 2); // IEEE 802.11 or Ethernet
            networkProfile.Unknown[2] = 1;                        // connects by itself

            if (wireless)
            {
                int length = System.Math.Min(name.Length, 32);

                networkProfile.WirelessSettingData.SsidLength = (byte)length;
                name.AsSpan(0, length).CopyTo(networkProfile.WirelessSettingData.Ssid.AsSpan());

                // After the name: hidden network or not, then how it is secured.
                networkProfile.WirelessSettingData.Unknown[1] = host.Authentication;
                networkProfile.WirelessSettingData.Unknown[2] = host.Encryption;
            }

            context.Memory.Write(networkProfileDataPosition, networkProfile);

            return ResultCode.Success;
        }

        [CommandCmif(12)]
        // GetCurrentIpAddress() -> nn::nifm::IpV4Address
        public ResultCode GetCurrentIpAddress(ServiceCtx context)
        {
            // Once the game is actually using the LAN Play network, the console lives on the virtual
            // 10.13.x.x network and games that implement their own LAN mode use this address to advertise
            // themselves. Until then the host address is reported, so simply having LAN Play selected
            // changes nothing for online play.
            if (SocketHelpers.ActiveLanPlayStack is { IsGuestActive: true } lanPlay)
            {
                context.ResponseData.WriteStruct(new IpV4Address(lanPlay.NetworkInterface.Address));

                Logger.Info?.Print(LogClass.ServiceNifm, $"Console's LAN Play IP is \"{lanPlay.NetworkInterface.Address}\".");

                return ResultCode.Success;
            }

            (_, UnicastIPAddressInformation unicastAddress) = GetLocalInterface(context);

            if (unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            context.ResponseData.WriteStruct(new IpV4Address(unicastAddress.Address));

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            return ResultCode.Success;
        }

        [CommandCmif(15)]
        // GetCurrentIpConfigInfo() -> (nn::nifm::IpAddressSetting, nn::nifm::DnsSetting)
        public ResultCode GetCurrentIpConfigInfo(ServiceCtx context)
        {
            (IPInterfaceProperties interfaceProperties, UnicastIPAddressInformation unicastAddress) = GetLocalInterface(context);

            if (interfaceProperties == null || unicastAddress == null)
            {
                return ResultCode.NoInternetConnection;
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"Console's local IP is \"{unicastAddress.Address}\".");

            if (SocketHelpers.ActiveLanPlayStack is { IsGuestActive: true } lanPlay)
            {
                context.ResponseData.WriteStruct(new IpAddressSetting(
                    lanPlay.NetworkInterface.Address,
                    lanPlay.NetworkInterface.SubnetMask,
                    NetworkHelpers.ConvertUint(LanPlayProtocol.GatewayAddress)));
            }
            else
            {
                context.ResponseData.WriteStruct(new IpAddressSetting(interfaceProperties, unicastAddress));
            }

            context.ResponseData.WriteStruct(new DnsSetting(interfaceProperties));

            return ResultCode.Success;
        }

        [CommandCmif(18)]
        // GetInternetConnectionStatus() -> nn::nifm::detail::sf::InternetConnectionStatus
        public ResultCode GetInternetConnectionStatus(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The link of the computer itself: wireless with its
            // strength, cable, or none (the HOME Menu draws its top right icon from this).
            HostNetwork.Reading host = HostNetwork.Named;

            if (host.Link == HostNetwork.Link.None)
            {
                return ResultCode.NoInternetConnection;
            }

            InternetConnectionStatus internetConnectionStatus = new()
            {
                Type = host.Link == HostNetwork.Link.Cable ? InternetConnectionType.Ethernet : InternetConnectionType.WiFi,
                WifiStrength = host.Bars,
                State = InternetConnectionState.Connected,
            };

            context.ResponseData.WriteStruct(internetConnectionStatus);

            return ResultCode.Success;
        }

        [CommandCmif(21)]
        // IsAnyInternetRequestAccepted(buffer<nn::nifm::ClientId, 0x19, 4>) -> bool
        public ResultCode IsAnyInternetRequestAccepted(ServiceCtx context)
        {
            ulong position = context.Request.PtrBuff[0].Position;
#pragma warning disable IDE0059 // Remove unnecessary value assignment
            ulong size = context.Request.PtrBuff[0].Size;
#pragma warning restore IDE0059

            int clientId = context.Memory.Read<int>(position);

            context.ResponseData.Write(GeneralServiceManager.Get(clientId).IsAnyInternetRequestAccepted);

            return ResultCode.Success;
        }

        private (IPInterfaceProperties, UnicastIPAddressInformation) GetLocalInterface(ServiceCtx context)
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                return (null, null);
            }

            string chosenInterface = context.Device.Configuration.MultiplayerLanInterfaceId;

            if (_targetPropertiesCache == null || _targetAddressInfoCache == null || _cacheChosenInterface != chosenInterface)
            {
                _cacheChosenInterface = chosenInterface;

                (_targetPropertiesCache, _targetAddressInfoCache) = NetworkHelpers.GetLocalInterface(chosenInterface);
            }

            return (_targetPropertiesCache, _targetAddressInfoCache);
        }

        private void LocalInterfaceCacheHandler(object sender, EventArgs e)
        {
            Logger.Info?.Print(LogClass.ServiceNifm, "NetworkAddress changed, invalidating cached data.");

            _targetPropertiesCache = null;
            _targetAddressInfoCache = null;
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                NetworkChange.NetworkAddressChanged -= LocalInterfaceCacheHandler;

                GeneralServiceManager.Remove(_generalServiceDetail.ClientId);
            }
        }
        // [Nextendo, HOME Menu work] nn::nifm::detail::sf::AccessPointData (0x34 bytes), as far as
        // the Internet page shows it: SSID length at 0, SSID at 1, bars at 0x2C, channel at 0x2E,
        // authentication at 0x30, encryption at 0x31, available at 0x32-0x33.
        private const int AccessPointSize = 0x34;

        private static void WriteAccessPoint(System.Span<byte> entry, string ssid, byte bars, int channel, byte authentication, byte encryption)
        {
            byte[] name = System.Text.Encoding.UTF8.GetBytes(ssid);
            int length = System.Math.Min(name.Length, 32);

            entry.Clear();
            entry[0] = (byte)length;
            name.AsSpan(0, length).CopyTo(entry[1..]);
            entry[0x2C] = bars;
            System.BitConverter.TryWriteBytes(entry.Slice(0x2E, 2), (ushort)channel);
            entry[0x30] = authentication;
            entry[0x31] = encryption;
            entry[0x32] = 1; // the page lists it as one to connect to (greyed out without)
            entry[0x33] = 1;
        }

        [CommandCmif(47)] // 19.0.0+
        // GetScanData(u64) -> (s32 count, buffer<nn::nifm::detail::sf::AccessPointData, 6>): the
        // wireless networks around the computer, as Windows sees them.
        public ResultCode GetScanDataV19(ServiceCtx context)
        {
            return WriteScanData(context, context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Position : 0, context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0);
        }

        [CommandCmif(35)] // 4.0.0+
        // GetScanData() -> (s32 count, buffer<nn::nifm::detail::sf::AccessPointData, 6>)
        public ResultCode GetScanData(ServiceCtx context)
        {
            return WriteScanData(context, context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Position : 0, context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0);
        }

        private static ResultCode WriteScanData(ServiceCtx context, ulong position, ulong size)
        {
            System.Collections.Generic.List<HostNetwork.Nearby> networks = HostNetwork.Scan();
            int count = System.Math.Min(networks.Count, (int)(size / AccessPointSize));

            byte[] data = new byte[count * AccessPointSize];

            for (int i = 0; i < count; i++)
            {
                HostNetwork.Nearby network = networks[i];

                WriteAccessPoint(data.AsSpan(i * AccessPointSize, AccessPointSize), network.Name, network.Bars, network.Channel, network.Authentication, network.Encryption);
            }

            if (count > 0)
            {
                context.Memory.Write(position, data);
            }

            Logger.Info?.Print(LogClass.ServiceNifm, $"[nifm] scan: {count} network(s) around the computer");

            context.ResponseData.Write(count);

            return ResultCode.Success;
        }

        [CommandCmif(36)]
        // GetCurrentAccessPoint() -> buffer<nn::nifm::detail::sf::AccessPointData, 0x1a>
        public ResultCode GetCurrentAccessPoint(ServiceCtx context)
        {
            // [Nextendo, HOME Menu work] The wireless network the computer is on, by name.
            HostNetwork.Reading host = HostNetwork.Named;

            if (host.Link != HostNetwork.Link.Wireless)
            {
                return ResultCode.NoInternetConnection;
            }

            ulong position = context.Request.RecvListBuff[0].Position;
            int size = 0x34;
            byte[] accessPoint = new byte[size];
            byte[] name = System.Text.Encoding.UTF8.GetBytes(host.Name);
            int length = System.Math.Min(name.Length, 32);

            accessPoint[0] = (byte)length;
            System.Array.Copy(name, 0, accessPoint, 1, length);

            // Found by probing the Internet page: bars of signal at 0x2C, channel at 0x2E (the
            // page works the frequency band out of the channel).
            accessPoint[0x2C] = host.Bars;
            System.BitConverter.TryWriteBytes(System.MemoryExtensions.AsSpan(accessPoint, 0x2E, 2), (ushort)host.Channel);
            accessPoint[0x30] = host.Authentication;
            accessPoint[0x31] = host.Encryption;

            context.Response.PtrBuff[0] = context.Response.PtrBuff[0].WithSize((uint)size);
            context.Memory.Write(position, accessPoint);

            return ResultCode.Success;
        }

        [CommandCmif(17)]
        // IsWirelessCommunicationEnabled() -> bool
        public ResultCode IsWirelessCommunicationEnabled(ServiceCtx context)
        {
            // False is airplane mode: the one of the computer, when it has one.
            context.ResponseData.Write(!HostNetwork.Current.Airplane);

            return ResultCode.Success;
        }

        [CommandCmif(20)]
        // IsEthernetCommunicationEnabled() -> bool
        public ResultCode IsEthernetCommunicationEnabled(ServiceCtx context)
        {
            context.ResponseData.Write(true);

            return ResultCode.Success;
        }
    }
}
