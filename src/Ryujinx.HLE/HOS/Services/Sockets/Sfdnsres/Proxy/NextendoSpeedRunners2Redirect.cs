using System;

namespace Ryujinx.HLE.HOS.Services.Sockets.Sfdnsres.Proxy
{
    internal static class NextendoSpeedRunners2Redirect
    {
        internal static bool IsServiceHost(string host, ulong programId)
        {
            return programId == 0x01004CC023582000UL &&
                   (string.Equals(host, "api.braincloudservers.com", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "ns.photonengine.io", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "api.gameanalytics.com", StringComparison.OrdinalIgnoreCase));
        }
    }
}
