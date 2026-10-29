using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Ryujinx.HLE.HOS.Services.Ssl.SslService
{
    internal static class SslHandshakeGuard
    {
        // A peer closing/resetting TLS is an emulated SSL result. It must not
        // escape through reflection and terminate the emulator's IPC thread.
        internal static ResultCode Run(Action handshake, Action restoreSocketMode)
        {
            try
            {
                handshake();
                return ResultCode.Success;
            }
            catch (IOException exception)
            {
                return exception.InnerException is SocketException socketException
                    ? Translate(socketException.SocketErrorCode)
                    : ResultCode.ConnectionAbort;
            }
            catch (SocketException exception)
            {
                return Translate(exception.SocketErrorCode);
            }
            catch (AuthenticationException)
            {
                return ResultCode.ConnectionAbort;
            }
            finally
            {
                restoreSocketMode();
            }
        }

        private static ResultCode Translate(SocketError error) => error switch
        {
            SocketError.ConnectionReset => ResultCode.ConnectionReset,
            SocketError.TimedOut => ResultCode.Timeout,
            _ => ResultCode.ConnectionAbort,
        };
    }
}
