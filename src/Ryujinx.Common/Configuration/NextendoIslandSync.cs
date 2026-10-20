using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Archivage de l'île d'Animal Crossing: New Horizons, fait par le JEU (écran titre,
    /// touche −, « Paramètres de sauvegarde ») à travers olsc:u, comme sur une Switch. Règle de
    /// Nintendo (ACNH 3.0.3 et olsc 22.5.0) : UN emplacement par compte, la dernière île envoyée
    /// remplace la précédente ; la restauration est permise d'office au titulaire du compte.
    /// Ce fichier ne contient que la logique sans IPC ni réseau (testée).
    /// </summary>
    public static class NextendoIslandSync
    {
        public const ulong AcnhTitleId = 0x01006f8002326000;

        /// <summary>Titres dont l'archivage passe par notre serveur (sauvegarde Device, une par compte).</summary>
        public static bool IsStrict(ulong titleId) => titleId == AcnhTitleId;

        /// <summary>Identifiant d'application Nextendo (X-Nextendo-Client-Id), posé au démarrage par l'interface.</summary>
        public static string ClientId { get; set; } = "";

        // ---- SaveDataBackupInfo (olsc) ------------------------------------------------------
        //
        // Lu dans ACNH 3.0.3 : GetSaveDataBackupInfoCache (cmd 18) remplit un tampon de taille fixe
        // 0x410 (descripteur 0x16) et le jeu y lit +8 (PosixTime, passée à ToCalendarTime) ; la liste
        // (cmd 25, objet cmd 101) rend des éléments de la même taille : +0 l'identifiant que le jeu
        // repasse à DownloadSaveDataBackupAsync (cmd 26), +8 la date, +0x10 les 0x400 octets des
        // données personnalisées que le jeu a lui-même posées (SetCustomData, cmd 15) et qu'il relit
        // pour afficher l'île (0x1c39d40 : un élément illisible fait échouer toute la liste).

        public const int BackupInfoSize = 0x410;
        public const int CustomDataSize = 0x400;

        public static void WriteBackupInfo(Span<byte> dst, ulong id, long posixTime, ReadOnlySpan<byte> custom)
        {
            if (dst.Length < BackupInfoSize)
            {
                throw new ArgumentException("buffer too small", nameof(dst));
            }

            dst[..BackupInfoSize].Clear();
            BitConverter.TryWriteBytes(dst[..8], id);
            BitConverter.TryWriteBytes(dst.Slice(8, 8), posixTime);
            custom[..Math.Min(custom.Length, CustomDataSize)].CopyTo(dst.Slice(0x10, CustomDataSize));
        }

        /// <summary>État de l'île du compte, tel que le rend GET /api/save/&lt;titre&gt;/meta.</summary>
        public sealed class CloudIsland
        {
            public bool Exists;
            public ulong Rev;
            public long PosixTime;
            public byte[] Custom = [];
        }

        public static CloudIsland ParseMeta(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement e = doc.RootElement;
            CloudIsland c = new()
            {
                Exists = e.TryGetProperty("exists", out JsonElement ex) && ex.ValueKind == JsonValueKind.True,
                Rev = e.TryGetProperty("rev", out JsonElement r) && r.TryGetUInt64(out ulong rv) ? rv : 0,
            };

            if (e.TryGetProperty("updatedAt", out JsonElement u) && u.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(u.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset t))
            {
                c.PosixTime = t.ToUnixTimeSeconds();
            }

            if (e.TryGetProperty("custom", out JsonElement cu) && cu.ValueKind == JsonValueKind.String)
            {
                try
                {
                    byte[] raw = Convert.FromBase64String(cu.GetString() ?? "");
                    c.Custom = raw.Length <= CustomDataSize ? raw : [];
                }
                catch (FormatException)
                {
                    c.Custom = [];
                }
            }

            return c;
        }

        /// <summary>
        /// Une île n'est proposée à la restauration que si elle porte les données personnalisées du
        /// jeu : sans elles, le jeu ferait échouer toute la liste (île envoyée par un autre client).
        /// </summary>
        public static bool IsRestorable(CloudIsland c) => c != null && c.Exists && c.Custom.Length > 0;

        // ---- empreinte, zip, extraction -----------------------------------------------------

        private static IEnumerable<(string Rel, string Full)> Files(string dir)
        {
            return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(f => (Path.GetRelativePath(dir, f).Replace('\\', '/'), f))
                .OrderBy(x => x.Item1, StringComparer.Ordinal);
        }

        /// <summary>Empreinte SHA-256 du dossier (chemins relatifs triés, tailles, contenus). "" si vide.</summary>
        public static string Fingerprint(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                return "";
            }

            using IncrementalHash h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            bool any = false;
            byte[] buf = new byte[1 << 16];
            foreach ((string rel, string full) in Files(dir))
            {
                any = true;
                h.AppendData(Encoding.UTF8.GetBytes(rel));
                h.AppendData(BitConverter.GetBytes(new FileInfo(full).Length));
                using FileStream fs = File.OpenRead(full);
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    h.AppendData(buf, 0, n);
                }
            }

            return any ? Convert.ToHexString(h.GetHashAndReset()).ToLowerInvariant() : "";
        }

        /// <summary>
        /// Zip du dossier, chemins en « / », sans entrées de dossier, sans compression (les .dat
        /// d'ACNH sont chiffrés : la compression ne gagne rien et coûte du temps).
        /// </summary>
        public static byte[] ZipDirectory(string dir)
        {
            using MemoryStream ms = new();
            using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach ((string rel, string full) in Files(dir))
                {
                    ZipArchiveEntry e = zip.CreateEntry(rel, CompressionLevel.NoCompression);
                    e.LastWriteTime = File.GetLastWriteTimeUtc(full);
                    using Stream dst = e.Open();
                    using FileStream src = File.OpenRead(full);
                    src.CopyTo(dst);
                }
            }

            return ms.ToArray();
        }

        /// <summary>Plafond d'une île une fois extraite (8 habitants : 21,35 Mo ; le serveur refuse au-delà de 64 Mio).</summary>
        public const long MaxExtractedBytes = 64L << 20;

        /// <summary>
        /// Extrait une île dans un dossier NEUF. Refuse tout chemin absolu ou qui sort du dossier,
        /// toute île sans main.dat, et tout contenu au-delà de <see cref="MaxExtractedBytes"/>.
        /// </summary>
        public static void ExtractIsland(byte[] zipBytes, string newDir)
        {
            if (Directory.Exists(newDir))
            {
                throw new IOException("target exists");
            }

            string root = Path.GetFullPath(newDir) + Path.DirectorySeparatorChar;
            using ZipArchive zip = new(new MemoryStream(zipBytes), ZipArchiveMode.Read);
            long total = 0;
            bool hasMain = false;

            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string name = e.FullName.Replace('\\', '/');
                if (name.Length == 0 || name.EndsWith('/'))
                {
                    continue;
                }

                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p == ".." || p == "."))
                {
                    throw new InvalidDataException("invalid entry name");
                }

                string full = Path.GetFullPath(Path.Combine(newDir, name));
                if (!full.StartsWith(root, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("entry outside the save");
                }

                total += e.Length;
                if (total > MaxExtractedBytes)
                {
                    throw new InvalidDataException("island too large");
                }

                hasMain |= name == "main.dat";
            }

            if (!hasMain)
            {
                throw new InvalidDataException("not an island (no main.dat)");
            }

            Directory.CreateDirectory(newDir);
            foreach (ZipArchiveEntry e in zip.Entries)
            {
                string name = e.FullName.Replace('\\', '/');
                if (name.Length == 0 || name.EndsWith('/'))
                {
                    continue;
                }

                string full = Path.GetFullPath(Path.Combine(newDir, name));
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                e.ExtractToFile(full, overwrite: false);
            }
        }

        /// <summary>
        /// Remplace l'île de la sauvegarde <paramref name="saveRoot"/> (dossiers « 0 » validé et « 1 »
        /// de travail) par celle du zip. L'île de cette machine est d'abord gardée dans
        /// <paramref name="backupsDir"/> (5 dernières « &lt;prefix&gt;-&lt;date&gt;.zip ») ; un zip invalide ne
        /// touche à rien, un échec en cours de route remet l'île d'avant.
        /// </summary>
        public static NextendoIslandCloud.Outcome ReplaceIsland(string saveRoot, byte[] zip, string backupsDir, string prefix)
        {
            string fresh = Path.Combine(saveRoot, "nx_restore_new");
            string old0 = Path.Combine(saveRoot, "nx_restore_old0");
            string old1 = Path.Combine(saveRoot, "nx_restore_old1");

            try
            {
                foreach (string leftover in new[] { fresh, old0, old1 })
                {
                    TryDelete(leftover);
                }

                ExtractIsland(zip, fresh);
            }
            catch (Exception)
            {
                TryDelete(fresh);
                return NextendoIslandCloud.Outcome.InvalidIsland;
            }

            string current = NextendoIslandCloud.ContentDir(saveRoot);
            if (current != null)
            {
                try
                {
                    Directory.CreateDirectory(backupsDir);
                    string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
                    File.WriteAllBytes(Path.Combine(backupsDir, $"{prefix}-{stamp}.zip"), ZipDirectory(current));
                    foreach (string extra in Directory.GetFiles(backupsDir, $"{prefix}-*.zip").OrderByDescending(f => f, StringComparer.Ordinal).Skip(5))
                    {
                        File.Delete(extra);
                    }
                }
                catch (Exception)
                {
                    TryDelete(fresh);
                    return NextendoIslandCloud.Outcome.NoLocalSave;
                }
            }

            string dir0 = Path.Combine(saveRoot, "0");
            string dir1 = Path.Combine(saveRoot, "1");
            bool had0 = Directory.Exists(dir0);
            bool had1 = Directory.Exists(dir1);

            try
            {
                if (had0)
                {
                    Directory.Move(dir0, old0);
                }

                if (had1)
                {
                    Directory.Move(dir1, old1);
                }

                Directory.Move(fresh, dir0);
                if (had1)
                {
                    CopyDirectory(dir0, dir1);
                }
            }
            catch (Exception)
            {
                try
                {
                    if (Directory.Exists(old0))
                    {
                        TryDelete(dir0);
                        Directory.Move(old0, dir0);
                    }

                    if (Directory.Exists(old1))
                    {
                        TryDelete(dir1);
                        Directory.Move(old1, dir1);
                    }
                }
                catch (Exception)
                {
                    // la copie reste dans backupsDir
                }

                TryDelete(fresh);
                return NextendoIslandCloud.Outcome.NoLocalSave;
            }

            TryDelete(old0);
            TryDelete(old1);

            return NextendoIslandCloud.Outcome.Ok;
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(dst, Path.GetRelativePath(src, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception)
            {
                // un reste sans effet : effacé au prochain essai
            }
        }

        // ---- réglages d'archivage, données personnalisées et cache, par profil ---------------

        /// <summary>Ce qu'olsc garde par (profil, jeu) : réglage (1 = activé), données du jeu, cache de l'île.</summary>
        public sealed class OlscEntry
        {
            public byte Setting { get; set; }
            public string Custom { get; set; } = "";
            public bool HasCache { get; set; }
            public ulong CacheId { get; set; }
            public long CacheTime { get; set; }
            public string CacheCustom { get; set; } = "";
            /// <summary>Empreinte de la dernière île envoyée ou restaurée (pas de renvoi inutile).</summary>
            public string LastUploadedHash { get; set; } = "";
            /// <summary>Envoi d'après-partie pas encore fait (relancé au lancement suivant de l'émulateur).</summary>
            public bool PendingUpload { get; set; }
            public string PendingSaveRoot { get; set; } = "";
        }

        private static readonly object _lock = new();

        /// <summary>Dossier du fichier d'état (par défaut celui des données de l'émulateur ; les tests le changent).</summary>
        public static string StoreDirectory { get; set; }

        private static string StorePath => Path.Combine(StoreDirectory ?? AppDataManager.BaseDirPath ?? ".", "nextendo_olsc.json");

        /// <summary>Clé d'un (jeu, profil) : « 01006f8002326000:&lt;uid en hexa&gt; ».</summary>
        public static string Key(ulong titleId, string uidHex) => $"{titleId:x16}:{uidHex.ToLowerInvariant()}";

        private static Dictionary<string, OlscEntry> LoadAll()
        {
            try
            {
                return File.Exists(StorePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, OlscEntry>>(File.ReadAllText(StorePath)) ?? []
                    : [];
            }
            catch
            {
                return [];
            }
        }

        /// <summary>Les (clé, état) dont l'envoi est en attente.</summary>
        public static List<(string Key, OlscEntry Entry)> AllPending()
        {
            lock (_lock)
            {
                return LoadAll().Where(kv => kv.Value.PendingUpload).Select(kv => (kv.Key, kv.Value)).ToList();
            }
        }

        public static OlscEntry Get(string key)
        {
            lock (_lock)
            {
                return LoadAll().TryGetValue(key, out OlscEntry e) ? e : new OlscEntry();
            }
        }

        public static void Update(string key, Action<OlscEntry> change)
        {
            lock (_lock)
            {
                Dictionary<string, OlscEntry> all = LoadAll();
                if (!all.TryGetValue(key, out OlscEntry e))
                {
                    e = new OlscEntry();
                    all[key] = e;
                }

                change(e);
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                string tmp = StorePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(all));
                File.Move(tmp, StorePath, overwrite: true);
            }
        }

        private static string _deviceId;

        /// <summary>Identifiant de cette installation (« ryujinx:3fa2c1d0 »), gardé dans la méta de l'île.</summary>
        public static string DeviceId
        {
            get
            {
                if (_deviceId != null)
                {
                    return _deviceId;
                }

                string p = Path.Combine(AppDataManager.BaseDirPath ?? ".", "nextendo_device_id.txt");
                try
                {
                    string id = File.Exists(p) ? File.ReadAllText(p).Trim() : "";
                    if (id.Length != 8 || !id.All(Uri.IsHexDigit))
                    {
                        id = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
                        File.WriteAllText(p, id);
                    }

                    _deviceId = "ryujinx:" + id;
                }
                catch
                {
                    _deviceId = "ryujinx:inconnu";
                }

                return _deviceId;
            }
        }
    }
}
