using Ryujinx.Ava.Common;
using Ryujinx.Ava.Common.Locale;
using System.Text.Json.Serialization;

namespace Ryujinx.Ava.Systems.AppLibrary
{
    public partial class ApplicationData
    {
        private ServiceListing _serviceListing;
        private int? _servicePlayers;

        [JsonIgnore] public bool WasListedOnService { get; private set; }
        [JsonIgnore] public bool IsAvailableOnService => _serviceListing?.Enabled == true;
        [JsonIgnore] public string ServiceVersion => _serviceListing?.Version ?? "";
        [JsonIgnore] public string ServiceBadge => IsAvailableOnService ? (ServiceVersion.Length == 0 ? "Nextendo Network" : $"Nextendo Network \u00b7 v{ServiceVersion}") : "";
        [JsonIgnore] public int? ServicePlayersOnline => _servicePlayers;
        [JsonIgnore] public bool HasServicePlayersCount => IsAvailableOnService && _servicePlayers.HasValue;
        [JsonIgnore]
        public string ServicePlayersText => _servicePlayers.HasValue
            ? LocaleManager.Instance.UpdateAndGetDynamicValue(
                _servicePlayers == 1 ? LocaleKeys.Dialog_Nextendo_PlayerOnlineFormat : LocaleKeys.Dialog_Nextendo_PlayersOnlineFormat,
                _servicePlayers.Value)
            : "";

        // Called on the UI thread. Existing built-in titles always keep their original path.
        public void ApplyServiceAvailability(ServiceListing listing, int? players)
        {
            if (IsNextendoCompatible)
                listing = null;
            if (listing != null)
                WasListedOnService = true;
            if (listing?.Enabled != true || players < 0)
                players = null;
            if (_serviceListing == listing && _servicePlayers == players)
                return;
            _serviceListing = listing;
            _servicePlayers = players;
            foreach (string property in new[] { nameof(IsAvailableOnService), nameof(ServiceVersion), nameof(ServiceBadge),
                nameof(ServicePlayersOnline), nameof(HasServicePlayersCount), nameof(ServicePlayersText) })
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));
            }
        }
    }
}
