using System;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Ptm.Psm
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The battery of the computer, shown as the console's own.
    /// A computer without one (or a system this is not read on) is a console on its charger
    /// at 100 %.
    /// </summary>
    static partial class HostBattery
    {
        public readonly record struct State(int Percent, bool Plugged);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte AcLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetSystemPowerStatus(out SystemPowerStatus status);

        public static State Read()
        {
            try
            {
                // BatteryFlag 128 = no battery, 255 = unknown; BatteryLifePercent 255 = unknown.
                if (OperatingSystem.IsWindows() && GetSystemPowerStatus(out SystemPowerStatus status) &&
                    status.BatteryFlag != 128 && status.BatteryFlag != 255 && status.BatteryLifePercent <= 100)
                {
                    return new State(Math.Max(1, (int)status.BatteryLifePercent), status.AcLineStatus != 0);
                }
            }
            catch (Exception)
            {
                // read as a console on its charger
            }

            return new State(100, true);
        }
    }
}
