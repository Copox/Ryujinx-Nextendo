using System;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo, HOME Menu work] Settings changed in the HOME Menu that are settings of the
    /// emulator too: the interface keeps them in its configuration.
    /// </summary>
    public static class NextendoSystemSettings
    {
        /// <summary>The system language, as the index of nn::settings::Language (SystemLanguage).</summary>
        public static event Action<int> LanguageChanged;

        public static void SetLanguage(int language) => Tell(() => LanguageChanged?.Invoke(language));

        /// <summary>The region, as nn::settings::RegionCode (Japan, USA, Europe, Australia, China, Korea, Taiwan).</summary>
        public static event Action<int> RegionChanged;

        public static void SetRegion(int region) => Tell(() => RegionChanged?.Invoke(region));

        /// <summary>The clock set by hand: its distance to the computer's clock, in seconds.</summary>
        public static event Action<long> TimeOffsetChanged;

        public static void SetTimeOffset(long seconds) => Tell(() => TimeOffsetChanged?.Invoke(seconds));

        /// <summary>"Synchronise clock via Internet": the computer's clock is the console's.</summary>
        public static event Action<bool> AutomaticTimeChanged;

        public static void SetAutomaticTime(bool enabled) => Tell(() => AutomaticTimeChanged?.Invoke(enabled));

        /// <summary>The time zone (a location name such as "Europe/Paris").</summary>
        public static event Action<string> TimeZoneChanged;

        public static void SetTimeZone(string location) => Tell(() => TimeZoneChanged?.Invoke(location));

        private static void Tell(Action tell)
        {
            try
            {
                tell();
            }
            catch (Exception)
            {
                // kept for this session only
            }
        }
    }
}
