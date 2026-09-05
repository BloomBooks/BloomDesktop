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
        public const string kTables = "tables";

        /// <summary>
        /// The name of the environment variable an e2e test uses to turn experimental features on
        /// for the Bloom it launches, and for that process alone.
        /// </summary>
        public const string kE2eEnvironmentVariable = "BLOOM_E2E_EXPERIMENTAL_FEATURES";

        /// <summary>
        /// The value an e2e test puts in that variable to say "no experimental features at all".
        /// A token is needed because an empty environment variable cannot be told from an absent
        /// one on Windows, and "absent" has to go on meaning "leave the saved setting alone" for
        /// every Bloom that is not under --e2e.
        /// </summary>
        public const string kE2eNoFeatures = "none";

        /// <summary>
        /// Features an e2e test asked for, as a comma-separated list of tokens, or the empty
        /// string.
        ///
        /// A test cannot turn one of these on the way a person does: they live in the Advanced tab
        /// of the collection Settings dialog, which is a WinForms surface CDP cannot reach. Nor can
        /// it write the setting, because Settings.Default lives in one user.config per build
        /// version, shared with the developer's own Bloom (see AUTOMATION-DEBT.md, "Every Bloom of
        /// one build shares one user.config"), so a test that saved a feature would leave it on for
        /// them. This reads the answer from the environment instead: nothing is saved, and the
        /// setting dies with the process. Honoured only under --e2e.
        /// </summary>
        private static string TokensFromE2eEnvironment =>
            Program.RunningE2eTests
                ? Environment.GetEnvironmentVariable(kE2eEnvironmentVariable) ?? ""
                : "";

        /// <summary>
        /// The tokens of the features that are on, comma-separated.
        ///
        /// Under --e2e the environment variable is the WHOLE answer, rather than something added
        /// to the saved setting: a run says which features it wants and gets exactly those, and a
        /// run that names none (kE2eNoFeatures) gets none. That matters because the saved setting
        /// lives in a user.config shared with the developer's own Bloom (see AUTOMATION-DEBT.md,
        /// "Every Bloom of one build shares one user.config"), so a test of how Bloom behaves with
        /// an experiment turned OFF could not be written at all while the developer's own saved
        /// setting could turn it back on.
        /// </summary>
        public static string TokensOfEnabledFeatures
        {
            get
            {
                var fromEnvironment = TokensFromE2eEnvironment;
                if (!string.IsNullOrEmpty(fromEnvironment))
                    return fromEnvironment == kE2eNoFeatures ? "" : fromEnvironment;
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
            // named in the environment counts as enabled everywhere this is asked.
            return TokensOfEnabledFeatures.Contains(featureName);
        }
    }
}
