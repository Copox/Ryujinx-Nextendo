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

        public static void SetLanguage(int language)
        {
            try
            {
                LanguageChanged?.Invoke(language);
            }
            catch (Exception)
            {
                // kept for this session only
            }
        }
    }
}
