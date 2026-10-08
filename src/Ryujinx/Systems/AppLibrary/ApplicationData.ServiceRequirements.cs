using Ryujinx.Ava.Common;
using Ryujinx.Common;
using System;
using System.Linq;
using System.Text.Json.Serialization;
namespace Ryujinx.Ava.Systems.AppLibrary
{
    public partial class ApplicationData
    {
        [JsonIgnore] public ServiceVersionStatus ServiceVersionStatus { get; private set; } = ServiceVersionStatus.UnknownCatalog;
        [JsonIgnore] public ServiceRequirementsStatus ServiceRequirementsStatus { get; private set; } = ServiceRequirementsStatus.UnknownCatalog;
        [JsonIgnore] public string ServiceUpdateAdvice { get; private set; } = "";
        [JsonIgnore]
        public string ServiceTooltip => string.Join("\n", new[] {ServiceBadge, ServiceRequirementsStatus switch {
   ServiceRequirementsStatus.UnknownCatalog => "Service information unavailable.",
   ServiceRequirementsStatus.NotSatisfied => "Accepted versions: "+string.Join(", ",_serviceListing?.Accepted??Array.Empty<string>()),
   ServiceRequirementsStatus.NotEvaluable => "This client cannot evaluate all requirements.",
   ServiceRequirementsStatus.Satisfied => "Service requirements satisfied.",
   _ => "Availability not announced for this title.",
  }, ServiceUpdateAdvice}.Where(s => !string.IsNullOrEmpty(s)));
        public void ApplyServiceRequirements(bool valid, ServiceClientRelease[] releases, string channel, string platform, string architecture)
        {
            ServiceVersionStatus version = valid ? (_serviceListing?.Enabled == true ? _serviceListing.Evaluate(Version) : ServiceVersionStatus.NotApplicable) : ServiceVersionStatus.UnknownCatalog;
            ServiceRequirementsStatus requirements = !valid ? ServiceRequirementsStatus.UnknownCatalog : _serviceListing?.Enabled != true ? ServiceRequirementsStatus.NotApplicable : !_serviceListing.CanEvaluate ? ServiceRequirementsStatus.NotEvaluable : version == ServiceVersionStatus.Accepted ? ServiceRequirementsStatus.Satisfied : version == ServiceVersionStatus.NotAccepted ? ServiceRequirementsStatus.NotSatisfied : ServiceRequirementsStatus.NotEvaluable;
            string advice = "";
            if (requirements == ServiceRequirementsStatus.NotEvaluable && _serviceListing?.CanEvaluate == false)
            {
                if (releases == null)
                    advice = "Could not check for a compatible client update.";
                else
                {
                    ServiceClientRelease release = ServiceCatalogParser.Recommend(releases, _serviceListing.Capabilities, channel, platform, architecture);
                    ServiceClientRelease current = releases.FirstOrDefault(r => r.Version == ReleaseInformation.Version && r.Channel == channel && r.Platform == platform && r.Architecture == architecture);
                    advice = release == null ? "No compatible client release is published for this channel and platform." : current == null ? "Release " + release.Version + " declares support for these requirements." : release.Sequence > current.Sequence ? "Compatible client update available: " + release.Version : "No newer compatible client release is published.";
                }
            }
            bool changed = version != ServiceVersionStatus || requirements != ServiceRequirementsStatus || advice != ServiceUpdateAdvice;
            ServiceVersionStatus = version;
            ServiceRequirementsStatus = requirements;
            ServiceUpdateAdvice = advice;
            if (changed)
            foreach (string property in new[] { nameof(ServiceVersionStatus), nameof(ServiceRequirementsStatus), nameof(ServiceUpdateAdvice) })
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(property));
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ServiceTooltip)));
        }
    }
}
