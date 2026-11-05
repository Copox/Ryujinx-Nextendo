using System.Net.Sockets;

namespace Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres
{
    // ErrorCode/NativeErrorCode contain host errno values on Unix. The enum
    // preserves the existing guest mappings across macOS, Linux and Windows.
    internal static class ResolverErrorConverter
    {
        public static NetDbError ToNetDbError(SocketException exception)
        {
            return exception.SocketErrorCode switch
            {
                SocketError.HostNotFound => NetDbError.HostNotFound,
                SocketError.TryAgain => NetDbError.TryAgain,
                SocketError.NoRecovery => NetDbError.NoRecovery,
                SocketError.NoData => NetDbError.NoData,
                _ => NetDbError.Internal,
            };
        }

        public static GaiError ToGaiError(SocketException exception, GaiError fallback)
        {
            return exception.SocketErrorCode switch
            {
                SocketError.HostNotFound => GaiError.NoData,
                SocketError.TimedOut => GaiError.Again,
                _ => fallback,
            };
        }
    }
}
