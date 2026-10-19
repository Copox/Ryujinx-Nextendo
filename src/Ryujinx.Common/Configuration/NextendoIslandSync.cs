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
    /// [Nextendo] Synchro STRICTE d'une sauvegarde (île d'Animal Crossing: New Horizons) : une seule
    /// sauvegarde par compte dans le cloud, une révision côté serveur, et jamais d'écrasement sans
    /// que le joueur l'ait choisi. Ce fichier ne contient que la logique sans interface (testable) :
    /// l'état de synchro par compte et par jeu, l'empreinte du contenu local, le zip envoyé, et la
    /// décision prise au lancement.
    /// </summary>
    public static class NextendoIslandSync
    {
        public const ulong AcnhTitleId = 0x01006f8002326000;

        /// <summary>Titres à synchro stricte (sauvegarde Device, une par compte).</summary>
        public static bool IsStrict(ulong titleId) => titleId == AcnhTitleId;

        public sealed class SyncState
        {
            /// <summary>Révision du cloud à la dernière synchro réussie (envoi ou téléchargement).</summary>
            public ulong Rev { get; set; }

            /// <summary>Empreinte du contenu local à ce moment-là.</summary>
            public string LocalHash { get; set; } = "";
        }

        public enum LaunchAction
        {
            /// <summary>Rien à demander : lancer.</summary>
            Nothing,
            /// <summary>Cloud vide, île locale : proposer de la lier au compte.</summary>
            OfferFirstUpload,
            /// <summary>Île dans le cloud, rien en local : proposer de la télécharger.</summary>
            OfferDownloadEmptyLocal,
            /// <summary>Le cloud a avancé, le local n'a pas bougé depuis la dernière synchro.</summary>
            OfferDownloadCloudNewer,
            /// <summary>Les deux ont changé, ou jamais synchronisé ici avec les deux présents.</summary>
            Conflict,
        }

        /// <summary>
        /// Décision au lancement. Ne choisit JAMAIS à la place du joueur quand une île serait perdue :
        /// seul le cas « rien n'a bougé ou seul le local a avancé » se passe de question.
        /// </summary>
        public static LaunchAction Decide(bool cloudExists, ulong cloudRev, bool localExists, string localHash, SyncState state)
        {
            if (!cloudExists)
            {
                return localExists ? LaunchAction.OfferFirstUpload : LaunchAction.Nothing;
            }

            if (!localExists)
            {
                return LaunchAction.OfferDownloadEmptyLocal;
            }

            if (state == null)
            {
                return LaunchAction.Conflict;
            }

            if (state.Rev == cloudRev)
            {
                return LaunchAction.Nothing; // local identique ou plus avancé : l'envoi suivra
            }

            return string.Equals(state.LocalHash, localHash, StringComparison.Ordinal)
                ? LaunchAction.OfferDownloadCloudNewer
                : LaunchAction.Conflict;
        }

        /// <summary>La plus récente des deux, pour la proposer en premier dans le conflit.</summary>
        public static bool CloudIsNewer(DateTime cloudUtc, DateTime localUtc) => cloudUtc >= localUtc;

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

        /// <summary>Identifiant de cette installation (« ryujinx:3fa2c1d0 »), affiché à l'autre machine.</summary>
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
