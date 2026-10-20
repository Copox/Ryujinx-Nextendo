using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ryujinx.Common.Configuration
{
    /// <summary>
    /// [Nextendo] Île d'Animal Crossing: New Horizons dans le cloud, règle de Nintendo (prouvée le
    /// 20/10 dans ACNH 3.0.3 et olsc 22.5.0) : UN emplacement par compte, la dernière île envoyée
    /// remplace la précédente ; une île appartient à sa console (aucun signal « en jeu ailleurs ») ;
    /// la restauration est verrouillée tant que le joueur ne l'a pas débloquée depuis le site
    /// (chez Nintendo : le support). Ce fichier ne contient que la logique sans interface.
    /// </summary>
    public static class NextendoIslandSync
    {
        public const ulong AcnhTitleId = 0x01006f8002326000;

        /// <summary>Titres à synchro stricte (sauvegarde Device, une par compte).</summary>
        public static bool IsStrict(ulong titleId) => titleId == AcnhTitleId;

        public sealed class SyncState
        {
            /// <summary>L'île de cette installation est celle archivée pour le compte (« Lier » ou restauration).</summary>
            public bool Linked { get; set; }

            /// <summary>Révision du cloud au dernier envoi ou à la dernière restauration (information).</summary>
            public ulong Rev { get; set; }

            /// <summary>Empreinte du contenu local à ce moment-là (évite de renvoyer une île inchangée).</summary>
            public string LocalHash { get; set; } = "";
        }

        public enum LaunchAction
        {
            /// <summary>Rien à demander : lancer.</summary>
            Nothing,
            /// <summary>Île locale pas encore archivée pour le compte : proposer de la lier.</summary>
            OfferLink,
            /// <summary>Restauration débloquée depuis le site et île dans le cloud : la proposer.</summary>
            OfferRestore,
        }

        /// <summary>
        /// Décision au lancement. La restauration n'est proposée que si le joueur l'a débloquée sur
        /// le site ; sinon une île locale non liée se voit proposer le lien (qui remplacera l'île
        /// archivée, comme l'activation de l'archivage dans le jeu).
        /// </summary>
        public static LaunchAction Decide(bool cloudExists, bool restoreUnlocked, bool localExists, SyncState state)
        {
            if (cloudExists && restoreUnlocked)
            {
                return LaunchAction.OfferRestore;
            }

            if (localExists && (state == null || !state.Linked))
            {
                return LaunchAction.OfferLink;
            }

            return LaunchAction.Nothing;
        }

        // ---- empreinte et zip ---------------------------------------------------------------

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

        // ---- état persistant ----------------------------------------------------------------

        private static string StateDir => Path.Combine(AppDataManager.BaseDirPath ?? ".", "nextendo_saves");

        private static string StatePath(ulong pid, ulong titleId) => Path.Combine(StateDir, $"{pid}_{titleId:x16}.json");

        public static SyncState LoadState(ulong pid, ulong titleId)
        {
            try
            {
                string p = StatePath(pid, titleId);
                return File.Exists(p) ? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(p)) : null;
            }
            catch
            {
                return null; // illisible = jamais synchronisé : la décision posera la question
            }
        }

        public static void SaveState(ulong pid, ulong titleId, SyncState state)
        {
            Directory.CreateDirectory(StateDir);
            string p = StatePath(pid, titleId);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state));
            File.Move(tmp, p, overwrite: true);
        }

        private static string _deviceId;

        /// <summary>Identifiant de cette installation (« ryujinx:3fa2c1d0 »), affiché sur le site et dans la méta.</summary>
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
