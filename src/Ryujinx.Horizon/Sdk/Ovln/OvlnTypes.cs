using System.Runtime.InteropServices;

namespace Ryujinx.Horizon.Sdk.Ovln
{
    // nn::ovln::SourceName : tampon opaque de 0x10 octets (l'applet de surimpression
    // l'envoie seul a AddSource : 0x10 octets de donnees), on ne le lit jamais.
    [StructLayout(LayoutKind.Sequential, Size = 0x10, Pack = 1)]
    struct SourceName
    {
    }

    // nn::ovln::QueueAttribute : u32 QueueLength + u32 reserve.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct QueueAttribute
    {
        public uint QueueLength;
        public uint Reserved;
    }

    // nn::ovln::RawMessage : u32 Tag + u32 DataSize + u8[0x78] Data (0x80).
    // Receive renvoie un message vide (DataSize 0), Send ignore le contenu.
    [StructLayout(LayoutKind.Sequential, Size = 0x80, Pack = 1)]
    struct RawMessage
    {
    }

    // nn::ovln::SendOption : u8 EnqueuePosition + u8 OverflowOption + 6 reserves.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct SendOption
    {
        public byte EnqueuePosition;
        public byte OverflowOption;
        public ushort Reserved0;
        public uint Reserved1;
    }
}
