using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu work] The "Ryujinx | Nextendo" section of the HOME Menu's settings
    /// (in the place of Bluetooth Audio): the themes page, its entries as System's ("Resolution
    /// ..... 2x", the value on the right in the theme's colour); A opens the system's own select
    /// box (the one of Region and Language: the title, the choices, the one in use ticked), the
    /// choice is kept in the emulator's settings. The added code makes the boxes when the page is
    /// built and tells which section built it (data page +0x3C0: 1 for this one).
    /// </summary>
    internal static class QlaunchSettingsPage
    {
        /// <summary>The page being shown is this section (read when the page is built).</summary>
        public static bool Active { get; set; }

        /// <summary>Where the values shown on the right are, in the data page (0x80 bytes each).</summary>
        public const int ValuesAt = 0x3000;

        /// <summary>The size of the values, for the data page writes.</summary>
        public const int ValuesSize = 0x80 * 18;

        /// <summary>The select boxes' table (number of choices, choice in use): data page.</summary>
        public const int BoxesAt = 0x3B00;
        public const int BoxesSize = 0x100;

        /// <summary>The labels of the boxes (pointers, then their names): data page.</summary>
        public const int LabelsAt = 0x4000;
        public const int LabelsSize = 0x2000;

        /// <summary>The debug string of the added code when a choice is made in a box.</summary>
        public const string ResultSignal = "NxSettng";

        // The setting whose box was opened last (its choice comes back with its number too).
        private const int OpenRequest = 0x3C4;
        private const int ResultChoice = 0x3C8;
        private const int ResultSetting = 0x3CC;

        private static readonly Dictionary<string, Dictionary<string, string>> _texts = new()
        {
            ["en"] = new()
            {
                ["resolution"] = "Resolution", ["aa"] = "Anti-aliasing", ["filter"] = "Scaling", ["aniso"] = "Anisotropic",
                ["aspect"] = "Aspect ratio", ["mode"] = "Console mode", ["api"] = "Graphics API", ["threading"] = "GPU threading",
                ["shadercache"] = "Shader cache", ["recompression"] = "Tex. recompress", ["macrohle"] = "Macro HLE",
                ["on"] = "On", ["off"] = "Off", ["auto"] = "Auto", ["native"] = "Native", ["tv"] = "TV", ["handheld"] = "Handheld",
                ["stretch"] = "Stretched",
                ["vanilla"] = "HOME Menu as on the console",
                ["xcicards"] = "Game cards as virtual cards",
                ["restart"] = "To apply these settings, Ryujinx | Nextendo needs to restart:",
            },
            ["fr"] = new()
            {
                ["resolution"] = "Résolution", ["aa"] = "Anticrénelage", ["filter"] = "Mise à l'échelle", ["aniso"] = "Anisotrope",
                ["aspect"] = "Format d'image", ["mode"] = "Mode console", ["api"] = "API graphique", ["threading"] = "Multithread GPU",
                ["shadercache"] = "Cache shaders", ["recompression"] = "Recomp. textures", ["macrohle"] = "Macro HLE",
                ["on"] = "Oui", ["off"] = "Non", ["auto"] = "Auto", ["native"] = "Native", ["tv"] = "TV", ["handheld"] = "Portable",
                ["stretch"] = "Étiré",
                ["vanilla"] = "Menu HOME comme sur la console",
                ["xcicards"] = "Cartes de jeu virtuelles (XCI)",
                ["restart"] = "Pour appliquer ces paramètres, Ryujinx | Nextendo doit redémarrer :",
            },
            ["es"] = new()
            {
                ["resolution"] = "Resolución", ["aa"] = "Antialiasing", ["filter"] = "Escalado", ["aniso"] = "Anisotrópico",
                ["aspect"] = "Relación", ["mode"] = "Modo consola", ["api"] = "API gráfica", ["threading"] = "Hilos GPU",
                ["shadercache"] = "Caché shaders", ["recompression"] = "Recomp. texturas", ["macrohle"] = "Macro HLE",
                ["on"] = "Sí", ["off"] = "No", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portátil",
                ["stretch"] = "Estirado",
                ["vanilla"] = "Menú HOME como en la consola",
                ["xcicards"] = "Tarjetas virtuales (XCI)",
                ["restart"] = "Para aplicar estos ajustes, Ryujinx | Nextendo debe reiniciarse:",
            },
            ["de"] = new()
            {
                ["resolution"] = "Auflösung", ["aa"] = "Kantenglättung", ["filter"] = "Skalierung", ["aniso"] = "Anisotrop",
                ["aspect"] = "Seitenverh.", ["mode"] = "Konsolenmodus", ["api"] = "Grafik-API", ["threading"] = "GPU-Threads",
                ["shadercache"] = "Shader-Cache", ["recompression"] = "Textur-Komp.", ["macrohle"] = "Macro HLE",
                ["on"] = "An", ["off"] = "Aus", ["auto"] = "Auto", ["native"] = "Nativ", ["tv"] = "TV", ["handheld"] = "Handheld",
                ["stretch"] = "Gestreckt",
                ["vanilla"] = "HOME-Menü wie auf der Konsole",
                ["xcicards"] = "Virtuelle Spielkarten (XCI)",
                ["restart"] = "Um diese Einstellungen zu übernehmen, muss Ryujinx | Nextendo neu starten:",
            },
            ["it"] = new()
            {
                ["resolution"] = "Risoluzione", ["aa"] = "Antialiasing", ["filter"] = "Scalatura", ["aniso"] = "Anisotropico",
                ["aspect"] = "Proporzioni", ["mode"] = "Modalità", ["api"] = "API grafica", ["threading"] = "Thread GPU",
                ["shadercache"] = "Cache shader", ["recompression"] = "Ricomp. texture", ["macrohle"] = "Macro HLE",
                ["on"] = "Sì", ["off"] = "No", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portatile",
                ["stretch"] = "Allungato",
                ["vanilla"] = "Menu HOME come sulla console",
                ["xcicards"] = "Schede virtuali (XCI)",
                ["restart"] = "Per applicare queste impostazioni, Ryujinx | Nextendo deve riavviarsi:",
            },
            ["pt"] = new()
            {
                ["resolution"] = "Resolução", ["aa"] = "Antisserrilh.", ["filter"] = "Escala", ["aniso"] = "Anisotrópico",
                ["aspect"] = "Proporção", ["mode"] = "Modo console", ["api"] = "API gráfica", ["threading"] = "Threads GPU",
                ["shadercache"] = "Cache shaders", ["recompression"] = "Recomp. texturas", ["macrohle"] = "Macro HLE",
                ["on"] = "Sim", ["off"] = "Não", ["auto"] = "Auto", ["native"] = "Nativa", ["tv"] = "TV", ["handheld"] = "Portátil",
                ["stretch"] = "Esticado",
                ["vanilla"] = "Menu HOME como no console",
                ["xcicards"] = "Cartões virtuais (XCI)",
                ["restart"] = "Para aplicar estas configurações, o Ryujinx | Nextendo precisa reiniciar:",
            },
            ["ja"] = new()
            {
                ["resolution"] = "解像度", ["aa"] = "アンチエイリアス", ["filter"] = "スケーリング", ["aniso"] = "異方性",
                ["aspect"] = "アスペクト比", ["mode"] = "本体モード", ["api"] = "グラフィックAPI", ["threading"] = "GPUスレッド",
                ["shadercache"] = "シェーダー", ["recompression"] = "テクスチャ圧縮", ["macrohle"] = "Macro HLE",
                ["on"] = "ON", ["off"] = "OFF", ["auto"] = "自動", ["native"] = "ネイティブ", ["tv"] = "TV", ["handheld"] = "携帯",
                ["stretch"] = "引き伸ばし",
                ["vanilla"] = "本体と同じHOMEメニュー",
                ["xcicards"] = "バーチャルゲームカード (XCI)",
                ["restart"] = "これらの設定を反映するには、Ryujinx | Nextendoを再起動する必要があります：",
            },
        };

        private static string Text(string key) => Text(key, QlaunchThemePage.LanguageKey);

        /// <summary>A text of the section in a language ("en", "fr"...): English when not known.</summary>
        public static string Text(string key, string language) =>
            _texts.TryGetValue(language, out var texts) && texts.TryGetValue(key, out string text) ? text
                : _texts["en"].TryGetValue(key, out string english) ? english : key;

        /// <summary>A value as shown ("on", "off"... translated; "2x", "FXAA" as they are).</summary>
        public static string Shown(string value, string language) => _texts["en"].ContainsKey(value) ? Text(value, language) : value;

        /// <summary>The label of a box's title, in setting.msbt.</summary>
        public static string TitleLabel(string key) => $"Nx_SetT_{key}";

        /// <summary>The label of a box's choice, in setting.msbt.</summary>
        public static string ChoiceLabel(string key, int choice) => $"Nx_SetV_{key}_{choice}";

        /// <summary>The entries of the section: the page's own two, then the added ones.</summary>
        public static void Write(byte[] data, int entriesToMake, string refreshSignal, ulong dataAddress)
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;
            int added = Math.Max(0, Math.Min(settings.Count - 2, entriesToMake));
            string language = QlaunchThemePage.LanguageKey;

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), added);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3DC), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), entriesToMake);
            Encoding.ASCII.GetBytes(refreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1);

            int count = Math.Min(Math.Min(settings.Count, added + 2), 16);
            int names = LabelsAt + 0x40 * 16;

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(BoxesAt), count);

            for (int i = 0; i < count; i++)
            {
                NextendoEmulatorSetting setting = settings[i];
                int current = Math.Clamp(setting.Get(), 0, setting.Values.Length - 1);

                // the entry: its name, its value on the right
                byte[] name = Encoding.Unicode.GetBytes(Text(setting.Key));
                int at = i switch { 0 => 0x2400, 1 => 0x2500, _ => 0x400 + 0x100 * (i - 2) };

                name.AsSpan(0, Math.Min(name.Length, 0xFE)).CopyTo(data.AsSpan(at));

                byte[] value = Encoding.Unicode.GetBytes(Shown(setting.Values[current], language));

                value.AsSpan(0, Math.Min(value.Length, 0x7E)).CopyTo(data.AsSpan(ValuesAt + 0x80 * i));

                // its box: the number of choices, the one in use, the labels
                int choices = Math.Min(setting.Values.Length, 7);

                data[BoxesAt + 4 + i] = (byte)choices;
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(BoxesAt + 0x40 + 4 * i), current);

                for (int c = -1; c < choices; c++)
                {
                    byte[] label = Encoding.ASCII.GetBytes((c < 0 ? TitleLabel(setting.Key) : ChoiceLabel(setting.Key, c)) + "\0");

                    label.CopyTo(data.AsSpan(names));
                    BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(LabelsAt + 0x40 * i + 8 * (c + 1)), dataAddress + (ulong)names);
                    names += (label.Length + 7) & ~7;
                }
            }
        }

        // ---- Settings read when the emulation starts: a restart, or their old values ----

        // The settings changed that need a restart (their rows), with the values they had.
        private static readonly Dictionary<int, int> _changed = [];

        /// <summary>Settings that need a restart were changed in the section.</summary>
        public static bool RestartPending => _changed.Count > 0;

        /// <summary>The settings that need a restart, in order (their rows).</summary>
        private static List<int> RestartRows()
        {
            List<int> rows = [];
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;

            for (int i = 0; i < settings.Count; i++)
            {
                if (settings[i].Restart)
                {
                    rows.Add(i);
                }
            }

            return rows;
        }

        /// <summary>The label of the restart dialog's body for some settings (a bit each).</summary>
        public static string RestartLabel(int mask) => $"Nx_SetRst_{mask}";

        /// <summary>The bodies of the restart dialog: (label, text) for each set of settings.</summary>
        public static IEnumerable<(string Label, string Text)> RestartBodies(string language)
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;
            List<int> rows = RestartRows();

            for (int mask = 1; mask < (1 << rows.Count); mask++)
            {
                StringBuilder text = new(Text("restart", language));

                text.Append('\n');

                for (int bit = 0; bit < rows.Count; bit++)
                {
                    if ((mask & (1 << bit)) != 0)
                    {
                        text.Append("\n• ").Append(Text(settings[rows[bit]].Key, language));
                    }
                }

                yield return (RestartLabel(mask), text.ToString());
            }
        }

        /// <summary>The restart dialog asked (by the added code, once a frame): which settings.</summary>
        public static void AskRestart(KProcess process, ulong dataAddress)
        {
            List<int> rows = RestartRows();
            int mask = 0;

            for (int bit = 0; bit < rows.Count; bit++)
            {
                if (_changed.ContainsKey(rows[bit]))
                {
                    mask |= 1 << bit;
                }
            }

            if (mask == 0)
            {
                return;
            }

            byte[] label = Encoding.ASCII.GetBytes(RestartLabel(mask) + "\0");

            process.CpuMemory.Write(dataAddress + 0x6200, label);
            process.CpuMemory.Write(dataAddress + 0x6110, dataAddress + 0x6200);
            process.CpuMemory.Write(dataAddress + 0x610C, 1);

            Logger.Info?.Print(LogClass.ServiceSet, $"[settings] restart dialog asked ({RestartLabel(mask)})");
        }

        /// <summary>
        /// The restart dialog answered: restart (the settings are kept), or the settings as they
        /// were. True when the entries are to be shown again.
        /// </summary>
        public static bool TakeRestartAnswer(KProcess process, ulong dataAddress)
        {
            int button = process.CpuMemory.Read<int>(dataAddress + 0x6108);

            if (button == 0)
            {
                Logger.Info?.Print(LogClass.ServiceSet, "[settings] restart to apply the settings");
                _changed.Clear();
                HomeMenuStatus.RequestPower(true);

                return false;
            }

            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;

            foreach ((int row, int value) in _changed)
            {
                settings[row].Set(value);
            }

            Logger.Info?.Print(LogClass.ServiceSet, $"[settings] {_changed.Count} setting(s) back as they were");
            _changed.Clear();

            return true;
        }

        /// <summary>An entry picked: its box opened (by the added code, the choice in use ticked).</summary>
        public static void Open(KProcess process, ulong dataAddress, int row)
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;

            if (row < 0 || row >= settings.Count || row >= 16)
            {
                return;
            }

            int current = Math.Clamp(settings[row].Get(), 0, settings[row].Values.Length - 1);

            process.CpuMemory.Write(dataAddress + (ulong)(BoxesAt + 0x40 + 4 * row), current);
            process.CpuMemory.Write(dataAddress + OpenRequest, row + 1);
        }

        /// <summary>A choice made in a box: the setting takes it. Its row (-1: none).</summary>
        public static int TakeResult(KProcess process, ulong dataAddress)
        {
            IReadOnlyList<NextendoEmulatorSetting> settings = NextendoEmulatorSettings.List;
            int choice = process.CpuMemory.Read<int>(dataAddress + ResultChoice);
            int row = process.CpuMemory.Read<int>(dataAddress + ResultSetting);

            if (row < 0 || row >= settings.Count || choice < 0 || choice >= settings[row].Values.Length)
            {
                return -1;
            }

            NextendoEmulatorSetting changed = settings[row];

            // read when the emulation starts: the value it had is kept, to give it back
            if (changed.Restart)
            {
                int before = Math.Clamp(changed.Get(), 0, changed.Values.Length - 1);

                if (!_changed.ContainsKey(row))
                {
                    _changed[row] = before;
                }

                if (_changed[row] == choice)
                {
                    _changed.Remove(row);
                }
            }

            settings[row].Set(choice);

            Logger.Info?.Print(LogClass.ServiceSet, $"[settings] {settings[row].Key}: {settings[row].Values[choice]}");

            return row;
        }
    }
}
