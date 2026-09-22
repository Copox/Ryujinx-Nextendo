namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Application credential template. The official release bakes the registered
    /// application token and Ed25519 private key here into NextendoAppSecrets.cs (which is
    /// gitignored). In custom/dev builds, this defaults to empty strings and can be filled with
    /// a developer app id from https://nextendo.network/developers. AppPrivateKeyB64 stays
    /// empty for a dev build: NextendoAttestation.BuildHeaderValue() simply omits the signed
    /// header when unset, same as AppToken already does for the plain one.
    /// </summary>
    public static class NextendoAppSecrets
    {
        public const string AppToken = "";
        public const string AppPrivateKeyB64 = "";
    }
}
