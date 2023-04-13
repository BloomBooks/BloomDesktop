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
		public const string kSpreadsheetImportExport = "spreadsheet-import-export";

		public static string TokensOfEnabledFeatures => Settings.Default.EnabledExperimentalFeatures;

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
				Settings.Default.EnabledExperimentalFeatures = Settings.Default.EnabledExperimentalFeatures
					.Replace(featureName, "")
					.Replace(",,", ",");
			}
			Settings.Default.EnabledExperimentalFeatures = Settings.Default.EnabledExperimentalFeatures.Trim(',');
		}

		public static bool IsFeatureEnabled(string featureName)
		{
			return Settings.Default.EnabledExperimentalFeatures.Contains(featureName);
		}
	}
}
