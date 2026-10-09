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

        public readonly record struct Reading(Link Link, string Name, int SignalPercent, bool Airplane)
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
                }

                if (link == Link.None)
                {
                    airplane = WindowsAirplaneMode();
                }
            }

            return new Reading(link, name, signal, airplane);
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
