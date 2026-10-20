using LibHac;
using LibHac.Fs;
using LibHac.Fs.Shim;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.IO;
using System.Threading.Tasks;
using Outcome = Ryujinx.Common.Configuration.NextendoIslandCloud.Outcome;
using Path = System.IO.Path;

namespace Ryujinx.HLE.HOS.Services.Olsc
{
    /// <summary>[Nextendo] olsc:u -> NextendoIslandCloud : sauvegarde Device du jeu en cours et codes de résultat.</summary>
    static class NextendoOlscCloud
    {
        public static ResultCode ToResult(Outcome outcome) => outcome switch
        {
            Outcome.Ok or Outcome.Unchanged => ResultCode.Success,
            Outcome.Unavailable => ResultCode.NextendoUnavailable,
            Outcome.Refused => ResultCode.NextendoServerRefused,
            Outcome.NoLocalSave => ResultCode.NextendoNoLocalSave,
            Outcome.InvalidIsland => ResultCode.NextendoInvalidIsland,
            _ => ResultCode.NextendoNetworkError,
        };

        /// <summary>Dossier racine de la sauvegarde Device du jeu (nand/user/save/&lt;id&gt;), ou null.</summary>
        public static string FindDeviceSaveRoot(Switch device, ulong titleId)
        {
            try
            {
                HorizonClient client = device.System.LibHacHorizonManager.RyujinxClient;
                SaveDataFilter filter = SaveDataFilter.Make(titleId, SaveDataType.Device, default, default, default);
                Result rc = client.Fs.FindSaveDataWithFilter(out SaveDataInfo info, SaveDataSpaceId.User, in filter);
                if (rc.IsFailure())
                {
                    return null;
                }

                string root = Path.Combine(VirtualFileSystem.GetNandPath(), "user", "save", info.SaveDataId.ToString("x16"));
                return Directory.Exists(root) ? root : null;
            }
            catch (Exception ex)
            {
                Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: device save lookup failed ({ex.Message})");
                return null;
            }
        }

        public static async Task<ResultCode> UploadAsync(Switch device, ulong titleId, string key)
        {
            return ToResult(await NextendoIslandCloud.UploadAsync(titleId, FindDeviceSaveRoot(device, titleId), key, onlyIfChanged: false));
        }

        public static async Task<ResultCode> DownloadAsync(Switch device, ulong titleId, string key)
        {
            return ToResult(await NextendoIslandCloud.DownloadAsync(titleId, FindDeviceSaveRoot(device, titleId), key));
        }
    }
}
