using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel.Threading;
using Ryujinx.Horizon.Common;
using System;
using System.Threading.Tasks;

namespace Ryujinx.HLE.HOS.Services.Olsc
{
    /// <summary>
    /// [Nextendo] Objet asynchrone d'olsc, commandes lues dans le client d'ACNH 3.0.3 (vtables
    /// 0x47f9208, 0x47f9328, 0x47f9470) : 0 Get (le résultat de l'opération), 1 Cancel,
    /// 2 HasDone -> b8, 3 GetSystemEvent -> handle&lt;copy&gt;. Le jeu attend l'événement, appelle
    /// HasDone puis Get (0x5bb398).
    /// </summary>
    class IOlscAsyncResult : IpcService
    {
        private readonly KEvent _event;
        private volatile bool _done;
        private ResultCode _result = ResultCode.NextendoNotDone;

        public IOlscAsyncResult(ServiceCtx context, Func<Task<ResultCode>> work)
        {
            _event = new KEvent(context.Device.System.KernelContext);

            Task.Run(async () =>
            {
                ResultCode result;
                try
                {
                    result = await work();
                }
                catch (Exception ex)
                {
                    Logger.Warning?.Print(LogClass.ServiceOlsc, $"[Nextendo] olsc: {ex.Message}");
                    result = ResultCode.NextendoNetworkError;
                }

                _result = result;
                _done = true;
                _event.ReadableEvent.Signal();
            });
        }

        [CommandCmif(0)]
        // Get()
        public ResultCode Get(ServiceCtx context)
        {
            return _done ? _result : ResultCode.NextendoNotDone;
        }

        [CommandCmif(1)]
        // Cancel()
        public ResultCode Cancel(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [CommandCmif(2)]
        // HasDone() -> b8
        public ResultCode HasDone(ServiceCtx context)
        {
            context.ResponseData.Write(_done);

            return ResultCode.Success;
        }

        [CommandCmif(3)]
        // GetSystemEvent() -> handle<copy>
        public ResultCode GetSystemEvent(ServiceCtx context)
        {
            if (context.Process.HandleTable.GenerateHandle(_event.ReadableEvent, out int handle) != Result.Success)
            {
                throw new InvalidOperationException("Out of handles!");
            }

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            return ResultCode.Success;
        }
    }

    /// <summary>
    /// [Nextendo] Résultat de ListDownloadableSaveDataBackupInfoAsync (cmd 25). Lu dans ACNH 3.0.3
    /// (0x5bacd8, 0x5bae14, appelant 0x25cce10) : 100 -> u32 nombre d'éléments ; 101 (u32 indice de
    /// départ, tampon de sortie d'éléments de 0x410 octets) -> u32 nombre écrit. Le jeu lit au plus 5
    /// éléments, un par un.
    /// </summary>
    class IAsyncListDownloadableSaveDataBackupInfo : IOlscAsyncResult
    {
        private readonly Func<byte[][]> _entries;

        public IAsyncListDownloadableSaveDataBackupInfo(ServiceCtx context, Func<Task<ResultCode>> work, Func<byte[][]> entries) : base(context, work)
        {
            _entries = entries;
        }

        [CommandCmif(100)]
        // GetCount() -> u32
        public ResultCode GetCount(ServiceCtx context)
        {
            context.ResponseData.Write((uint)(_entries()?.Length ?? 0));

            return ResultCode.Success;
        }

        [CommandCmif(101)]
        // GetList(u32 offset, buffer<SaveDataBackupInfo, 6>) -> u32 count
        public ResultCode GetList(ServiceCtx context)
        {
            uint offset = context.RequestData.ReadUInt32();
            byte[][] entries = _entries() ?? [];

            ulong position = context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Position : 0;
            ulong size = context.Request.ReceiveBuff.Count > 0 ? context.Request.ReceiveBuff[0].Size : 0;

            uint written = 0;
            for (ulong i = offset; i < (ulong)entries.Length && (written + 1) * (ulong)Ryujinx.Common.Configuration.NextendoIslandSync.BackupInfoSize <= size; i++)
            {
                context.Memory.Write(position + written * (ulong)Ryujinx.Common.Configuration.NextendoIslandSync.BackupInfoSize, entries[i]);
                written++;
            }

            context.ResponseData.Write(written);

            return ResultCode.Success;
        }
    }

    /// <summary>
    /// [Nextendo] Résultat de DownloadSaveDataBackupAsync (cmd 26) et UploadSaveDataBackupAsync
    /// (cmd 27) : la commande 100 rend 0x30 octets (0x5bb180). ACNH ne l'appelle pas ; l'opération
    /// est rendue terminée d'un bloc, donc une progression nulle.
    /// </summary>
    class IAsyncTransferSaveDataBackup : IOlscAsyncResult
    {
        public IAsyncTransferSaveDataBackup(ServiceCtx context, Func<Task<ResultCode>> work) : base(context, work) { }

        [CommandCmif(100)]
        // GetProgress() -> bytes<0x30>
        public ResultCode GetProgress(ServiceCtx context)
        {
            context.ResponseData.Write(new byte[0x30]);

            return ResultCode.Success;
        }
    }
}
