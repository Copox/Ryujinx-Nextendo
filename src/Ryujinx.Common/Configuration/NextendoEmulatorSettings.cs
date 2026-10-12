using System;
using System.Collections.Generic;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] A setting of the emulator shown in the HOME Menu's settings
    /// ("Ryujinx | Nextendo" section): its name (a key the HOME Menu page translates), its values
    /// as shown ("on" and "off" are keys too), the one in use and how to change it.
    /// </summary>
    public sealed class NextendoEmulatorSetting
    {
        public string Key { get; init; }
        public string[] Values { get; init; }
        public Func<int> Get { get; init; }
        public Action<int> Set { get; init; }

        /// <summary>Taken into account when the emulation starts again (it then starts again).</summary>
        public bool Restart { get; init; }
    }

    /// <summary>The emulator's settings for the HOME Menu, given by the interface.</summary>
    public static class NextendoEmulatorSettings
    {
        public static IReadOnlyList<NextendoEmulatorSetting> List { get; set; } = [];
    }
}
