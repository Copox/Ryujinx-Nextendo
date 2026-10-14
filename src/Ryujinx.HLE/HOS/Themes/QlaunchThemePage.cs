using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Kernel.Memory;
using Ryujinx.HLE.HOS.Kernel.Process;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Ryujinx.HLE.HOS.Themes
{
    /// <summary>
    /// [Nextendo, HOME Menu themes] The themes installed on the SD card listed in "System Settings >
    /// Themes" of the HOME Menu, after Basic White and Basic Black. The HOME Menu (qlaunch 22.5.0)
    /// only builds those two entries; code added after its image (Qlaunch/theme_page.s, assembled
    /// by Qlaunch/build.py) builds one more per theme. The emulator puts that code there when the
    /// program is loaded, with a data page holding the themes, and points the page at it.
    /// </summary>
    public static class QlaunchThemePage
    {
        private const string BuildId = "26CC3BC5AC534AE990B410769736EE81B919C999";

        public const uint Cave = 0x425F000;
        public const uint CodeSize = 0x8000;
        public const uint DataSize = 0x8000;
        public const uint Size = CodeSize + DataSize;

        private const int MaxEntries = 16;

        // "ThemezerNX\Purple Skies ANIMATED by Hsushi (186B6)" -> "Purple Skies ANIMATED by Hsushi".
        private static string DisplayName(string pack)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(pack.TrimEnd('\\', '/'));
            int id = name.LastIndexOf(" (", StringComparison.Ordinal);

            return id > 0 && name.EndsWith(')') ? name[..id] : name;
        }

        /// <summary>Where the picture of an added entry is in the HOME Menu's files (as it names its own).</summary>
        public static string PicturePath(int entry) => $"theme/NxThemeIcon_{entry:D2}.jpg";

        /// <summary>
        /// Where the picture of an entry of the pages of a theme is: files of their own (the HOME
        /// Menu keeps what it read of a file, a file read in both lists would show the other's).
        /// </summary>
        public static string PagePicturePath(int entry) => $"theme/NxPageIcon_{entry:D2}.jpg";

        // The pictures, one place per entry, of a fixed size (a JPEG followed by zeros): their
        // bytes change when the themes do, the HOME Menu's files stay as they are.
        private const int PictureSize = 0x8000;
        private static readonly byte[][] _pictures = new byte[MaxEntries][];
        private static readonly byte[][] _pagePictures = new byte[MaxEntries][];

        /// <summary>The places of the pictures of the added entries, to add to the HOME Menu's files.</summary>
        public static IEnumerable<(string Path, byte[] Data)> Pictures()
        {
            for (int i = 0; i < MaxEntries; i++)
            {
                _pictures[i] ??= new byte[PictureSize];

                yield return ("/" + PicturePath(i), _pictures[i]);
            }

            for (int i = 0; i < MaxEntries; i++)
            {
                _pagePictures[i] ??= new byte[PictureSize];

                yield return ("/" + PagePicturePath(i), _pagePictures[i]);
            }
        }

        /// <summary>The message of the footer text of X on a theme (added to the HOME Menu's common.msbt).</summary>
        public const string CustomizeLabel = "Nx_FooterText_Customize";

        /// <summary>The debug string the added code sends when the page is built.</summary>
        public const string RefreshSignal = "NxThemes";

        // The pages of a theme, shown in place of the themes (X on a theme): its parts, which of
        // them are applied, and the entry whose deletion waits for a second press.
        private static string _menuPack;
        private static List<(string Target, string File)> _menuParts = [];
        private static bool[] _menuOn = [];
        private static int _confirm = -1;
        // The texts of the pages of a theme, in each language of the system (an entry shows
        // about 21 characters: kept short).
        private static string _language = "en";

        /// <summary>The language of the page's texts ("en", "fr"...).</summary>
        internal static string LanguageKey => _language;

        private static readonly Dictionary<string, Dictionary<string, string>> _texts = new()
        {
            ["en"] = new()
            {
                ["apply"] = "Apply this theme",
                ["back"] = "Back to themes",
                ["on"] = "on",
                ["off"] = "off",
                ["confirm"] = "Confirm deletion",
                ["deleteOff"] = "Delete pages set off",
                ["deleteAll"] = "Delete whole theme",
                ["home"] = "HOME Menu",
                ["lock"] = "Lock screen",
                ["apps"] = "All software",
                ["set"] = "System Settings",
                ["news"] = "News",
                ["user"] = "User page",
                ["psl"] = "User selection",
            },
            ["fr"] = new()
            {
                ["apply"] = "Appliquer ce thème",
                ["back"] = "Retour aux thèmes",
                ["on"] = "oui",
                ["off"] = "non",
                ["confirm"] = "Confirmer la suppression",
                ["deleteOff"] = "Supprimer les pages « non »",
                ["deleteAll"] = "Supprimer tout le thème",
                ["home"] = "Menu HOME",
                ["lock"] = "Écran de verrouillage",
                ["apps"] = "Tous les logiciels",
                ["set"] = "Paramètres de la console",
                ["news"] = "Actualités",
                ["user"] = "Page utilisateur",
                ["psl"] = "Choix de l'utilisateur",
            },
            ["de"] = new()
            {
                ["apply"] = "Design anwenden",
                ["back"] = "Zurück zu Designs",
                ["on"] = "an",
                ["off"] = "aus",
                ["confirm"] = "Löschen bestätigen",
                ["deleteOff"] = "Inaktive Seiten löschen",
                ["deleteAll"] = "Ganzes Design löschen",
                ["home"] = "HOME-Menü",
                ["lock"] = "Sperrbildschirm",
                ["apps"] = "Alle Software",
                ["set"] = "Systemeinstellungen",
                ["news"] = "Neuigkeiten",
                ["user"] = "Nutzerseite",
                ["psl"] = "Nutzerauswahl",
            },
            ["es"] = new()
            {
                ["apply"] = "Aplicar este tema",
                ["back"] = "Volver a los temas",
                ["on"] = "sí",
                ["off"] = "no",
                ["confirm"] = "Confirmar borrado",
                ["deleteOff"] = "Borrar páginas en «no»",
                ["deleteAll"] = "Borrar todo el tema",
                ["home"] = "Menú HOME",
                ["lock"] = "Pantalla de bloqueo",
                ["apps"] = "Todos los programas",
                ["set"] = "Configuración",
                ["news"] = "Noticias",
                ["user"] = "Página de usuario",
                ["psl"] = "Elegir usuario",
            },
            ["it"] = new()
            {
                ["apply"] = "Applica questo tema",
                ["back"] = "Torna ai temi",
                ["on"] = "sì",
                ["off"] = "no",
                ["confirm"] = "Conferma eliminazione",
                ["deleteOff"] = "Elimina pagine «no»",
                ["deleteAll"] = "Elimina tutto il tema",
                ["home"] = "Menu HOME",
                ["lock"] = "Schermata di blocco",
                ["apps"] = "Tutti i software",
                ["set"] = "Impostazioni",
                ["news"] = "Notizie",
                ["user"] = "Pagina utente",
                ["psl"] = "Scelta utente",
            },
            ["nl"] = new()
            {
                ["apply"] = "Thema toepassen",
                ["back"] = "Terug naar thema's",
                ["on"] = "aan",
                ["off"] = "uit",
                ["confirm"] = "Verwijderen bevestigen",
                ["deleteOff"] = "Pagina's 'uit' wissen",
                ["deleteAll"] = "Heel thema wissen",
                ["home"] = "HOME-menu",
                ["lock"] = "Vergrendelscherm",
                ["apps"] = "Alle software",
                ["set"] = "Systeeminstellingen",
                ["news"] = "Nieuws",
                ["user"] = "Gebruikerspagina",
                ["psl"] = "Gebruiker kiezen",
            },
            ["pt"] = new()
            {
                ["apply"] = "Aplicar este tema",
                ["back"] = "Voltar aos temas",
                ["on"] = "sim",
                ["off"] = "não",
                ["confirm"] = "Confirmar eliminação",
                ["deleteOff"] = "Apagar páginas «não»",
                ["deleteAll"] = "Apagar todo o tema",
                ["home"] = "Menu HOME",
                ["lock"] = "Ecrã de bloqueio",
                ["apps"] = "Todo o software",
                ["set"] = "Definições",
                ["news"] = "Notícias",
                ["user"] = "Página do utilizador",
                ["psl"] = "Escolher utilizador",
            },
            ["pt-BR"] = new()
            {
                ["apply"] = "Aplicar este tema",
                ["back"] = "Voltar aos temas",
                ["on"] = "sim",
                ["off"] = "não",
                ["confirm"] = "Confirmar exclusão",
                ["deleteOff"] = "Excluir páginas «não»",
                ["deleteAll"] = "Excluir todo o tema",
                ["home"] = "Menu HOME",
                ["lock"] = "Tela de bloqueio",
                ["apps"] = "Todos os softwares",
                ["set"] = "Configurações",
                ["news"] = "Notícias",
                ["user"] = "Página do usuário",
                ["psl"] = "Escolher usuário",
            },
            ["ru"] = new()
            {
                ["apply"] = "Применить тему",
                ["back"] = "Назад к темам",
                ["on"] = "вкл.",
                ["off"] = "выкл.",
                ["confirm"] = "Подтвердить удаление",
                ["deleteOff"] = "Удалить выкл. страницы",
                ["deleteAll"] = "Удалить всю тему",
                ["home"] = "Меню HOME",
                ["lock"] = "Экран блокировки",
                ["apps"] = "Все программы",
                ["set"] = "Настройки системы",
                ["news"] = "Новости",
                ["user"] = "Страница пользователя",
                ["psl"] = "Выбор пользователя",
            },
            ["ja"] = new()
            {
                ["apply"] = "このテーマを適用",
                ["back"] = "テーマ一覧に戻る",
                ["on"] = "オン",
                ["off"] = "オフ",
                ["confirm"] = "削除を確定",
                ["deleteOff"] = "オフのページを削除",
                ["deleteAll"] = "テーマをすべて削除",
                ["home"] = "HOMEメニュー",
                ["lock"] = "ロック画面",
                ["apps"] = "すべてのソフト",
                ["set"] = "設定",
                ["news"] = "ニュース",
                ["user"] = "ユーザーページ",
                ["psl"] = "ユーザー選択",
            },
            ["ko"] = new()
            {
                ["apply"] = "이 테마 적용",
                ["back"] = "테마로 돌아가기",
                ["on"] = "켜짐",
                ["off"] = "꺼짐",
                ["confirm"] = "삭제 확인",
                ["deleteOff"] = "꺼진 페이지 삭제",
                ["deleteAll"] = "테마 전체 삭제",
                ["home"] = "HOME 메뉴",
                ["lock"] = "잠금 화면",
                ["apps"] = "모든 소프트웨어",
                ["set"] = "설정",
                ["news"] = "뉴스",
                ["user"] = "유저 페이지",
                ["psl"] = "유저 선택",
            },
            ["zh-Hans"] = new()
            {
                ["apply"] = "应用此主题",
                ["back"] = "返回主题",
                ["on"] = "开",
                ["off"] = "关",
                ["confirm"] = "确认删除",
                ["deleteOff"] = "删除关闭的页面",
                ["deleteAll"] = "删除整个主题",
                ["home"] = "HOME菜单",
                ["lock"] = "锁定画面",
                ["apps"] = "所有软件",
                ["set"] = "设置",
                ["news"] = "新闻",
                ["user"] = "用户页面",
                ["psl"] = "选择用户",
            },
            ["zh-Hant"] = new()
            {
                ["apply"] = "套用此主題",
                ["back"] = "返回主題",
                ["on"] = "開",
                ["off"] = "關",
                ["confirm"] = "確認刪除",
                ["deleteOff"] = "刪除關閉的頁面",
                ["deleteAll"] = "刪除整個主題",
                ["home"] = "HOME選單",
                ["lock"] = "鎖定畫面",
                ["apps"] = "所有軟體",
                ["set"] = "設定",
                ["news"] = "新聞",
                ["user"] = "使用者頁面",
                ["psl"] = "選擇使用者",
            },
            ["pl"] = new()
            {
                ["apply"] = "Zastosuj motyw",
                ["back"] = "Wróć do motywów",
                ["on"] = "wł.",
                ["off"] = "wył.",
                ["confirm"] = "Potwierdź usunięcie",
                ["deleteOff"] = "Usuń wyłączone strony",
                ["deleteAll"] = "Usuń cały motyw",
                ["home"] = "Menu HOME",
                ["lock"] = "Ekran blokady",
                ["apps"] = "Całe oprogramowanie",
                ["set"] = "Ustawienia",
                ["news"] = "Aktualności",
                ["user"] = "Strona użytkownika",
                ["psl"] = "Wybór użytkownika",
            },
            ["th"] = new()
            {
                ["apply"] = "ใช้ธีมนี้",
                ["back"] = "กลับไปที่ธีม",
                ["on"] = "เปิด",
                ["off"] = "ปิด",
                ["confirm"] = "ยืนยันการลบ",
                ["deleteOff"] = "ลบหน้าที่ปิด",
                ["deleteAll"] = "ลบทั้งธีม",
                ["home"] = "เมนู HOME",
                ["lock"] = "หน้าจอล็อก",
                ["apps"] = "ซอฟต์แวร์ทั้งหมด",
                ["set"] = "การตั้งค่า",
                ["news"] = "ข่าว",
                ["user"] = "หน้าผู้ใช้",
                ["psl"] = "เลือกผู้ใช้",
            },
        };

        /// <summary>The language of the system, for the texts of the pages of a theme.</summary>
        public static void SetLanguage(SystemState.SystemLanguage language) => _language = language switch
        {
            SystemState.SystemLanguage.French => "fr",
            SystemState.SystemLanguage.CanadianFrench => "fr",
            SystemState.SystemLanguage.German => "de",
            SystemState.SystemLanguage.Spanish => "es",
            SystemState.SystemLanguage.LatinAmericanSpanish => "es",
            SystemState.SystemLanguage.Italian => "it",
            SystemState.SystemLanguage.Dutch => "nl",
            SystemState.SystemLanguage.Portuguese => "pt",
            SystemState.SystemLanguage.BrazilianPortuguese => "pt-BR",
            SystemState.SystemLanguage.Russian => "ru",
            SystemState.SystemLanguage.Japanese => "ja",
            SystemState.SystemLanguage.Korean => "ko",
            SystemState.SystemLanguage.Chinese => "zh-Hans",
            SystemState.SystemLanguage.SimplifiedChinese => "zh-Hans",
            SystemState.SystemLanguage.Taiwanese => "zh-Hant",
            SystemState.SystemLanguage.TraditionalChinese => "zh-Hant",
            SystemState.SystemLanguage.Polish => "pl",
            SystemState.SystemLanguage.Thai => "th",
            _ => "en",
        };

        private static string Text(string key) =>
            (_texts.TryGetValue(_language, out var texts) && texts.TryGetValue(key, out string text)) ? text : _texts["en"][key];

        private static string TargetName(string target) => target is "home" or "lock" or "apps" or "set" or "news" or "user" or "psl" ? Text(target) : target;


        // The entries of the pages of a theme after its parts (added entry numbers).
        private static int DeleteOffRow => _menuParts.Count;
        private static int DeleteAllRow => _menuParts.Count + 1;

        /// <summary>
        /// The themes page is being built: the themes, their names and pictures as they are now
        /// (the parts the code uses for the entries it made are left as they are). Built by the
        /// system (not rebuilt for the pages of a theme): the themes again.
        /// </summary>
        internal static bool Refresh(KProcess process)
        {
            if (process != _process)
            {
                return false;
            }

            // Built by the system: the themes, or the "Ryujinx | Nextendo" section.
            _menuPack = null;
            _confirm = -1;

            try
            {
                QlaunchSettingsPage.Active = process.CpuMemory.Read<int>(_base + Cave + CodeSize + 0x3C0) == 1;
            }
            catch (Exception)
            {
                QlaunchSettingsPage.Active = false;
            }

            try
            {
                byte[] data = new byte[DataSize];

                WriteData(data);

                ulong at = _base + Cave + CodeSize;

                process.CpuMemory.Write(at, data.AsSpan(0, 8));
                process.CpuMemory.Write(at + 0x3DC, data.AsSpan(0x3DC, 4));
                process.CpuMemory.Write(at + 0x3E4, data.AsSpan(0x3E4, 4));
                process.CpuMemory.Write(at + 0x400, data.AsSpan(0x400, 0x1000));
                process.CpuMemory.Write(at + 0x1800, data.AsSpan(0x1800, 0x20 * MaxEntries));
                process.CpuMemory.Write(at + 0x2400, data.AsSpan(0x2400, 0x240));
                process.CpuMemory.Write(at + QlaunchSettingsPage.ValuesAt, data.AsSpan(QlaunchSettingsPage.ValuesAt, QlaunchSettingsPage.ValuesSize));
                process.CpuMemory.Write(at + QlaunchSettingsPage.BoxesAt, data.AsSpan(QlaunchSettingsPage.BoxesAt, QlaunchSettingsPage.BoxesSize));
                process.CpuMemory.Write(at + QlaunchSettingsPage.LabelsAt, data.AsSpan(QlaunchSettingsPage.LabelsAt, QlaunchSettingsPage.LabelsSize));
                process.CpuMemory.Write(at + 0x3F8, data.AsSpan(0x3F8, 4));
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.Loader, $"[theme] themes page not refreshed: {e.Message}");
            }

            return true;
        }

        private static bool _xDown;

        /// <summary>
        /// The pad's X button, as the emulator reads it: pressed on a theme of the page (the
        /// entry whose key node, button+0x828, is focused: flags +0x148 bits 0 and 1), its pages
        /// are shown in place of the themes.
        /// </summary>
        public static void OnPadX(bool down)
        {
            bool pressed = down && !_xDown;

            _xDown = down;

            KProcess process = _process;

            if (!pressed || process == null || _menuPack != null || QlaunchSettingsPage.Active)
            {
                return;
            }

            try
            {
                ulong data = _base + Cave + CodeSize;

                if (process.CpuMemory.Read<ulong>(data + 0x38) == 0)
                {
                    return; // the page is not built
                }

                int count = Math.Min(process.CpuMemory.Read<int>(data), MaxEntries);

                for (int i = 0; i < count; i++)
                {
                    ulong button = process.CpuMemory.Read<ulong>(data + 0x100 + 0x20 * (ulong)i + 0x10);

                    if (Focused(process, button))
                    {
                        OpenMenu(process, i);

                        return;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] X on the themes page: {e.Message}");
            }
        }

        private static bool _bDown;
        private static bool _bSwallowed;

        /// <summary>
        /// The pad's B button, as the emulator reads it: in the pages of a theme (focus on the
        /// page), back to the themes; the HOME Menu does not get it (it would leave the page).
        /// True while it is kept from the HOME Menu.
        /// </summary>
        public static bool OnPadB(bool down)
        {
            if (_bSwallowed)
            {
                _bSwallowed = down;

                return down;
            }

            bool pressed = down && !_bDown;

            _bDown = down;

            KProcess process = _process;

            // [Nextendo, HOME Menu work] Leaving the "Ryujinx | Nextendo" section with settings
            // changed that need a restart: the restart dialog first.
            if (pressed && process != null && QlaunchSettingsPage.Active && QlaunchSettingsPage.RestartPending)
            {
                try
                {
                    if (PageFocused(process))
                    {
                        QlaunchSettingsPage.AskRestart(process, _base + Cave + CodeSize);
                        _bSwallowed = true;

                        return true;
                    }
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceSet, $"[settings] restart dialog: {e.Message}");
                }
            }

            if (!pressed || process == null || _menuPack == null)
            {
                return false;
            }

            try
            {
                if (!PageFocused(process))
                {
                    return false;
                }

                BackToThemes(process);
                _bSwallowed = true;

                return true;
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] B on the themes page: {e.Message}");

                return false;
            }
        }

        // The focus on an entry of the page (its key node, button+0x828: flags +0x148 bits 0 and 1).
        private static bool Focused(KProcess process, ulong button) =>
            button != 0 && (process.CpuMemory.Read<byte>(button + 0x828 + 0x148) & 3) == 3;

        private static bool PageFocused(KProcess process)
        {
            ulong data = _base + Cave + CodeSize;
            ulong page = process.CpuMemory.Read<ulong>(data + 0x38);

            if (page == 0)
            {
                return false;
            }

            if (Focused(process, process.CpuMemory.Read<ulong>(page + 0xCD0)) || Focused(process, process.CpuMemory.Read<ulong>(page + 0xCF0)))
            {
                return true;
            }

            int count = Math.Min(process.CpuMemory.Read<int>(data + 0x3EC), MaxEntries);

            for (int i = 0; i < count; i++)
            {
                if (Focused(process, process.CpuMemory.Read<ulong>(data + 0x100 + 0x20 * (ulong)i + 0x10)))
                {
                    return true;
                }
            }

            return false;
        }

        // From the pages of a theme back to the themes, the focus on that theme.
        private static void BackToThemes(KProcess process)
        {
            int index = _packs.IndexOf(_menuPack);

            _menuPack = null;
            _confirm = -1;
            Rebuild(process, index >= 0 ? index + 2 : 0);
        }

        // The pages of a theme shown in place of the themes.
        private static void OpenMenu(KProcess process, int entry)
        {
            if (entry < 0 || entry >= _packs.Count)
            {
                return;
            }

            _menuPack = _packs[entry];
            _menuParts = NextendoThemes.PartsOf(_menuPack);

            HashSet<string> off = _menuPack == NextendoThemes.Chosen ? NextendoThemes.ExcludedTargets : [];

            _menuOn = _menuParts.Select(p => !off.Contains(p.Target)).ToArray();
            _confirm = -1;

            Logger.Info?.Print(LogClass.ServiceSet, $"[theme] pages of {_menuPack}: {string.Join(", ", _menuParts.Select(p => p.Target))}");

            Rebuild(process, 2);
        }

        // The page shown again (by the added code, once its keys are handled) with the entries as
        // they are now: the themes, or the pages of one.
        private static void Rebuild(KProcess process, int focus)
        {
            ulong at = _base + Cave + CodeSize;
            byte[] data = new byte[DataSize];

            WriteData(data);

            process.CpuMemory.Write(at, data.AsSpan(0, 8));
            process.CpuMemory.Write(at + 0x3DC, data.AsSpan(0x3DC, 4));
            process.CpuMemory.Write(at + 0x400, data.AsSpan(0x400, 0x1000));
            process.CpuMemory.Write(at + 0x1800, data.AsSpan(0x1800, 0x20 * MaxEntries));
            process.CpuMemory.Write(at + 0x2400, data.AsSpan(0x2400, 0x200));
            process.CpuMemory.Write(at + QlaunchSettingsPage.ValuesAt, data.AsSpan(QlaunchSettingsPage.ValuesAt, QlaunchSettingsPage.ValuesSize));
            process.CpuMemory.Write(at + 0x3E8, focus);
            process.CpuMemory.Write(at + 0x3D4, 1);
        }

        // An entry of the pages of a theme picked (0: apply, 1: back, then the parts and the deletions).
        private static void TakeMenuPick(KProcess process, int row)
        {
            int part = row - 2;

            if (row == 0)
            {
                string pack = _menuPack;
                HashSet<string> off = _menuParts.Where((p, i) => !_menuOn[i]).Select(p => p.Target).ToHashSet();

                _menuPack = null;

                if (_menuOn.Any(on => on))
                {
                    NextendoThemes.SetChoice(pack, off);
                    Logger.Info?.Print(LogClass.ServiceSet, $"[theme] HOME Menu theme: {pack} (pages left out: {string.Join(", ", off)}), the HOME Menu starts again");
                }
                else
                {
                    NextendoThemes.Chosen = null;
                    Logger.Info?.Print(LogClass.ServiceSet, "[theme] HOME Menu theme: no page kept, the system's, the HOME Menu starts again");
                }

                HomeMenuStatus.RequestPower(true);

                return;
            }

            if (row == 1)
            {
                BackToThemes(process);

                return;
            }

            if (part >= 0 && part < _menuParts.Count)
            {
                _menuOn[part] = !_menuOn[part];
                _confirm = -1;
                Rebuild(process, row);

                return;
            }

            if (part == DeleteOffRow || part == DeleteAllRow)
            {
                if (_confirm != part)
                {
                    _confirm = part;
                    Rebuild(process, row);

                    return;
                }

                _confirm = -1;

                bool all = part == DeleteAllRow || _menuOn.All(on => !on);
                List<string> files = _menuParts.Where((p, i) => all || !_menuOn[i]).Select(p => p.File).ToList();
                string pack = _menuPack;
                bool inUse = pack == NextendoThemes.Current;

                NextendoThemes.Delete(pack, files);

                Logger.Info?.Print(LogClass.ServiceSet, $"[theme] {pack}: {files.Count} page(s) deleted");

                _menuParts = NextendoThemes.PartsOf(pack);

                if (all || _menuParts.Count == 0)
                {
                    _menuPack = null;

                    if (inUse)
                    {
                        NextendoThemes.Chosen = null;
                        HomeMenuStatus.RequestPower(true);

                        return;
                    }

                    Rebuild(process, 0);

                    return;
                }

                _menuOn = _menuParts.Select(_ => true).ToArray();

                if (inUse)
                {
                    HomeMenuStatus.RequestPower(true);

                    return;
                }

                Rebuild(process, 2);
            }
        }

        // The page's "build" method in its vtable (relocated at start: the base is added to it).
        private const uint BuildSlot = 0x10D4020;

        /// <summary>Room to add after the image of that program (0 when nothing is added).</summary>
        public static uint ExtraSize(string buildId, uint imageSize)
        {
            if (!Enabled(buildId))
            {
                return 0;
            }

            if (imageSize != Cave)
            {
                Logger.Warning?.Print(LogClass.Loader, $"[theme] HOME Menu image of 0x{imageSize:X} bytes, 0x{Cave:X} expected: themes page left as it is");

                return 0;
            }

            return Size;
        }

        private static bool Enabled(string buildId) =>
            buildId != null && buildId.StartsWith(BuildId, StringComparison.OrdinalIgnoreCase);

        /// <summary>Puts the code and the data after the image, and points the page at them.</summary>
        internal static void Apply(KProcess process, ulong baseAddress)
        {
            // [Nextendo] the HOME Menu as the console has it: no themes page, no added section
            if (Ryujinx.Common.Configuration.NextendoHomeMenuVanilla.Enabled)
            {
                return;
            }

            byte[] code = Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/Themes/Qlaunch/theme_page.bin");
            byte[] data = new byte[DataSize];

            WriteData(data);

            process.CpuMemory.Write(baseAddress + Cave, code);
            process.CpuMemory.Write(baseAddress + Cave + CodeSize, data);

            // Not executable yet: at start the program takes every executable region for a module
            // of its own (and stops on one that is not). It becomes so once the program runs.
            // Read only, a block of its own (next to the program's writable data it would be taken
            // for a part of it).
            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave, CodeSize, KMemoryPermission.Read);
            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave + CodeSize, DataSize, KMemoryPermission.ReadAndWrite);

            lock (_pending)
            {
                _pending[process] = baseAddress;
            }

            // The page's methods to ours (entry points at the start of the code, 4 bytes each). The
            // slots hold offsets the program relocates itself (the base is added to them).
            uint[] slots = [BuildSlot, BuildSlot + 0x08, BuildSlot + 0x40, BuildSlot + 0x48]; // build, refresh, deactivate, destroy

            for (int i = 0; i < slots.Length; i++)
            {
                byte[] slot = new byte[8];

                BinaryPrimitives.WriteUInt64LittleEndian(slot, Cave + (uint)(4 * i));
                process.CpuMemory.Write(baseAddress + slots[i], slot);
            }

            // [Nextendo, HOME Menu work] +0x420: the page uses select boxes (the "Ryujinx |
            // Nextendo" section), its base then makes their request holder (+0xBB8).
            byte[] selectBoxes = new byte[8];

            BinaryPrimitives.WriteUInt64LittleEndian(selectBoxes, Cave + 0x38);
            process.CpuMemory.Write(baseAddress + BuildSlot + 0x60, selectBoxes);

            // The restart dialog's callbacks: their vtables (as the system's lambdas': its
            // type's function, two that do nothing, copy, call), in the data page.
            ulong[] buttons = [baseAddress + 0x439010, baseAddress + 0x26F0, baseAddress + 0x26F0, baseAddress + Cave + 0x3C, baseAddress + Cave + 0x40, 0];
            ulong[] dialog = [baseAddress + 0x439010, baseAddress + 0x26F0, baseAddress + 0x26F0, baseAddress + Cave + 0x3C, baseAddress + Cave + 0x44, 0];

            for (int i = 0; i < 6; i++)
            {
                process.CpuMemory.Write(baseAddress + Cave + CodeSize + 0x6000 + (ulong)(8 * i), buttons[i]);
                process.CpuMemory.Write(baseAddress + Cave + CodeSize + 0x6030 + (ulong)(8 * i), dialog[i]);
            }

            // [Nextendo, HOME Menu work] The virtual game cards page's welcome, seen already.
            process.CpuMemory.Write(baseAddress + Cave + CodeSize + VgcWelcomeSeenAt, (byte)(NextendoVirtualCards.WelcomeSeen ? 1 : 0));

            // The page's own code, changed to know the added entries (Qlaunch/build.py).
            byte[] patches = Ryujinx.Common.EmbeddedResources.Read("Ryujinx.HLE/HOS/Themes/Qlaunch/theme_page.patches");
            int count = BinaryPrimitives.ReadInt32LittleEndian(patches);

            for (int i = 0, at = 4; i < count; i++)
            {
                uint address = BinaryPrimitives.ReadUInt32LittleEndian(patches.AsSpan(at));
                int length = BinaryPrimitives.ReadInt32LittleEndian(patches.AsSpan(at + 4));

                process.CpuMemory.Write(baseAddress + address, patches.AsSpan(at + 8, length));
                at += 8 + length;
            }

            _process = process;
            _base = baseAddress;

            Logger.Info?.Print(LogClass.Loader, $"[theme] HOME Menu themes page: {code.Length} bytes of code added");
        }

        private static readonly Dictionary<KProcess, ulong> _pending = [];

        private static KProcess _process;
        private static ulong _base;
        private static List<string> _packs = [];

        /// <summary>
        /// The HOME Menu has set its colour set: an entry of its themes page was picked. A theme
        /// different from the one in use is kept, and the HOME Menu starts again with it (its
        /// layouts are made when it starts). True when that happens.
        /// </summary>
        public static bool TakePick()
        {
            KProcess process = _process;

            if (process == null)
            {
                return false;
            }

            if (QlaunchSettingsPage.Active)
            {
                try
                {
                    QlaunchSettingsPage.Open(process, _base + Cave + CodeSize, process.CpuMemory.Read<int>(_base + Cave + CodeSize + 0x3D8));
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceSet, $"[settings] HOME Menu settings: {e.Message}");
                }

                return false;
            }

            if (_menuPack != null)
            {
                try
                {
                    TakeMenuPick(process, process.CpuMemory.Read<int>(_base + Cave + CodeSize + 0x3D8));
                }
                catch (Exception e)
                {
                    Logger.Warning?.Print(LogClass.ServiceSet, $"[theme] pages of a theme: {e.Message}");
                }

                return false;
            }

            ulong at = _base + Cave + CodeSize + 0x30;
            int picked;

            try
            {
                picked = process.CpuMemory.Read<int>(at);
                process.CpuMemory.Write(at, -2);
            }
            catch (Exception)
            {
                return false;
            }

            if (picked < -1 || picked >= _packs.Count)
            {
                return false;
            }

            string theme = picked < 0 ? null : _packs[picked];

            if (theme == NextendoThemes.Current)
            {
                return false;
            }

            NextendoThemes.Chosen = theme;

            Logger.Info?.Print(LogClass.ServiceSet, $"[theme] HOME Menu theme: {theme ?? "the system's"}, the HOME Menu starts again");

            HomeMenuStatus.RequestPower(true);

            return true;
        }

        /// <summary>
        /// [Nextendo, HOME Menu work] A choice made in a select box of the "Ryujinx | Nextendo"
        /// section (the added code says so): the setting takes it, the entries show it.
        /// </summary>
        internal static bool TakeSettingsResult(KProcess process)
        {
            if (process != _process)
            {
                return false;
            }

            try
            {
                int row = QlaunchSettingsPage.TakeResult(process, _base + Cave + CodeSize);

                if (row >= 0)
                {
                    Rebuild(process, row);
                }
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[settings] HOME Menu settings: {e.Message}");
            }

            return true;
        }

        // The virtual game cards page's welcome seen (read by the added code: data page).
        private const ulong VgcWelcomeSeenAt = 0x6300;

        /// <summary>[Nextendo, HOME Menu work] The virtual game cards page's welcome was seen.</summary>
        internal static void SetVgcWelcomeSeen()
        {
            KProcess process = _process;

            try
            {
                process?.CpuMemory.Write(_base + Cave + CodeSize + VgcWelcomeSeenAt, (byte)1);
            }
            catch (Exception)
            {
                // the HOME Menu is gone
            }
        }

        /// <summary>[Nextendo, HOME Menu work] The restart dialog of the settings answered.</summary>
        internal static bool TakeRestartAnswer(KProcess process)
        {
            if (process != _process)
            {
                return false;
            }

            try
            {
                if (QlaunchSettingsPage.TakeRestartAnswer(process, _base + Cave + CodeSize))
                {
                    // The values as they were, shown once the dialog is closed: asked while it is
                    // still open, the page took the focus back and the dialog stayed on screen.
                    System.Threading.Tasks.Task.Delay(700).ContinueWith(_ =>
                    {
                        try
                        {

                            if (process == _process && QlaunchSettingsPage.Active)
                            {
                                Rebuild(process, -1);
                            }
                        }
                        catch (Exception)
                        {
                            // the HOME Menu is gone
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Logger.Warning?.Print(LogClass.ServiceSet, $"[settings] restart dialog: {e.Message}");
            }

            return true;
        }

        /// <summary>The program has started (its modules are known): the added code may run.</summary>
        internal static void Activate(KProcess process)
        {
            ulong baseAddress;

            lock (_pending)
            {
                if (!_pending.Remove(process, out baseAddress))
                {
                    return;
                }
            }

            process.MemoryManager.SetProcessMemoryPermission(baseAddress + Cave, CodeSize, KMemoryPermission.ReadAndExecute);

            Logger.Info?.Print(LogClass.Loader, "[theme] HOME Menu themes page: code active");
        }

        // The data page: what the code reads (see theme_page.s).
        private static void WriteData(byte[] data)
        {
            List<string> packs = NextendoThemes.Packs();

            _packs = packs;

            if (QlaunchSettingsPage.Active)
            {
                QlaunchSettingsPage.Write(data, EntriesToMake(Math.Min(packs.Count, MaxEntries)), RefreshSignal, _base + Cave + CodeSize);

                return;
            }

            if (_menuPack != null)
            {
                WriteMenu(data);

                return;
            }

            int count = Math.Min(packs.Count, MaxEntries);

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), count);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), packs.IndexOf(NextendoThemes.Current ?? "") is int chosen and < MaxEntries ? chosen : -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2); // nothing picked yet
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), EntriesToMake(count));
            Encoding.ASCII.GetBytes(RefreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1); // no entry to focus
            Encoding.ASCII.GetBytes(CustomizeLabel).CopyTo(data.AsSpan(0x2600));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3F8), 0); // the kind of footer text (0, as B's; 1 is A's)

            for (int i = 0; i < count; i++)
            {
                byte[] name = Encoding.Unicode.GetBytes(DisplayName(packs[i]));

                name.AsSpan(0, Math.Min(name.Length, 0xFE)).CopyTo(data.AsSpan(0x400 + 0x100 * i));

                byte[] picture = NextendoThemes.Picture(packs[i]);

                _pictures[i] ??= new byte[PictureSize];

                lock (_pictures[i])
                {
                    Array.Clear(_pictures[i]);

                    if (picture != null && picture.Length <= PictureSize)
                    {
                        picture.CopyTo(_pictures[i], 0);
                        Encoding.ASCII.GetBytes(PicturePath(i)).CopyTo(data.AsSpan(0x1800 + 0x20 * i));
                    }
                }
            }

            Logger.Info?.Print(LogClass.Loader, $"[theme] themes for the HOME Menu page: {packs.Count} ({string.Join(", ", packs)})");
        }

        // The entries made with the page: one per theme, and enough for the pages of any theme
        // (7 parts and the 2 deletions) and one never shown, checked while they are; those not
        // needed are not shown.
        private static int EntriesToMake(int themes) => Math.Min(Math.Max(themes, 10), MaxEntries);

        private static void WriteName(byte[] data, int at, string name)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(name);

            bytes.AsSpan(0, Math.Min(bytes.Length, 0xFE)).CopyTo(data.AsSpan(at));
        }

        // The pages of a theme: entry 0 applies them, 1 goes back, then the parts (A: applied or
        // not) and the deletions (a second press confirms).
        private static void WriteMenu(byte[] data)
        {
            int count = Math.Min(_menuParts.Count + 2, MaxEntries);

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0), count);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), -1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x30), -2);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3DC), 1);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E4), EntriesToMake(Math.Min(_packs.Count, MaxEntries)));
            Encoding.ASCII.GetBytes(RefreshSignal).CopyTo(data.AsSpan(0x3F0));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3E8), -1);

            WriteName(data, 0x2400, Text("apply"));
            WriteName(data, 0x2500, Text("back"));

            for (int i = 0; i < _menuParts.Count && i < count; i++)
            {
                string state = _menuOn[i] ? Text("on") : Text("off");

                WriteName(data, 0x400 + 0x100 * i, $"{TargetName(_menuParts[i].Target)} : {state}");
            }

            if (DeleteOffRow < count)
            {
                WriteName(data, 0x400 + 0x100 * DeleteOffRow, _confirm == DeleteOffRow
                    ? Text("confirm")
                    : Text("deleteOff"));
            }

            if (DeleteAllRow < count)
            {
                WriteName(data, 0x400 + 0x100 * DeleteAllRow, _confirm == DeleteAllRow
                    ? Text("confirm")
                    : Text("deleteAll"));
            }

            // The entries of the parts show the wallpaper of their page (paths at 0x1800, as the
            // themes, in files of their own).
            for (int i = 0; i < MaxEntries; i++)
            {
                _pagePictures[i] ??= new byte[PictureSize];

                lock (_pagePictures[i])
                {
                    Array.Clear(_pagePictures[i]);

                    if (i < _menuParts.Count)
                    {
                        byte[] thumb = NextendoThemes.PartThumbnail(_menuParts[i].File);

                        if (thumb != null && thumb.Length <= PictureSize)
                        {
                            thumb.CopyTo(_pagePictures[i], 0);
                            Encoding.ASCII.GetBytes(PagePicturePath(i)).CopyTo(data.AsSpan(0x1800 + 0x20 * i));
                        }
                    }
                }
            }
        }
    }
}
