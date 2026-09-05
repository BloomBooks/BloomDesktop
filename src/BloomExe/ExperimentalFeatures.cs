using System;
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

        /// <summary>
        /// The comma-separated tokens of the features that are enabled: normally the saved setting,
        /// but under --e2e only what the command line asked for (--experimental-features), which
        /// may be nothing.
        ///
        /// A test cannot turn a feature on the way a person does: they live in the Advanced tab of
        /// the collection Settings dialog, which is a WinForms surface CDP cannot reach. Nor can it
        /// write the setting, because Settings.Default lives in one user.config per build version,
        /// shared with the developer's own Bloom (see AUTOMATION-DEBT.md, "Every Bloom of one build
        /// shares one user.config"), so a test that saved a feature would leave it on for them.
        /// So under --e2e the command line is the whole answer: nothing is saved, the setting dies
        /// with the process, and the developer's own saved experiments do not reach the run, so a
        /// test that needs a feature OFF gets it off by not naming the token. Program refuses the
        /// argument without --e2e. A feature named here stays enabled for the whole run: SetValue
        /// edits only the saved setting, so it cannot turn such a feature off.
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
                if (!saved.Contains(featureName))
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
            return TokensOfEnabledFeatures.Contains(featureName);
        }
    }
}
