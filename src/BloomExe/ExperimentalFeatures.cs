using System;
using System.Linq;
using Bloom.Properties;

namespace Bloom
{
    /// <summary>
    /// Wrap the handling of the user settings related to experimental features in Bloom.
    /// </summary>
    public static class ExperimentalFeatures
    {
        public const string kExperimentalSourceBooks = "experimental-source-books";
        public const string kTeamCollections = "team-collections";
        public const string kAppBuilder = "app-builder";

        /// <summary>
        /// Token for the cloud-backed Team Collections experimental feature. Wired to the
        /// "Cloud Team Collections (experimental)" checkbox in Settings -> Advanced (see
        /// CollectionSettingsDialog.PendingAllowCloudTeamCollection / CollectionSettingsApi).
        /// </summary>
        public const string kCloudTeamCollections = "cloud-team-collections";

        public static string TokensOfEnabledFeatures =>
            Settings.Default.EnabledExperimentalFeatures;

        public static void MigrateFromOldSettings()
        {
            if (Settings.Default.EnabledExperimentalFeatures == null)
                Settings.Default.EnabledExperimentalFeatures = "";
            // migrate old value once and once only.
            if (Settings.Default.ShowExperimentalFeatures)
            {
                SetValue(kExperimentalSourceBooks, true);
                Settings.Default.ShowExperimentalFeatures = false;
            }
            // remove obsolete experimental feature that has gone mainstream
            SetValue("webView2", false);

            // In June 2025, the only one of these sources was the Picture Dictionary,
            // and it had issues which had been introduced in an earlier version.
            // We decided just to turn it off. We could clean up the code above, but
            // I'm actually leaving the code as much like it previously was as possible
            // so we can reinstate it easily if we want to.
            SetValue(kExperimentalSourceBooks, false);
        }

        public static void SetValue(string featureName, bool isEnabled)
        {
            if (isEnabled)
            {
                if (!IsFeatureEnabled(featureName))
                    Settings.Default.EnabledExperimentalFeatures += "," + featureName;
            }
            else
            {
                // Remove the EXACT token, not a substring: a raw Replace() would turn
                // "cloud-team-collections" into "cloud-" when disabling "team-collections"
                // (the two feature tokens share a substring).
                Settings.Default.EnabledExperimentalFeatures = string.Join(
                    ",",
                    (Settings.Default.EnabledExperimentalFeatures ?? "")
                        .Split(',')
                        .Where(token => token != featureName && token.Length > 0)
                );
            }
            Settings.Default.EnabledExperimentalFeatures =
                Settings.Default.EnabledExperimentalFeatures.Trim(',');
        }

        public static bool IsFeatureEnabled(string featureName)
        {
            // Exact-token match, not substring: the setting is a comma-separated list and one
            // token can be a substring of another ("team-collections" is contained in
            // "cloud-team-collections"), so a Contains() check reported the cloud feature as
            // enabling the folder feature too.
            return (Settings.Default.EnabledExperimentalFeatures ?? "")
                .Split(',')
                .Contains(featureName);
        }
    }
}
