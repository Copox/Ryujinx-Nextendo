using LibHac.Bcat;
using Ryujinx.Horizon.Common;
using Ryujinx.Horizon.Sdk.Bcat;
using Ryujinx.Horizon.Sdk.Sf;
using Ryujinx.Horizon.Sdk.Sf.Hipc;
using System;

namespace Ryujinx.Horizon.Bcat.Ipc
{
    // The delivery cache of a title that never received anything: it opens, and lists nothing.
    // Handed to system applets (the HOME Menu looks into the eShop's and Nintendo Switch
    // Online's caches), which abort on an error where a console answers with an empty storage.
    partial class EmptyDeliveryCacheStorageService : IDeliveryCacheStorageService
    {
        private static readonly Result _notFound = new(122, 2);

        [CmifCommand(0)]
        public Result CreateFileService(out IDeliveryCacheFileService service)
        {
            service = null;

            return _notFound;
        }

        [CmifCommand(1)]
        public Result CreateDirectoryService(out IDeliveryCacheDirectoryService service)
        {
            service = null;

            return _notFound;
        }

        [CmifCommand(10)]
        public Result EnumerateDeliveryCacheDirectory(out int count, [Buffer(HipcBufferFlags.Out | HipcBufferFlags.MapAlias)] Span<DirectoryName> directoryNames)
        {
            count = 0;

            return Result.Success;
        }
    }
}
