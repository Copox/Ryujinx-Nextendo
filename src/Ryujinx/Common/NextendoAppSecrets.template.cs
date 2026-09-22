namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// [Nextendo] Application credential template. The official release bakes the registered
    /// application token here into NextendoAppSecrets.cs (which is gitignored). In custom/dev
    /// builds, this defaults to empty string and can be filled with a developer app id from
    /// https://nextendo.network/developers.
    /// </summary>
    public static class NextendoAppSecrets
    {
        public const string AppToken = "";
    }
}
