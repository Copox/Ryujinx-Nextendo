using Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres;
using System;
using System.Net;
using System.Net.Sockets;

namespace Ryujinx.Tests.SfdnsresErrors
{
    internal static class Program
    {
        private static int _checks;
        private static int _failures;

        private static void Check<T>(T actual, T expected, string message) where T : Enum
        {
            _checks++;
            if (!actual.Equals(expected))
            {
                _failures++;
                Console.Error.WriteLine($"FAIL {message}: expected {expected}, got {actual}");
            }
        }

        private static void CheckError(SocketError error, NetDbError expectedNetDb, GaiError expectedGai)
        {
            SocketException exception = new((int)error);
            Check(ResolverErrorConverter.ToNetDbError(exception), expectedNetDb, $"{error} netdb (native {exception.ErrorCode})");
            Check(ResolverErrorConverter.ToGaiError(exception, GaiError.Overflow), expectedGai, $"{error} getaddrinfo (native {exception.ErrorCode})");
        }

        private static int Main()
        {
            CheckError(SocketError.HostNotFound, NetDbError.HostNotFound, GaiError.NoData);
            CheckError(SocketError.TryAgain, NetDbError.TryAgain, GaiError.Overflow);
            CheckError(SocketError.NoRecovery, NetDbError.NoRecovery, GaiError.Overflow);
            CheckError(SocketError.NoData, NetDbError.NoData, GaiError.Overflow);
            CheckError(SocketError.TimedOut, NetDbError.Internal, GaiError.Again);
            CheckError(SocketError.AccessDenied, NetDbError.Internal, GaiError.Overflow);
            CheckError(SocketError.ConnectionRefused, NetDbError.Internal, GaiError.Overflow);
            CheckError(SocketError.SocketError, NetDbError.Internal, GaiError.Overflow);

            SocketException unknown = new((int)SocketError.SocketError);
            Check(ResolverErrorConverter.ToGaiError(unknown, GaiError.AddressFamily), GaiError.AddressFamily, "reverse-lookup fallback is retained");
            Check(ResolverErrorConverter.ToGaiError(unknown, GaiError.Success), GaiError.Success, "unmapped error does not invent a replacement");

            // Exercise an actual malformed-name system lookup as well as the
            // platform-specific exceptions above. No Nextendo service is used.
            try
            {
                Dns.GetHostEntry("invalid name with spaces");
                _failures++;
                Console.Error.WriteLine("FAIL malformed name unexpectedly resolved");
            }
            catch (SocketException exception)
            {
                Check(ResolverErrorConverter.ToNetDbError(exception), NetDbError.HostNotFound, "real malformed-name netdb result");
                Check(ResolverErrorConverter.ToGaiError(exception, GaiError.Overflow), GaiError.NoData, "real malformed-name getaddrinfo result");
            }

            Console.WriteLine($"sfdnsres errors: {_checks} checks, {_failures} failures");
            return _failures == 0 ? 0 : 1;
        }
    }
}
