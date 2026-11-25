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
        public const string kTables = "tables";

        /// <summary>
        /// The comma-separated tokens of the features that are enabled: normally the saved setting,
        /// but under --e2e only what the command line asked for (--experimental-features), which
        /// may be nothing.
        ///
        /// A person turns a feature on in the Experimental page of the Collection Settings dialog,
        /// which a test can drive too (collection-settings-experimental.spec.ts). A test that only
        /// needs a feature on from the start names it on the command line instead; Program refuses
        /// the argument without --e2e. Under --e2e a feature named there stays enabled for the
        /// whole run, and one saved on the Experimental page is saved but does not take effect:
        /// SetValue edits only the saved setting.
        /// </summary>
        public static string TokensOfEnabledFeatures
        {
            get
            {
                if (Program.RunningE2eTests)
                    return Program.StartupExperimentalFeatures ?? "";
                return Settings.Default.EnabledExperimentalFeatures ?? "";
            }
        }

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
            // remove obsolete experimental features that have gone mainstream
            SetValue("webView2", false);
            // App Building and AI image editing stopped being experimental in 6.5 (BL-16731);
            // they are now gated only by the subscription tier.
            SetValue("app-builder", false);
            SetValue("ai-image-editing", false);

            // In June 2025, the only one of these sources was the Picture Dictionary,
            // and it had issues which had been introduced in an earlier version.
            // We decided just to turn it off. We could clean up the code above, but
            // I'm actually leaving the code as much like it previously was as possible
            // so we can reinstate it easily if we want to.
            SetValue(kExperimentalSourceBooks, false);

            // The two settings we changed directly above (rather than through SetValue) need
            // saving, or the "once and once only" migration would run again next time.
            Settings.Default.Save();
        }

        public static void SetValue(string featureName, bool isEnabled)
        {
            if (isEnabled)
            {
                // Asks the saved setting itself, not IsFeatureEnabled: under --e2e that answers
                // from the command line, so a saved token would look absent and be saved again.
                var saved = Settings.Default.EnabledExperimentalFeatures ?? "";
                if (!HasToken(saved, featureName))
                    Settings.Default.EnabledExperimentalFeatures = saved + "," + featureName;
            }
            else
            {
                // Replace does no harm if the feature is not found in the string.
                Settings.Default.EnabledExperimentalFeatures = Settings
                    .Default.EnabledExperimentalFeatures.Replace(featureName, "")
                    .Replace(",,", ",");
            }
            Settings.Default.EnabledExperimentalFeatures =
                Settings.Default.EnabledExperimentalFeatures.Trim(',');
            Settings.Default.Save();
        }

        public static bool IsFeatureEnabled(string featureName)
        {
            // Reads TokensOfEnabledFeatures rather than the setting, so a feature an e2e test
            // named on the command line counts as enabled everywhere this is asked.
            return HasToken(TokensOfEnabledFeatures, featureName);
        }

        /// <summary>
        /// Whether the comma-separated list names exactly this token. A substring test would let
        /// a token that merely contains a feature's name ("not-team-collections") enable it.
        /// </summary>
        private static bool HasToken(string commaSeparatedTokens, string token)
        {
            return commaSeparatedTokens.Split(',').Select(t => t.Trim()).Contains(token);
        }
    }
}
