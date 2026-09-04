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
        /// Features an e2e test asked for on the command line (--experimental-features, a
        /// comma-separated list of tokens), or the empty string.
        ///
        /// A test cannot turn one of these on the way a person does: they live in the Advanced tab
        /// of the collection Settings dialog, which is a WinForms surface CDP cannot reach. Nor can
        /// it write the setting, because Settings.Default lives in one user.config per build
        /// version, shared with the developer's own Bloom (see AUTOMATION-DEBT.md, "Every Bloom of
        /// one build shares one user.config"), so a test that saved a feature would leave it on for
        /// them. This reads the answer from the command line instead: nothing is saved, and the
        /// setting dies with the process. Honoured only under --e2e; Program refuses the argument
        /// without it. A feature named here stays enabled for the whole run: SetValue edits only
        /// the saved setting, so it cannot turn such a feature off, and a test that needs the
        /// feature off must launch without the token.
        /// </summary>
        private static string TokensFromE2eCommandLine =>
            Program.RunningE2eTests ? Program.StartupExperimentalFeatures ?? "" : "";

        public static string TokensOfEnabledFeatures
        {
            get
            {
                var saved = Settings.Default.EnabledExperimentalFeatures ?? "";
                var fromCommandLine = TokensFromE2eCommandLine;
                if (string.IsNullOrEmpty(fromCommandLine))
                    return saved;
                if (string.IsNullOrEmpty(saved))
                    return fromCommandLine;
                return saved + "," + fromCommandLine;
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
                if (!IsFeatureEnabled(featureName))
                    Settings.Default.EnabledExperimentalFeatures += "," + featureName;
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
