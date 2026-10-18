using Ryujinx.HLE.HOS.Ipc;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Account.Acc.AccountService
{
    internal static class CachedAccountInfo
    {
        public const int Size = 0x68;

        // SDK CachedNintendoAccountInfo::GetCountry : chaîne de 4 octets au plus en 0x20
        // (GetGender 8 en 0x10, GetRegion 4 en 0x24), relevé dans le sdk.
        public const int CountryOffset = 0x20;

        public static byte[] Create(string country = "")
        {
            // Operator-requested shared compatibility date, NOT a stored account DOB.
            // SDK GetBirthday reads a NUL-terminated string at offset 0 (capacity 0x10).
            // Unknown fields stay empty; the account ID is a separate IPC scalar.
            byte[] data = new byte[Size];
            Encoding.ASCII.GetBytes("2000-01-01").CopyTo(data, 0);

            // Pays du compte (deux lettres ISO, terminé par 0) : Mario Kart 8 Deluxe en tire le
            // drapeau de son profil en ligne. Vide = comportement d'avant (pas de drapeau).
            string cc = Ryujinx.Common.Configuration.NextendoCountry.Normalize(country);
            if (cc.Length == 2)
            {
                Encoding.ASCII.GetBytes(cc).CopyTo(data, CountryOffset);
            }

            return data;
        }

        public static ResultCode PrepareResponse(IpcMessage request, IpcMessage response,
            ulong pointerAddress, ulong pointerSize)
        {
            if (request.RecvListBuff.Count == 0 || pointerAddress == 0 || pointerSize < Size)
            {
                return ResultCode.InvalidBufferSize;
            }

            // Received C descriptors are not client addresses we can dereference.
            // Reply from server-owned memory via an X descriptor, including when
            // a B buffer is present (ServerBase does not prepare that combination).
            if (request.ReceiveBuff.Count > 0 && request.ReceiveBuff[0].Size > 0 &&
                request.ReceiveBuff[0].Position == 0)
            {
                return ResultCode.InvalidBuffer;
            }

            response.PtrBuff.Clear();
            response.PtrBuff.Add(new IpcPtrBuffDesc(pointerAddress, 0, Size));
            return ResultCode.Success;
        }
    }
}
