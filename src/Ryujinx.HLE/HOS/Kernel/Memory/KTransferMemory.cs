using Ryujinx.Common;
using Ryujinx.HLE.HOS.Kernel.Common;
using Ryujinx.HLE.HOS.Kernel.Process;
using Ryujinx.Horizon.Common;
using System;

namespace Ryujinx.HLE.HOS.Kernel.Memory
{
    class KTransferMemory : KAutoObject
    {
        private KProcess _creator;

        // TODO: Remove when we no longer need to read it from the owner directly.
        public KProcess Creator => _creator;

        private readonly KPageList _pageList;

        public ulong Address { get; private set; }
        public ulong Size { get; private set; }

        public KMemoryPermission Permission { get; private set; }

        private bool _hasBeenInitialized;
        private bool _isMapped;

        public KTransferMemory(KernelContext context) : base(context)
        {
            _pageList = new KPageList();
        }

        public KTransferMemory(KernelContext context, SharedMemoryStorage storage) : base(context)
        {
            _pageList = storage.GetPageList();
            Permission = KMemoryPermission.ReadAndWrite;

            _hasBeenInitialized = true;
            _isMapped = false;
        }

        public Result Initialize(ulong address, ulong size, KMemoryPermission permission)
        {
            KProcess creator = KernelStatic.GetCurrentProcess();

            _creator = creator;

            Result result = creator.MemoryManager.BorrowTransferMemory(_pageList, address, size, permission);

            if (result != Result.Success)
            {
                return result;
            }

            creator.IncrementReferenceCount();

            Permission = permission;
            Address = address;
            Size = size;
            _hasBeenInitialized = true;
            _isMapped = false;

            return result;
        }

        public Result MapIntoProcess(
            KPageTableBase memoryManager,
            ulong address,
            ulong size,
            KProcess process,
            KMemoryPermission permission)
        {
            if (_pageList.GetPagesCount() != BitUtils.DivRoundUp<ulong>(size, KPageTableBase.PageSize))
            {
                return KernelResult.InvalidSize;
            }

            if (permission != Permission || _isMapped)
            {
                return KernelResult.InvalidState;
            }

            MemoryState state = Permission == KMemoryPermission.None ? MemoryState.TransferMemoryIsolated : MemoryState.TransferMemory;

            Result result = memoryManager.MapPages(address, _pageList, state, KMemoryPermission.ReadAndWrite);

            if (result == Result.Success)
            {
                _isMapped = true;
            }

            return result;
        }

        public Result UnmapFromProcess(
            KPageTableBase memoryManager,
            ulong address,
            ulong size,
            KProcess process)
        {
            if (_pageList.GetPagesCount() != BitUtils.DivRoundUp<ulong>(size, (ulong)KPageTableBase.PageSize))
            {
                return KernelResult.InvalidSize;
            }

            MemoryState state = Permission == KMemoryPermission.None ? MemoryState.TransferMemoryIsolated : MemoryState.TransferMemory;

            Result result = memoryManager.UnmapPages(address, _pageList, state);

            if (result == Result.Success)
            {
                _isMapped = false;
            }

            return result;
        }

        protected override void Destroy()
        {
            if (_hasBeenInitialized)
            {
                // [Nextendo, HOME Menu work] A program that ended (a game closed from the HOME Menu)
                // gets its memory back whole, borrowed parts included: a service that closes its
                // handle later has nothing to give back.
                if (!_isMapped && _creator.State is not (Process.ProcessState.Exiting or Process.ProcessState.Exited) &&
                    _creator.MemoryManager.UnborrowTransferMemory(Address, Size, _pageList) != Result.Success)
                {
                    throw new InvalidOperationException("Unexpected failure restoring transfer memory attributes.");
                }

                _creator.ResourceLimit?.Release(LimitableResource.TransferMemory, 1);
                _creator.DecrementReferenceCount();
            }
        }
    }
}
