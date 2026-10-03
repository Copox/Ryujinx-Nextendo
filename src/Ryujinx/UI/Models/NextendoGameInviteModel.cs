namespace Ryujinx.Ava.UI.Models
{
    // [Nextendo] One pending game invitation in the friends window.
    public class NextendoGameInviteModel
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string FriendCode { get; init; } = "";

        /// <summary>The game and the minutes left before the invitation expires.</summary>
        public string Detail { get; init; } = "";

        public byte[] Image { get; init; }
    }
}
