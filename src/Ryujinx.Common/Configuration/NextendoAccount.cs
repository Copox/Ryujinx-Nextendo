using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Ryujinx.Common.Logging;

namespace Ryujinx.Common.Configuration
{
    public enum NextendoCredentialStorageState
    {
        None, LegacyPending, Protected, Unsupported, Conflict, IdentityMismatch, Rejected, Unavailable, RecoveryRequired, Failed,
    }

    /// <summary>
    /// Nextendo Network linked account. Written by the in-app "Connexion Nextendo
    /// Network" dialog, read by the Account service (ManagerServer) so the NEX
    /// login presents the account's PERSISTENT principal id (PID) instead of the
    /// 0xcafe stub — this is what makes "log in with your account = play online as
    /// you" work. Persisted with operating-system protection; legacy migration runs automatically in the background.
    /// </summary>
    public static class NextendoAccount
    {
        private static readonly object _lock = new();
        private static bool _loaded;
        private static bool _identityValid;
        private static bool _canProtect;
        private static string _activeHash;
        private static Task _migrationTask;
        public static NextendoCredentialStorageState BackgroundMigrationIssue { get; private set; }
        private static NextendoCredentialStorageState _storageState;
        public static NextendoCredentialStorageState StorageState
        {
            get { lock (_lock) { EnsureLoaded(); return NextendoTextPaths.HasPendingCredentialCopy(AppDataManager.BaseDirPath)
                    ? NextendoCredentialStorageState.RecoveryRequired : NextendoProfileLinks.Issue(AppDataManager.BaseDirPath) != NextendoCredentialStorageState.None
                    ? NextendoProfileLinks.Issue(AppDataManager.BaseDirPath) : _storageState; } }
        }
        private static long _generation;
        internal static NextendoIdentityAuthority Authority { get; set; } = new();

        public static bool CredentialPersistenceFailed { get; private set; }

        /// <summary>A delayed rejection must never invalidate a different active account.</summary>
        public static bool RejectCredential(string token)
        {
            lock (_lock)
            {
                EnsureLoaded();
                if (string.IsNullOrEmpty(token) || token != _nexToken)
                {
                    return false;
                }

                _identityValid = false;
                RecordRejectionLocked(NextendoCredentialStorageState.Rejected);
                _nexToken = "";
                _generation++;
            }

            try { ActiveChanged?.Invoke(); } catch { /* The UI may not be ready. */ }
            return true;
        }

        /// <summary>Returns one coherent local session without network access or periodic writes.</summary>
        public static NextendoIdentitySnapshot GetIdentitySnapshot()
        {
            if (NextendoServerOverride.HorsNextendo)
            {
                return new(0, "", "", -1);
            }

            lock (_lock)
            {
                EnsureLoaded();
                return _identityValid ? new(_pid, _nexToken, _profileUserId, _generation) : new(0, "", "", _generation);
            }
        }

        /// <summary>Validates a legacy credential once before protecting it. Availability
        /// failures preserve the existing session; confirmed identity conflicts require sign-in.</summary>
        public static async Task<NextendoCredentialStorageState> ProtectLegacyAsync()
        {
            string token;
            ulong pid;
            long generation;
            lock (_lock)
            {
                EnsureLoaded();
                if (NextendoTextPaths.HasPendingCredentialCopy(AppDataManager.BaseDirPath))
                    return _storageState = NextendoCredentialStorageState.RecoveryRequired;
                if (NextendoServerOverride.HorsNextendo || !_identityValid || _canProtect)
                    return _storageState;
                if (!OperatingSystem.IsWindows()) return _storageState = NextendoCredentialStorageState.Unsupported;
                token = _nexToken;
                pid = _pid;
                generation = _generation;
            }

            var resolution = await Authority.ResolveAsync(token).ConfigureAwait(false);
            lock (_lock)
            {
                if (generation != _generation || token != _nexToken || NextendoServerOverride.HorsNextendo)
                    return _storageState;
                if (resolution.Identity == null)
                {
                    if (resolution.Rejected)
                    {
                        _identityValid = false;
                        _generation++;
                        RecordRejectionLocked(NextendoCredentialStorageState.Rejected);
                        return _storageState;
                    }
                    return _storageState = NextendoCredentialStorageState.Unavailable;
                }
                if (resolution.Identity.Pid != pid)
                {
                    _identityValid = false;
                    _generation++;
                    RecordRejectionLocked(NextendoCredentialStorageState.IdentityMismatch);
                    return _storageState;
                }

                _canProtect = true;
                if (!WriteFileLocked()) _canProtect = false;
                return _storageState;
            }
        }

        /// <summary>Schedules one background pass for active and inactive legacy accounts.
        /// Repeated startup/account-change requests share the same pass.</summary>
        public static Task MigrateLegacyAccountsAsync()
        {
            lock (_lock)
            {
                if (_migrationTask == null || _migrationTask.IsCompleted)
                    _migrationTask = Task.Run(MigrateLegacyAccountsCoreAsync);
                return _migrationTask;
            }
        }

        private static bool NeedsAttention(NextendoCredentialStorageState state) => state is
            NextendoCredentialStorageState.Conflict or NextendoCredentialStorageState.IdentityMismatch or
            NextendoCredentialStorageState.Rejected or NextendoCredentialStorageState.RecoveryRequired or NextendoCredentialStorageState.Failed;

        private static async Task MigrateLegacyAccountsCoreAsync()
        {
            NextendoCredentialStorageState issue = NextendoCredentialStorageState.None;
            try
            {
                lock (_lock)
                {
                    EnsureLoaded();
                    if (!OperatingSystem.IsWindows() || NextendoServerOverride.HorsNextendo) return;
                }
                NextendoCredentialStorageState active = await ProtectLegacyAsync().ConfigureAwait(false);
                if (NeedsAttention(active)) issue = active;
                if (NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)) return;
                if (NextendoTextPaths.HasPendingCredentialCopy(AppDataManager.BaseDirPath)) return;
                lock (_lock)
                {
                    if (_identityValid && _canProtect && !NextendoServerOverride.HorsNextendo && Directory.Exists(StoreDir))
                    {
                        bool pendingCopy = Directory.GetFiles(StoreDir, "*.txt").Any(path =>
                        {
                            Dictionary<string, string> fields = ReadFields(path);
                            return fields?.GetValueOrDefault("nex_token") == _nexToken &&
                                !File.ReadAllText(path).StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal);
                        });
                        if (pendingCopy && !WriteFileLocked()) issue = _storageState;
                    }
                }
                string[] files;
                lock (_lock) files = Directory.Exists(StoreDir) ? Directory.GetFiles(StoreDir, "*.txt") : [];
                foreach (string path in files)
                {
                    string text, token;
                    ulong pid;
                    try
                    {
                        lock (_lock)
                        {
                            if (NextendoServerOverride.HorsNextendo) return;
                            // Protected records are never periodically revalidated.
                            if (new FileInfo(path).Length > (NextendoCredentialFile.MaxPlaintextBytes + 4096L) * 2)
                            {
                                issue = NextendoCredentialStorageState.Failed;
                                continue;
                            }
                            if (File.ReadAllText(path).StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal)) continue;
                            text = NextendoCredentialFile.Read(path);
                            Dictionary<string, string> fields = ParseFields(text);
                            token = fields.GetValueOrDefault("nex_token", "");
                            if (string.IsNullOrEmpty(token) || token == _nexToken || !ulong.TryParse(fields.GetValueOrDefault("pid"), out pid) || pid == 0) continue;
                            string statusesPath = Path.Combine(AppDataManager.BaseDirPath, "nextendo_account_status.dat");
                            if (File.Exists(statusesPath) && ParseFields(NextendoProfileLinks.ReadState(AppDataManager.BaseDirPath)).TryGetValue(CredentialHash(token), out string rejection))
                            {
                                if (Enum.TryParse(rejection, out NextendoCredentialStorageState recorded) && NeedsAttention(recorded)) issue = recorded;
                                continue;
                            }
                        }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                        System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
                    {
                        issue = NextendoCredentialStorageState.Failed;
                        continue;
                    }
                    var resolution = await Authority.ResolveAsync(token).ConfigureAwait(false);
                    lock (_lock)
                    {
                        if (NextendoServerOverride.HorsNextendo) return;
                        if (!File.Exists(path) || NextendoCredentialFile.Read(path) != text || token == _nexToken) continue;
                        if (resolution.Identity == null)
                        {
                            if (resolution.Rejected) issue = RecordRejectionLocked(NextendoCredentialStorageState.Rejected, token);
                            continue;
                        }
                        if (resolution.Identity.Pid != pid)
                        {
                            issue = RecordRejectionLocked(NextendoCredentialStorageState.IdentityMismatch, token);
                            continue;
                        }
                        try
                        {
                            Dictionary<string, string> fields = ParseFields(text);
                            NextendoAccountStorage.Execute(AppDataManager.BaseDirPath, plan =>
                            {
                                if (plan.Read("nextendo_accounts/" + Path.GetFileName(path)) != text)
                                    throw new IOException("Saved account changed during migration.");
                                StageAccount(plan, pid, token, fields.GetValueOrDefault("profile_user_id", ""),
                                    fields.GetValueOrDefault("mii_data", ""), text, true, false);
                            });
                        }
                        catch (AccountConflictException) { issue = NextendoCredentialStorageState.Conflict; }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
            {
                issue = NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)
                    ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
            }
            finally
            {
                lock (_lock) BackgroundMigrationIssue = issue;
            }
        }

        // ⚠️ Le PID et le jeton NEX sont stockes ici a l etat BRUT, et lus par les
        // proprietes publiques ci-dessous, qui les taisent en mode « serveur
        // personnalise ». La persistance ecrit les champs bruts, jamais les proprietes :
        // sinon enregistrer pendant ce mode effacerait le compte du disque.
        private static ulong _pid;
        private static string _nexToken = "";

        /// <summary>Numero de compte Nextendo. Zero quand aucun compte n est utilisable,
        /// y compris parce que le mode « serveur personnalise » est actif.</summary>
        public static ulong Pid => GetIdentitySnapshot().Pid;

        public static string Username { get; private set; } = "";
        public static string FriendCode { get; private set; } = "";

        /// <summary>
        /// Le jeton signe qui prouve l identite du compte.
        ///
        /// ⚠️ IL NE DOIT JAMAIS SORTIR EN MODE « SERVEUR PERSONNALISE ». Le jeu le
        /// recopie dans le login NEX (claim « nnex ») : sans cette garde, se connecter au
        /// serveur d un inconnu lui livrerait un jeton signe valable sur NOS serveurs.
        /// </summary>
        public static string NexToken => GetIdentitySnapshot().Token;

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
        public static bool IsLinked => GetIdentitySnapshot().Pid != 0;

        /// <summary>Runtime-only (not persisted): set true at game launch when the running
        /// game is a Nextendo title on an UNSUPPORTED version. While true, the NEX login
        /// presents the anonymous stub instead of the account PID, so the gated server
        /// refuses online — enforcing the required game version.</summary>
        public static bool OnlineBlocked { get; set; }

        private static string FilePath => Ryujinx.Common.Configuration.NextendoTextPaths.GetFilePath("nextendo_account.txt");

        private static void EnsureLoaded()
        {
            lock (_lock)
            {
                if (_loaded)
                {
                    return;
                }

                _loaded = true;
                _canProtect = false;
                CredentialPersistenceFailed = false;
                _storageState = NextendoCredentialStorageState.None;

                try
                {
                    NextendoAccountStorage.Recover(AppDataManager.BaseDirPath);
                    _activeHash = ActiveHash();
                    if (!File.Exists(FilePath))
                    {
                        return;
                    }

                    ReadLocked(FilePath);
                }
                catch
                {
                    // Corrupt/unreadable file -> treat as not linked.
                    _pid = 0;
                    _identityValid = false;
                    _storageState = NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)
                        ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
                }
            }
        }

        private static void ReadLocked(string path, ulong expectedPid = 0)
        {
            _identityValid = false;
            _pid = 0;
            Username = "";
            FriendCode = "";
            _nexToken = "";
            _profileUserId = "";
            _miiData = "";
            _isGuest = false;

            string text = NextendoCredentialFile.Read(path);
            foreach (string line in text.Split('\n'))
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

            if (expectedPid != 0 && _pid != expectedPid) throw new InvalidDataException("Stored account PID differs from its filename.");
            _identityValid = _pid != 0 && !string.IsNullOrEmpty(_nexToken);
            _canProtect = File.ReadAllText(path).StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal);
            _storageState = _canProtect ? NextendoCredentialStorageState.Protected : OperatingSystem.IsWindows()
                ? NextendoCredentialStorageState.LegacyPending : NextendoCredentialStorageState.Unsupported;
            string statusPath = Path.Combine(AppDataManager.BaseDirPath, "nextendo_account_status.dat");
            if (File.Exists(statusPath))
            {
                string rejection = ParseFields(NextendoProfileLinks.ReadState(AppDataManager.BaseDirPath)).GetValueOrDefault(CredentialHash(_nexToken));
                if (Enum.TryParse(rejection, out NextendoCredentialStorageState rejected) &&
                    rejected is NextendoCredentialStorageState.Rejected or NextendoCredentialStorageState.IdentityMismatch)
                {
                    _identityValid = false;
                    _storageState = rejected;
                }
            }
            _generation++;
        }

        // Plusieurs comptes Nextendo dans un seul emulateur.
        //
        // nextendo_account.txt reste LE compte actif : tout l'emulateur (NEX, amis, sauvegardes,
        // presence) lit le compte actif et rien d'autre. Chaque compte connecte a en plus sa copie
        // dans nextendo_accounts/<pid>.txt ; changer de compte recopie l'un d'eux a la place du
        // compte actif. Un meme compte n'a qu'un fichier (la cle est son PID) : se reconnecter
        // avec lui le met a jour, jamais un second exemplaire.

        /// <summary>Un compte Nextendo connecte sur cet emulateur.</summary>
        public sealed record Stored(ulong Pid, string Username, string FriendCode, string ProfileUserId, bool IsGuest)
        {
            public NextendoCredentialStorageState StorageState { get; init; }
            public bool RequiresSignIn => StorageState is NextendoCredentialStorageState.IdentityMismatch or NextendoCredentialStorageState.Rejected;
        }

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
                            Dictionary<string, string> statuses = ParseFields(NextendoProfileLinks.ReadState(AppDataManager.BaseDirPath));
                            foreach (string file in Directory.GetFiles(StoreDir, "*.txt"))
                            {
                                Dictionary<string, string> v = ReadFields(file);
                                if (v == null)
                                {
                                    continue;
                                }

                                if (!v.TryGetValue("pid", out string ps) || !ulong.TryParse(ps, out ulong pid) || pid == 0)
                                {
                                    continue;
                                }

                                NextendoCredentialStorageState state = NextendoCredentialStorageState.None;
                                string token = v.GetValueOrDefault("nex_token", "");
                                if (!string.IsNullOrEmpty(token) && statuses.TryGetValue(CredentialHash(token), out string rejection) &&
                                    Enum.TryParse(rejection, out NextendoCredentialStorageState rejected) &&
                                    rejected is NextendoCredentialStorageState.IdentityMismatch or NextendoCredentialStorageState.Rejected)
                                    state = rejected;
                                list.Add(new Stored(pid,
                                    v.GetValueOrDefault("username", ""),
                                    v.GetValueOrDefault("friend_code", ""),
                                    v.GetValueOrDefault("profile_user_id", ""),
                                    v.GetValueOrDefault("is_guest", "0") == "1") { StorageState = state });
                            }
                        }
                    }
                    catch
                    {
                        // an unreadable list still leaves the active account
                    }

                    if (_pid != 0 && list.All(a => a.Pid != _pid))
                    {
                        list.Add(new Stored(_pid, Username, FriendCode, _profileUserId, _isGuest) { StorageState = _storageState });
                    }

                    ulong active = _pid;

                    return [.. list.DistinctBy(a => a.Pid).OrderByDescending(a => a.Pid == active).ThenBy(a => a.Username, StringComparer.OrdinalIgnoreCase)];
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
                    return pid != 0 && GetIdentitySnapshot().Pid == pid;
                }

                if (pid == 0 || !File.Exists(StoredPath(pid)))
                {
                    return false;
                }

                var previous = (_pid, Username, FriendCode, _nexToken, _profileUserId, _miiData, _isGuest, _identityValid, _canProtect, _storageState);
                try
                {
                    if (!PreservePreviousLocked())
                        return false;
                    ReadLocked(StoredPath(pid), pid);
                    if (!_identityValid || _pid != pid)
                    {
                        throw new InvalidOperationException("The selected account could not be verified.");
                    }
                    _loaded = true;
                    if (!WriteFileLocked()) throw new IOException("Account selection could not be saved.");
                }
                catch
                {
                    (_pid, Username, FriendCode, _nexToken, _profileUserId, _miiData, _isGuest, _identityValid, _canProtect, _storageState) = previous;
                    _generation++;
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

        /// <summary>Persists an authenticated login response without selecting it on a failed write.</summary>
        public static bool Save(ulong pid, string username, string friendCode, string nexToken, bool isGuest = false, long? expectedGeneration = null)
        {
            if (pid == 0 || string.IsNullOrWhiteSpace(nexToken) || nexToken.Contains('\n') || nexToken.Contains('\r'))
                throw new ArgumentException("A login response must contain a valid account and credential.");
            username = CleanField(username);
            friendCode = CleanField(friendCode);
            lock (_lock)
            {
                EnsureLoaded();
                if (expectedGeneration.HasValue && expectedGeneration.Value != _generation)
                    throw new InvalidOperationException("The active account changed while signing in. Please try again.");
                string rememberedProfile = LinkedProfile(pid);
                bool switching = pid != _pid;
                if (switching && !PreservePreviousLocked())
                    throw new IOException("The previous account could not be saved. Resolve its storage problem before switching.");
                bool saved = PersistMutationLocked(() =>
                {
                    if (switching)
                    {
                        _profileUserId = "";
                        _miiData = "";
                        if (File.Exists(StoredPath(pid)))
                        {
                            Dictionary<string, string> fields = ReadFields(StoredPath(pid));
                            if (fields != null)
                            {
                                _profileUserId = fields.GetValueOrDefault("profile_user_id", "");
                                _miiData = fields.GetValueOrDefault("mii_data", "");
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(_profileUserId)) _profileUserId = rememberedProfile;
                    _pid = pid;
                    Username = username;
                    FriendCode = friendCode;
                    _nexToken = nexToken;
                    _isGuest = isGuest;
                    _identityValid = true;
                    _canProtect = true;
                    _generation++;
                    _loaded = true;
                });
                if (saved) BackgroundMigrationIssue = NextendoCredentialStorageState.None;
                return saved;
            }
        }

        private static string CleanField(string value)
        {
            value ??= "";
            if (value.Contains('\r') || value.Contains('\n')) throw new ArgumentException("Account fields cannot contain line breaks.");
            return value;
        }

        // Callers hold _lock, so no reader can observe the candidate before persistence finishes.
        private static bool PersistMutationLocked(Action mutate)
        {
            var previous = (_pid, Username, FriendCode, _nexToken, _profileUserId, _miiData,
                _isGuest, _identityValid, _canProtect, _loaded, _activeHash);
            void Restore()
            {
                (_pid, Username, FriendCode, _nexToken, _profileUserId, _miiData,
                    _isGuest, _identityValid, _canProtect, _loaded, _activeHash) = previous;
                // Invalidate responses captured during either the previous or failed candidate session.
                _generation++;
            }
            try
            {
                mutate();
                if (WriteFileLocked()) return true;
            }
            catch
            {
                Restore();
                throw;
            }
            // Keep the storage error and pending journal visible while restoring session identity.
            Restore();
            return false;
        }

        /// <summary>Updates display metadata only if the originating session is still active.</summary>
        public static bool UpdateDisplayName(NextendoIdentitySnapshot expected, string username)
        {
            lock (_lock)
            {
                EnsureLoaded();
                if (expected == null || expected.Pid != _pid || expected.Token != _nexToken || expected.Generation != _generation || !_identityValid)
                    return false;
                string cleaned = CleanField(username);
                return PersistMutationLocked(() => Username = cleaned);
            }
        }

        /// <summary>Binds the local profile only when its account metadata can be persisted.</summary>
        public static bool SetProfileUserId(string userId)
        {
            lock (_lock)
            {
                EnsureLoaded();
                string cleaned = CleanField(userId);
                if (_profileUserId == cleaned) return true;
                return PersistMutationLocked(() =>
                {
                    _profileUserId = cleaned;
                    _generation++;
                });
            }
        }

        /// <summary>Mirrors the account's Mii in its persisted metadata.</summary>
        public static bool SetMiiData(string miiData)
        {
            lock (_lock)
            {
                EnsureLoaded();
                string cleaned = CleanField(miiData);
                if (_miiData == cleaned) return true;
                return PersistMutationLocked(() => _miiData = cleaned);
            }
        }

        // pid=profile lines: which local profile each Nextendo account was bound to. Kept apart
        // from the session, so that losing the session never loses the link.
        private static string LinksPath => Ryujinx.Common.Configuration.NextendoTextPaths.GetFilePath("nextendo_profile_links.txt");

        private static string LinkedProfile(ulong pid) =>
            NextendoProfileLinks.Fields(NextendoProfileLinks.Read(AppDataManager.BaseDirPath)).GetValueOrDefault(pid.ToString(), "");

        private static Dictionary<string, string> ParseFields(string text)
        {
            Dictionary<string, string> fields = [];
            foreach (string line in (text ?? "").Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) fields[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            return fields;
        }

        private static string CredentialHash(string token) => Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));

        private static string ActiveHash()
        {
            if (!File.Exists(FilePath)) return null;
            if (new FileInfo(FilePath).Length > (NextendoCredentialFile.MaxPlaintextBytes + 4096L) * 2)
                throw new InvalidDataException("Account file exceeds its size limit.");
            using FileStream stream = File.OpenRead(FilePath);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }

        private static NextendoCredentialStorageState RecordRejectionLocked(NextendoCredentialStorageState rejected, string token = null)
        {
            token ??= _nexToken;
            try
            {
                NextendoAccountStorage.Execute(AppDataManager.BaseDirPath, plan =>
                {
                    Dictionary<string, string> statuses = ParseFields(plan.Read("nextendo_account_status.dat"));
                    statuses[CredentialHash(token)] = rejected.ToString();
                    plan.Put("nextendo_account_status.dat", string.Join("\n", statuses.Select(p => p.Key + "=" + p.Value)), OperatingSystem.IsWindows());
                });
                if (token == _nexToken) _storageState = rejected;
                return rejected;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
            {
                CredentialPersistenceFailed = true;
                var failure = NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)
                    ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
                if (token == _nexToken) _storageState = failure;
                return failure;
            }
        }

        private static bool WriteFileLocked()
        {
            if (!_identityValid || _pid == 0) return false;
            if (NextendoTextPaths.HasPendingCredentialCopy(AppDataManager.BaseDirPath))
            {
                CredentialPersistenceFailed = true;
                _storageState = NextendoCredentialStorageState.RecoveryRequired;
                return false;
            }
            try
            {
                bool protect = OperatingSystem.IsWindows() && _canProtect;
                string text = $"pid={_pid}\nusername={Username}\nfriend_code={FriendCode}\nnex_token={_nexToken}\nprofile_user_id={_profileUserId}\nmii_data={_miiData}\nis_guest={(_isGuest ? "1" : "0")}\n";
                NextendoAccountStorage.Execute(AppDataManager.BaseDirPath, plan =>
                {
                    StageAccount(plan, _pid, _nexToken, _profileUserId, _miiData, text, protect, true);
                });
                _activeHash = ActiveHash();
                CredentialPersistenceFailed = false;
                _storageState = protect ? NextendoCredentialStorageState.Protected : OperatingSystem.IsWindows()
                    ? NextendoCredentialStorageState.LegacyPending : NextendoCredentialStorageState.Unsupported;
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException or AccountConflictException)
            {
                CredentialPersistenceFailed = true;
                _storageState = exception is AccountConflictException or NextendoProfileLinks.ConflictException ? NextendoCredentialStorageState.Conflict :
                    NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath) ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
                Logger.Warning?.Print(LogClass.Application, "[Nextendo] Account storage is incomplete. Existing account files are retained for recovery; check account settings.");
                return false;
            }
        }

        private static void StageAccount(NextendoAccountStorage.Plan plan, ulong pid, string token, string profileId,
            string miiData, string text, bool protect, bool writeActive)
        {
            string activeText = plan.Read("nextendo_account.txt");
            if (ActiveHash() != _activeHash) throw new IOException("The active account file changed in another process. Restart before saving.");
            Dictionary<string, string> active = ParseFields(activeText);
            string sourceProfile = active.GetValueOrDefault("pid") == pid.ToString() ? active.GetValueOrDefault("profile_user_id", "") : profileId;
            string sourceMii = active.GetValueOrDefault("pid") == pid.ToString() ? active.GetValueOrDefault("mii_data", "") : miiData;
            List<string> copies = Directory.Exists(StoreDir)
                ? Directory.GetFiles(StoreDir, "*.txt").Select(f => "nextendo_accounts/" + Path.GetFileName(f)).ToList() : [];
            string canonical = "nextendo_accounts/" + pid + ".txt";
            if (!copies.Contains(canonical)) copies.Add(canonical);
            List<string> matching = [];
            foreach (string copy in copies)
            {
                // Unreadable unrelated accounts are preserved. A canonical destination
                // must be readable before it can be replaced.
                string previous;
                try { previous = plan.Read(copy); }
                catch when (copy != canonical) { continue; }
                Dictionary<string, string> fields = ParseFields(previous);
                if (copy != canonical && fields.GetValueOrDefault("nex_token") != token) continue;
                if (!protect && previous != null && File.ReadAllText(Path.Combine(AppDataManager.BaseDirPath, copy))
                    .StartsWith(NextendoCredentialFile.Prefix, StringComparison.Ordinal)) throw new AccountConflictException();
                string profile = fields.GetValueOrDefault("profile_user_id", "");
                string mii = fields.GetValueOrDefault("mii_data", "");
                if ((!string.IsNullOrEmpty(profile) && !string.Equals(profile, sourceProfile, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(mii) && mii != sourceMii))
                    throw new AccountConflictException();
                matching.Add(copy);
            }
            Dictionary<string, string> links = ParseFields(plan.Read("nextendo_profile_links.txt"));
            string existingLink = links.GetValueOrDefault(pid.ToString(), "");
            if (!string.IsNullOrEmpty(existingLink) && !string.Equals(existingLink, sourceProfile, StringComparison.OrdinalIgnoreCase)) throw new AccountConflictException();
            if (!string.IsNullOrEmpty(profileId)) links[pid.ToString()] = profileId;
            Dictionary<string, string> statuses = ParseFields(plan.Read("nextendo_account_status.dat"));
            if (statuses.Remove(CredentialHash(token)))
                plan.Put("nextendo_account_status.dat", string.Join("\n", statuses.Select(p => p.Key + "=" + p.Value)), OperatingSystem.IsWindows());
            foreach (string copy in matching) plan.Put(copy, text, protect);
            if (writeActive) plan.Put("nextendo_account.txt", text, protect);
            plan.Put("nextendo_profile_links.txt", string.Join("\n", links.Select(p => p.Key + "=" + p.Value)) + "\n", OperatingSystem.IsWindows());
        }


        private sealed class AccountConflictException : Exception { }

        private static bool PreservePreviousLocked()
        {
            if (_pid == 0) return true;
            if (_identityValid) return WriteFileLocked();
            if (File.Exists(StoredPath(_pid)))
            {
                Dictionary<string, string> fields = ReadFields(StoredPath(_pid));
                if (fields != null && fields.GetValueOrDefault("profile_user_id", "") == _profileUserId &&
                    fields.GetValueOrDefault("mii_data", "") == _miiData) return true;
                CredentialPersistenceFailed = true;
                _storageState = NextendoCredentialStorageState.Conflict;
                return false;
            }
            // A rejected legacy account still owns local profile metadata. Archive its record
            // before replacing the active file; its credential-specific rejection remains in force.
            try
            {
                NextendoAccountStorage.Execute(AppDataManager.BaseDirPath, plan =>
                {
                    string original = plan.Read("nextendo_account.txt");
                    if (ActiveHash() != _activeHash || original == null) throw new IOException("The previous account changed.");
                    plan.Put("nextendo_accounts/" + _pid + ".txt", original, OperatingSystem.IsWindows());
                });
                return true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
            {
                CredentialPersistenceFailed = true;
                _storageState = NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)
                    ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
                return false;
            }
        }

        private static Dictionary<string, string> ReadFields(string path)
        {
            try
            {
                Dictionary<string, string> fields = [];
                foreach (string line in NextendoCredentialFile.Read(path).Split('\n'))
                {
                    int separator = line.IndexOf('=');
                    if (separator > 0)
                    {
                        fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                    }
                }

                return fields;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
            {
                // One damaged saved account must not hide or prevent removal of other accounts.
                return null;
            }
        }

        /// <summary>Signs out the ACTIVE account: it leaves the list, and another connected
        /// account, if any, becomes the active one.</summary>
        public static bool Clear()
        {
            ulong[] candidates;
            lock (_lock)
            {
                EnsureLoaded();
                if (NextendoTextPaths.HasPendingCredentialCopy(AppDataManager.BaseDirPath))
                {
                    CredentialPersistenceFailed = true;
                    _storageState = NextendoCredentialStorageState.RecoveryRequired;
                    return false;
                }
                candidates = All.Where(a => a.Pid != _pid && !a.RequiresSignIn).Select(a => a.Pid).ToArray();
                try
                {
                    NextendoAccountStorage.Execute(AppDataManager.BaseDirPath, plan =>
                    {
                        plan.Read("nextendo_account.txt");
                        if (ActiveHash() != _activeHash) throw new IOException("The active account file changed.");
                        foreach (string file in Directory.Exists(StoreDir) ? Directory.GetFiles(StoreDir, "*.txt") : [])
                        {
                            string relative = "nextendo_accounts/" + Path.GetFileName(file);
                            Dictionary<string, string> fields;
                            try { fields = ParseFields(plan.Read(relative)); }
                            catch { continue; }
                            if (_pid != 0 && (fields.GetValueOrDefault("pid") == _pid.ToString() ||
                                (!string.IsNullOrEmpty(_nexToken) && fields.GetValueOrDefault("nex_token") == _nexToken)))
                                plan.Put(relative, null);
                        }
                        // Keep local profile associations and saves on sign-out.
                        plan.Put("nextendo_account.txt", null);
                    });
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
                    System.Security.Cryptography.CryptographicException or FormatException or PlatformNotSupportedException)
                {
                    CredentialPersistenceFailed = true;
                    _storageState = NextendoAccountStorage.HasPending(AppDataManager.BaseDirPath)
                        ? NextendoCredentialStorageState.RecoveryRequired : NextendoCredentialStorageState.Failed;
                    return false;
                }
                _activeHash = null;
                _identityValid = false;
                _canProtect = false;
                _generation++;
                _pid = 0;
                Username = "";
                FriendCode = "";
                _nexToken = "";
                _profileUserId = "";
                _miiData = "";
                _isGuest = false;
                _storageState = NextendoCredentialStorageState.None;
                CredentialPersistenceFailed = false;
            }
            foreach (ulong candidate in candidates)
            {
                if (Activate(candidate)) return true;
            }
            try { ActiveChanged?.Invoke(); } catch { /* The UI may not be ready. */ }
            return true;
        }
    }
}
