using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// Nextendo Network linked account. Written by the in-app "Connexion Nextendo
    /// Network" dialog, read by the Account service (ManagerServer) so the NEX
    /// login presents the account's PERSISTENT principal id (PID) instead of the
    /// 0xcafe stub — this is what makes "log in with your account = play online as
    /// you" work. Stored as a tiny key=value file (no JSON, trimming/AOT-safe).
    /// </summary>
    public static class NextendoAccount
    {
        private static readonly object _lock = new();
        private static bool _loaded;

        // ⚠️ Le PID et le jeton NEX sont stockes ici a l etat BRUT, et lus par les
        // proprietes publiques ci-dessous, qui les taisent en mode « serveur
        // personnalise ». La persistance ecrit les champs bruts, jamais les proprietes :
        // sinon enregistrer pendant ce mode effacerait le compte du disque.
        private static ulong _pid;
        private static string _nexToken = "";

        /// <summary>Numero de compte Nextendo. Zero quand aucun compte n est utilisable,
        /// y compris parce que le mode « serveur personnalise » est actif.</summary>
        public static ulong Pid => NextendoServerOverride.HorsNextendo ? 0 : _pid;

        public static string Username { get; private set; } = "";
        public static string FriendCode { get; private set; } = "";

        /// <summary>
        /// Le jeton signe qui prouve l identite du compte.
        ///
        /// ⚠️ IL NE DOIT JAMAIS SORTIR EN MODE « SERVEUR PERSONNALISE ». Le jeu le
        /// recopie dans le login NEX (claim « nnex ») : sans cette garde, se connecter au
        /// serveur d un inconnu lui livrerait un jeton signe valable sur NOS serveurs.
        /// </summary>
        public static string NexToken => NextendoServerOverride.HorsNextendo ? "" : _nexToken;

        private static bool _isGuest;

        /// <summary>True when the linked identity is a no-account GUEST profile (created by
        /// the beta quick-start via /api/guest) rather than a full registered account. A guest
        /// has a real persistent PID/friend code (online, friends and sync all work) but no
        /// e-mail/password; it can be renamed freely and later upgraded to a full account.</summary>
        public static bool IsGuest
        {
            get { EnsureLoaded(); return _isGuest; }
        }

        private static string _profileUserId = "";

        /// <summary>The Ryujinx user-profile UserId bound to this Nextendo account, so
        /// we reuse the same local profile instead of creating duplicates on each login.</summary>
        public static string ProfileUserId
        {
            get { EnsureLoaded(); return _profileUserId; }
        }

        private static string _miiData = "";

        /// <summary>Base64 of the account's Mii (Switch StoreData, 0x44 bytes), mirrored
        /// locally so the exact same Mii can be removed from the Mii database on logout.</summary>
        public static string MiiData
        {
            get { EnsureLoaded(); return _miiData; }
        }

        /// <summary>
        /// Le compte est-il utilisable MAINTENANT ?
        ///
        /// ⚠️ Le mode « serveur personnalisé » rend ceci faux sans effacer quoi que ce soit :
        /// le compte reste sur le disque et revient dès qu'on décoche la case. C'est ICI que
        /// la coupure est faite, et pas dans chaque appelant, parce que tout ce qui touche à
        /// Nextendo passe déjà par cette question — synchronisation des sauvegardes, amis,
        /// présence, historique, identité NEX présentée au jeu. Une coupure éparpillée aurait
        /// laissé passer celui qu'on aurait oublié.
        /// </summary>
        public static bool IsLinked
        {
            get
            {
                if (NextendoServerOverride.HorsNextendo)
                {
                    return false;
                }

                EnsureLoaded();

                return _pid != 0;
            }
        }

        /// <summary>Runtime-only (not persisted): set true at game launch when the running
        /// game is a Nextendo title on an UNSUPPORTED version. While true, the NEX login
        /// presents the anonymous stub instead of the account PID, so the gated server
        /// refuses online — enforcing the required game version.</summary>
        public static bool OnlineBlocked { get; set; }

        private static string FilePath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_account.txt");

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;

                try
                {
                    if (!File.Exists(FilePath))
                    {
                        return;
                    }

                    ReadLocked(FilePath);

                    // Un compte connecte avant le multi-comptes n'a pas encore sa copie dans la liste.
                    if (_pid != 0 && !File.Exists(StoredPath(_pid)))
                    {
                        WriteFileLocked();
                    }
                }
                catch
                {
                    // Corrupt/unreadable file -> treat as not linked.
                    _pid = 0;
                }
            }
        }

        private static void ReadLocked(string path)
        {
            _pid = 0;
            Username = "";
            FriendCode = "";
            _nexToken = "";
            _profileUserId = "";
            _miiData = "";
            _isGuest = false;

            foreach (string line in File.ReadAllLines(path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = line[..eq].Trim();
                string val = line[(eq + 1)..].Trim();

                switch (key)
                {
                    case "pid": ulong.TryParse(val, out ulong p); _pid = p; break;
                    case "username": Username = val; break;
                    case "friend_code": FriendCode = val; break;
                    case "nex_token": _nexToken = val; break;
                    case "profile_user_id": _profileUserId = val; break;
                    case "mii_data": _miiData = val; break;
                    case "is_guest": _isGuest = val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                }
            }
        }

        // Plusieurs comptes Nextendo dans un seul emulateur.
        //
        // nextendo_account.txt reste LE compte actif : tout l'emulateur (NEX, amis, sauvegardes,
        // presence) lit le compte actif et rien d'autre. Chaque compte connecte a en plus sa copie
        // dans nextendo_accounts/<pid>.txt ; changer de compte recopie l'un d'eux a la place du
        // compte actif. Un meme compte n'a qu'un fichier (la cle est son PID) : se reconnecter
        // avec lui le met a jour, jamais un second exemplaire.

        /// <summary>Un compte Nextendo connecte sur cet emulateur.</summary>
        public sealed record Stored(ulong Pid, string Username, string FriendCode, string ProfileUserId, bool IsGuest);

        private static string StoreDir => Path.Combine(AppDataManager.BaseDirPath, "nextendo_accounts");

        private static string StoredPath(ulong pid) => Path.Combine(StoreDir, pid + ".txt");

        /// <summary>Raised after the active account changed (another account chosen, one
        /// signed out).</summary>
        public static event Action ActiveChanged;

        /// <summary>Every Nextendo account connected here, the active one first.</summary>
        public static IReadOnlyList<Stored> All
        {
            get
            {
                EnsureLoaded();

                List<Stored> list = [];

                lock (_lock)
                {
                    try
                    {
                        if (Directory.Exists(StoreDir))
                        {
                            foreach (string file in Directory.GetFiles(StoreDir, "*.txt"))
                            {
                                Dictionary<string, string> v = [];

                                foreach (string line in File.ReadAllLines(file))
                                {
                                    int eq = line.IndexOf('=');
                                    if (eq > 0)
                                    {
                                        v.TryAdd(line[..eq].Trim(), line[(eq + 1)..].Trim());
                                    }
                                }

                                if (!v.TryGetValue("pid", out string ps) || !ulong.TryParse(ps, out ulong pid) || pid == 0)
                                {
                                    continue;
                                }

                                list.Add(new Stored(pid,
                                    v.GetValueOrDefault("username", ""),
                                    v.GetValueOrDefault("friend_code", ""),
                                    v.GetValueOrDefault("profile_user_id", ""),
                                    v.GetValueOrDefault("is_guest", "0") == "1"));
                            }
                        }
                    }
                    catch
                    {
                        // an unreadable list still leaves the active account
                    }

                    if (_pid != 0 && list.All(a => a.Pid != _pid))
                    {
                        list.Add(new Stored(_pid, Username, FriendCode, _profileUserId, _isGuest));
                    }

                    ulong active = _pid;

                    return [.. list.OrderByDescending(a => a.Pid == active).ThenBy(a => a.Username, StringComparer.OrdinalIgnoreCase)];
                }
            }
        }

        /// <summary>Number of Nextendo accounts connected (0 in "custom server" mode).</summary>
        public static int Count => NextendoServerOverride.HorsNextendo ? 0 : All.Count;

        /// <summary>Makes one of the connected accounts the active one. False if it is not here.</summary>
        public static bool Activate(ulong pid)
        {
            lock (_lock)
            {
                EnsureLoaded();

                if (pid == _pid)
                {
                    return _pid != 0;
                }

                if (pid == 0 || !File.Exists(StoredPath(pid)))
                {
                    return false;
                }

                try
                {
                    if (_pid != 0)
                    {
                        WriteFileLocked(); // the account left keeps its copy, up to date
                    }

                    ReadLocked(StoredPath(pid));
                    _loaded = true;
                    WriteFileLocked();
                }
                catch
                {
                    return false;
                }
            }

            try { ActiveChanged?.Invoke(); } catch { /* best effort */ }

            return true;
        }

        /// <summary>Activates the account bound to this local profile, if one is. Called when a
        /// game opens a user: the user picked decides the account, as on a console where each
        /// user carries their own account.</summary>
        public static bool ActivateForProfile(string userId)
        {
            if (string.IsNullOrEmpty(userId) || NextendoServerOverride.HorsNextendo)
            {
                return false;
            }

            Stored match = All.FirstOrDefault(a => string.Equals(a.ProfileUserId, userId, StringComparison.OrdinalIgnoreCase));

            return match != null && Activate(match.Pid);
        }

        /// <summary>True when this local profile is bound to a connected account other than the
        /// active one (so it must never be reused or renamed for another account).</summary>
        public static bool BelongsToOtherAccount(string userId)
        {
            EnsureLoaded();

            ulong active = _pid;

            return !string.IsNullOrEmpty(userId) && All.Any(a => a.Pid != active && string.Equals(a.ProfileUserId, userId, StringComparison.OrdinalIgnoreCase));
        }

        public static void Save(ulong pid, string username, string friendCode, string nexToken, bool isGuest = false)
        {
            lock (_lock)
            {
                EnsureLoaded(); // preserve an existing profile binding for the same account
                // Comparaison sur le champ BRUT : la propriete Pid rend 0 en mode « serveur
                // personnalise », ce qui ferait croire a un changement de compte et delierait
                // le profil local a tort.
                if (pid != _pid)
                {
                    // Another account signs in: the one active until now stays connected (its
                    // copy is refreshed), and the new one takes back its own profile + Mii if it
                    // was already connected here.
                    if (_pid != 0)
                    {
                        WriteFileLocked();
                    }

                    _profileUserId = "";
                    _miiData = "";

                    if (pid != 0 && File.Exists(StoredPath(pid)))
                    {
                        try
                        {
                            ReadLocked(StoredPath(pid));
                        }
                        catch
                        {
                            _profileUserId = "";
                            _miiData = "";
                        }
                    }
                }
                // The local profile this account used before (a session that expired, a
                // logout): signing in again takes it back, with its saves, as a console asks
                // to sign in again to the same user rather than making a new one.
                if (string.IsNullOrEmpty(_profileUserId) && pid != 0)
                {
                    _profileUserId = LinkedProfile(pid);
                }
                _pid = pid;
                Username = username ?? "";
                FriendCode = friendCode ?? "";
                _nexToken = nexToken ?? "";
                _isGuest = isGuest;
                _loaded = true;
                WriteFileLocked();
            }
        }

        /// <summary>Binds the Ryujinx user-profile created/used for this account.</summary>
        public static void SetProfileUserId(string userId)
        {
            lock (_lock)
            {
                EnsureLoaded();
                _profileUserId = userId ?? "";
                WriteFileLocked();
            }
        }

        /// <summary>Mirrors the account's Mii (base64 StoreData) locally for logout removal.</summary>
        public static void SetMiiData(string miiData)
        {
            lock (_lock)
            {
                EnsureLoaded();
                _miiData = miiData ?? "";
                WriteFileLocked();
            }
        }

        // pid=profile lines: which local profile each Nextendo account was bound to. Kept apart
        // from the session, so that losing the session never loses the link.
        private static string LinksPath => Path.Combine(AppDataManager.BaseDirPath, "nextendo_profile_links.txt");

        private static string LinkedProfile(ulong pid)
        {
            try
            {
                if (File.Exists(LinksPath))
                {
                    foreach (string line in File.ReadAllLines(LinksPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && ulong.TryParse(line[..eq].Trim(), out ulong p) && p == pid)
                        {
                            return line[(eq + 1)..].Trim();
                        }
                    }
                }
            }
            catch
            {
                // no link known
            }

            return "";
        }

        private static void RememberLinkLocked()
        {
            if (_pid == 0 || string.IsNullOrEmpty(_profileUserId) || LinkedProfile(_pid) == _profileUserId)
            {
                return;
            }

            try
            {
                System.Collections.Generic.List<string> lines = File.Exists(LinksPath) ? [.. File.ReadAllLines(LinksPath)] : [];

                lines.RemoveAll(l => l.StartsWith(_pid + "=", StringComparison.Ordinal));
                lines.Add($"{_pid}={_profileUserId}");
                File.WriteAllLines(LinksPath, lines);
            }
            catch
            {
                // best effort
            }
        }

        private static void WriteFileLocked()
        {
            RememberLinkLocked();

            try
            {
                // ⚠️ Les champs BRUTS, jamais les propriétés. Pid et NexToken se taisent en mode
                // « serveur personnalisé » : les écrire ici réécrirait le fichier avec pid=0 et un
                // jeton vide, c'est-à-dire DÉTRUIRAIT le compte sur le disque — au premier
                // SetProfileUserId ou SetMiiData venu, alors que l'utilisateur voulait seulement
                // jouer ailleurs un moment.
                string text = $"pid={_pid}\nusername={Username}\nfriend_code={FriendCode}\nnex_token={_nexToken}\nprofile_user_id={_profileUserId}\nmii_data={_miiData}\nis_guest={(_isGuest ? "1" : "0")}\n";

                File.WriteAllText(FilePath, text);

                if (_pid != 0)
                {
                    Directory.CreateDirectory(StoreDir);
                    File.WriteAllText(StoredPath(_pid), text);
                }
            }
            catch
            {
                // Best effort; the in-memory values still apply this session.
            }
        }

        /// <summary>Signs out the ACTIVE account: it leaves the list, and another connected
        /// account, if any, becomes the active one.</summary>
        public static void Clear()
        {
            ulong next;

            lock (_lock)
            {
                EnsureLoaded();

                try
                {
                    if (_pid != 0 && File.Exists(StoredPath(_pid)))
                    {
                        File.Delete(StoredPath(_pid));
                    }
                }
                catch
                {
                    // ignore
                }

                ulong gone = _pid;
                next = All.Select(a => a.Pid).FirstOrDefault(p => p != gone);

                _pid = 0;
                Username = "";
                FriendCode = "";
                _nexToken = "";
                _profileUserId = "";
                _miiData = "";
                _isGuest = false;
                _loaded = true;

                try
                {
                    if (File.Exists(FilePath))
                    {
                        File.Delete(FilePath);
                    }
                }
                catch
                {
                    // ignore
                }
            }

            if (next == 0 || !Activate(next))
            {
                try { ActiveChanged?.Invoke(); } catch { /* best effort */ }
            }
        }
    }
}
