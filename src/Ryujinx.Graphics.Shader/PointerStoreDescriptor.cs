namespace Ryujinx.Graphics.Shader
{
    public readonly struct PointerStoreDescriptor
    {
        public readonly byte SbCbSlot;
        public readonly ushort SbCbOffset;

        public readonly int PointerStride;
        public readonly int PointerOffset;

        public readonly int StoreOffset;
        public readonly uint Value;

        public PointerStoreDescriptor(int sbCbSlot, int sbCbOffset, int pointerStride, int pointerOffset, int storeOffset, uint value)
        {
            SbCbSlot = (byte)sbCbSlot;
            SbCbOffset = (ushort)sbCbOffset;
            PointerStride = pointerStride;
            PointerOffset = pointerOffset;
            StoreOffset = storeOffset;
            Value = value;
        }

        public bool HasSamePointer(in PointerStoreDescriptor other)
        {
            return SbCbSlot == other.SbCbSlot &&
                   SbCbOffset == other.SbCbOffset &&
                   PointerStride == other.PointerStride &&
                   PointerOffset == other.PointerOffset;
        }
    }
}
