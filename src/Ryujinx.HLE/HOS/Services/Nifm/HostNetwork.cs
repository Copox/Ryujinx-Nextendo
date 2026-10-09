using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Nifm
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The network of the computer, as the console software asks
    /// about its own: wireless, cable or nothing, the name and strength of the wireless
    /// network, and whether every radio is off (airplane mode).
    /// Never blocks a caller: what is returned is the last reading, renewed in the background.
    /// </summary>
    static class HostNetwork
    {
        public enum Link
        {
            None,
            Wireless,
            Cable,
        }

        public readonly record struct Reading(Link Link, string Name, int SignalPercent, bool Airplane, int Channel = 0, byte Authentication = 0, byte Encryption = 0)
        {
            /// <summary>Bars of the console's indicator, 0 to 3.</summary>
            public byte Bars => Link != Link.Wireless ? (byte)0 : SignalPercent >= 70 ? (byte)3 : SignalPercent >= 40 ? (byte)2 : (byte)1;
        }

        private static Reading _last = Read(quick: true);
        private static long _readAt = long.MinValue / 2;
        private static int _reading;

        public static Reading Current
        {
            get
            {
                if (Environment.TickCount64 - _readAt > 8000 && Interlocked.Exchange(ref _reading, 1) == 0)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _last = Read(quick: false);
                        }
                        catch (Exception)
                        {
                            // the last reading stays
                        }
                        finally
                        {
                            _readAt = Environment.TickCount64;
                            _reading = 0;
                        }
                    });
                }

                return _last;
            }
        }

        private static Reading Read(bool quick)
        {
            Link link = Link.None;

            try
            {
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up ||
                        adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                        adapter.GetIPProperties().GatewayAddresses.Count == 0)
                    {
                        continue;
                    }

                    // A cable wins over the wireless network, as on a docked console.
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        if (link == Link.None)
                        {
                            link = Link.Wireless;
                        }
                    }
                    else
                    {
                        link = Link.Cable;
                    }
                }
            }
            catch (Exception)
            {
                link = NetworkInterface.GetIsNetworkAvailable() ? Link.Cable : Link.None;
            }

            string name = "";
            int signal = 100;
            bool airplane = false;
            int channel = 0;
            byte authentication = 0, encryption = 0;

            if (OperatingSystem.IsWindows() && !quick)
            {
                if (link == Link.Wireless)
                {
                    string report = Run("netsh", "wlan show interfaces");

                    Match ssid = Regex.Match(report, "^\\s*SSID\\s*:\\s*(.+?)\\s*$", RegexOptions.Multiline);
                    Match strength = Regex.Match(report, ":\\s*(\\d{1,3})\\s*%");

                    if (ssid.Success)
                    {
                        name = ssid.Groups[1].Value;
                    }

                    if (strength.Success)
                    {
                        signal = int.Parse(strength.Groups[1].Value);
                    }

                    // The report is in the language of the system: the lines are found by what
                    // they hold, not by their label.
                    Match channelLine = Regex.Match(report, "^\\s*(Canal|Channel|Kanal)\\s*:\\s*(\\d+)\\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                    if (channelLine.Success)
                    {
                        channel = int.Parse(channelLine.Groups[2].Value);
                    }

                    // nn::nifm::Authentication: 1 open, 4 WPA-PSK, 5 WPA2 (enterprise), 6 WPA2-PSK.
                    // nn::nifm::Encryption: 1 none, 2 WEP, 3 TKIP, 4 AES.
                    if (Regex.IsMatch(report, ":\\s*WPA[23]\\s*-?\\s*(Personal|Personnel|Pers)", RegexOptions.IgnoreCase) || Regex.IsMatch(report, ":\\s*WPA3", RegexOptions.IgnoreCase))
                    {
                        authentication = 6;
                    }
                    else if (Regex.IsMatch(report, ":\\s*WPA2", RegexOptions.IgnoreCase))
                    {
                        authentication = 5;
                    }
                    else if (Regex.IsMatch(report, ":\\s*WPA", RegexOptions.IgnoreCase))
                    {
                        authentication = 4;
                    }
                    else if (ssid.Success)
                    {
                        authentication = 1;
                    }

                    encryption = Regex.IsMatch(report, ":\\s*(CCMP|GCMP)", RegexOptions.IgnoreCase) ? (byte)4
                        : Regex.IsMatch(report, ":\\s*TKIP", RegexOptions.IgnoreCase) ? (byte)3
                        : Regex.IsMatch(report, ":\\s*WEP", RegexOptions.IgnoreCase) ? (byte)2
                        : ssid.Success ? (byte)1 : (byte)0;
                }

                if (link == Link.None)
                {
                    airplane = WindowsAirplaneMode();
                }
            }

            return new Reading(link, name, signal, airplane, channel, authentication, encryption);
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool WindowsAirplaneMode()
        {
            try
            {
                using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\RadioManagement\\SystemRadioState");

                return key?.GetValue("") is int state && state == 1;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Run(string program, string arguments)
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(program, arguments)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                string output = process.StandardOutput.ReadToEnd();

                process.WaitForExit(3000);

                return output;
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
