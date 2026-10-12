using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The "Ryujinx | Nextendo" section of the HOME Menu's settings
    /// (in the place of Bluetooth Audio): the themes page, shown with one entry per setting of the
    /// emulator ("Resolution: 2x"); A shows its values (the one in use ticked), as the system's
    /// lists do (the region); A on one keeps it in the emulator's settings, B goes back.
    /// The added code tells which section built the page (data page +0x3C0: 1 for this one).
    /// </summary>
    internal static class QlaunchSettingsPage
    {
        private static bool _active;

        /// <summary>The page being shown is this section (read when the page is built).</summary>
        public static bool Active
        {
            get => _active;
            set
            {
                _active = value;
                _open = -1;
            }
        }

        // The setting whose values are shown (-1: the settings).
        private static int _open = -1;

        /// <summary>The values of a setting are shown (B goes back to the settings).</summary>
        public static bool ValuesShown => _active && _open >= 0;

        /// <summary>The row of the setting whose values are shown (to focus when going back).</summary>
        public static int OpenRow => _open;

        // U+E14B, the tick of the system's button font.
        private const string Tick = "\uE14B";

        private static readonly Dictionary<string, Dictionary<string, string>> _texts = new()
        {
            ["en"] = new()
            {
                ["resolution"] = "Resolution", ["aa"] = "Anti-aliasing", ["filter"] = "Scaling", ["aniso"] = "Anisotropic",
                ["aspect"] = "Aspect ratio", ["mode"] = "Console mode", ["api"] = "Graphics API", ["threading"] = "GPU threading",
                ["shadercache"] = "Shader cache", ["recompression"] = "Tex. recompress", ["macrohle"] = "Macro HLE",
                ["on"] = "On", ["off"] = "Off", ["auto"] = "Auto", ["native"] = "Native", ["tv"] = "TV", ["handheld"] = "Handheld",
                ["stretch"] = "Stretched",
            },
            ["fr"] = new()
            {
                ["resolution"] = "Résolution", ["aa"] = "Anticrénelage", ["filter"] = "Mise à l'échelle", ["aniso"] = "Anisotrope",
                ["aspect"] = "Format d'image", ["mode"] = "Mode console", ["api"] = "API graphique", ["threading"] = "Multithread GPU",
                ["shadercache"] = "Cache shaders", ["recompression"] = "Recomp. textures", ["macrohle"] = "Macro HLE",
                ["on"] = "Oui", ["off"] = "Non", ["auto"] = "Auto", ["native"] = "Native", ["tv"] = "TV", ["handheld"] = "Portable",
                ["stretch"] = "Étiré",
            },
            ["es"] = new()
            {
                ["resolution"] = "Resolución", ["aa"] = "Antialiasing", ["filter"] = "Escalado", ["aniso"] = "Anisotrópico",
                ["aspect"] = "Relación", ["mode"] = "Modo consola", ["api"] = "API gráfica", ["threading"] = "Hilos GPU",
                ["shadercache"] = "Caché shaders", ["recompression"] = "Recomp. texturas", ["macrohle"] = "Macro HLE",
                ["on"] = "Sí", ["off"] = "No", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portátil",
                ["stretch"] = "Estirado",
            },
            ["de"] = new()
            {
                ["resolution"] = "Auflösung", ["aa"] = "Kantenglättung", ["filter"] = "Skalierung", ["aniso"] = "Anisotrop",
                ["aspect"] = "Seitenverh.", ["mode"] = "Konsolenmodus", ["api"] = "Grafik-API", ["threading"] = "GPU-Threads",
                ["shadercache"] = "Shader-Cache", ["recompression"] = "Textur-Komp.", ["macrohle"] = "Macro HLE",
                ["on"] = "An", ["off"] = "Aus", ["auto"] = "Auto", ["native"] = "Nativ", ["tv"] = "TV", ["handheld"] = "Handheld",
                ["stretch"] = "Gestreckt",
            },
            ["it"] = new()
            {
                ["resolution"] = "Risoluzione", ["aa"] = "Antialiasing", ["filter"] = "Scalatura", ["aniso"] = "Anisotropico",
                ["aspect"] = "Proporzioni", ["mode"] = "Modalità", ["api"] = "API grafica", ["threading"] = "Thread GPU",
                ["shadercache"] = "Cache shader", ["recompression"] = "Ricomp. texture", ["macrohle"] = "Macro HLE",
                ["on"] = "Sì", ["off"] = "No", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portatile",
                ["stretch"] = "Allungato",
            },
            ["pt"] = new()
            {
                ["resolution"] = "Resolução", ["aa"] = "Antisserrilh.", ["filter"] = "Escala", ["aniso"] = "Anisotrópico",
                ["aspect"] = "Proporção", ["mode"] = "Modo console", ["api"] = "API gráfica", ["threading"] = "Threads GPU",
                ["shadercache"] = "Cache shaders", ["recompression"] = "Recomp. texturas", ["macrohle"] = "Macro HLE",
                ["on"] = "Sim", ["off"] = "Não", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portátil",
                ["stretch"] = "Esticado",
            },
            ["ja"] = new()
            {
                ["resolution"] = "解像度", ["aa"] = "アンチエイリアス", ["filter"] = "スケーリング", ["aniso"] = "異方性",
                ["aspect"] = "アスペクト比", ["mode"] = "本体モード", ["api"] = "グラフィックAPI", ["threading"] = "GPUスレッド",
                ["shadercache"] = "シェーダー", ["recompression"] = "テクスチャ圧縮", ["macrohle"] = "Macro HLE",
                ["on"] = "ON", ["off"] = "OFF", ["auto"] = "自動", ["native"] = "ネイティブ", ["tv"] = "TV", ["handheld"] = "携帯",
                ["stretch"] = "引き伸ばし",
            },
        };

        private static string Text(string key)
        {
            string language = QlaunchThemePage.LanguageKey;

            return _texts.TryGetValue(language, out var texts) && texts.TryGetValue(key, out string text) ? text
                : _texts["en"].TryGetValue(key, out string english) ? english : key;
        }

        private static string Row(NextendoEmulatorSetting setting)
        {
            int index = Math.Clamp(setting.Get(), 0, setting.Values.Length - 1);
            string value = setting.Values[index];

            return $"{Text(setting.Key)} : {(_texts["en"].ContainsKey(value) ? Text(value) : value)}";
        }

        private static string Shown(string value) => _texts["en"].ContainsKey(value) ? Text(value) : value;

        // The texts of the entries: the settings, or the values of the one open.
        private static List<string> Rows()
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;
            List<string> rows = [];

            if (_open >= 0 && _open < settings.Count)
            {
                NextendoEmulatorSetting setting = settings[_open];
                int current = setting.Get();

                for (int i = 0; i < setting.Values.Length; i++)
                {
                    rows.Add(i == current ? $"{Shown(setting.Values[i])}   {Tick}" : Shown(setting.Values[i]));
                }

                return rows;
            }

            foreach (NextendoEmulatorSetting setting in settings)
            {
                rows.Add(Row(setting));
            }

            return rows;
        }

        /// <summary>The entries of the section: the page's own two, then the added ones.</summary>
        public static void Write(byte[] data, int entriesToMake, string refreshSignal)
        {
            List<string> rows = Rows();
            int added = Math.Max(0, Math.Min(rows.Count - 2, entriesToMake));

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), added);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3DC), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), entriesToMake);
            Encoding.ASCII.GetBytes(refreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1);

            for (int i = 0; i < rows.Count && i < added + 2; i++)
            {
                byte[] name = Encoding.Unicode.GetBytes(rows[i]);
                int at = i switch { 0 => 0x2400, 1 => 0x2500, _ => 0x400 + 0x100 * (i - 2) };

                name.AsSpan(0, Math.Min(name.Length, 0xFE)).CopyTo(data.AsSpan(at));
            }
        }

        /// <summary>
        /// An entry picked: a setting shows its values; a value is kept and the settings shown
        /// again. Returns the entry to focus once the page is shown again (-1: not to show again).
        /// </summary>
        public static int Pick(int row)
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;

            if (_open < 0)
            {
                if (row < 0 || row >= settings.Count)
                {
                    return -1;
                }

                _open = row;

                return Math.Clamp(settings[row].Get(), 0, settings[row].Values.Length - 1);
            }

            NextendoEmulatorSetting setting = settings[_open];
            int back = _open;

            _open = -1;

            if (row >= 0 && row < setting.Values.Length)
            {
                setting.Set(row);

                Logger.Info?.Print(LogClass.ServiceSet, $"[settings] {setting.Key}: {setting.Values[row]}");
            }

            return back;
        }

        /// <summary>B on the values of a setting: the settings again. The entry to focus.</summary>
        public static int Back()
        {
            int back = _open;

            _open = -1;

            return back;
        }
    }
}
