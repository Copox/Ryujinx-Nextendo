namespace Ryujinx.Ava.Common
{
    /// <summary>
    /// The application identity this build presents to Nextendo. A release carries it in
    /// <see cref="NextendoAppSecrets.AppToken"/> (written at release time); a development build,
    /// which carries none, reads it from a local file next to the program
    /// (nextendo_client_id.txt, never committed) so that it can sign in as well.
    /// </summary>
    public static class NextendoAppIdentity
    {
        public static string Id => _id.Value;

        private static readonly System.Lazy<string> _id = new(() =>
        {
            string baked = NextendoAppSecrets.AppToken;

            if (!string.IsNullOrEmpty(baked))
            {
                return baked;
            }

            try
            {
                string file = System.IO.Path.Combine(System.AppContext.BaseDirectory, "nextendo_client_id.txt");

                return System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : "";
            }
            catch (System.Exception)
            {
                return "";
            }
        });
    }
}
