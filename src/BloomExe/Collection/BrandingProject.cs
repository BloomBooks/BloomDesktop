using System.Collections.Generic;
using System.IO;
using L10NSharp;
using SIL.IO;

namespace Bloom.Collection
{
	/// <summary>
	/// A BrandingProject, known in the UI as a "Bloom Enterprise Project", is an organization or effort
	///  using Bloom that has registered with us in order to get various things into every book created
	/// by that group or effort (project). These include logos & Creative Commons license. Under the
	/// Bloom Enterprise system, this also unlocks some advanced publishing capabilities like comprehension
	/// questions and bookshelves.
	///
	/// At the moment, this class is just a thing to provide a localized tostring() for the list of
	/// brandings, while the actual access to the bits of the brand go straight to the branding folder, which
	/// is shipped as part of Bloom.
	/// </summary>
	public class BrandingProject
	{
		// Map from legacy project keys (before we introduced branding codes) to a default
		// code which is used if that key is encountered in a settings file without a branding code.
		// In most cases they give an effectively infinite expiration time (3000AD or so).
		static Dictionary<string, string> _legacyBrandings;

		public static Dictionary<string, string> LegacyBrandings
		{
			get
			{
				if (_legacyBrandings == null)
				{
					_legacyBrandings = new Dictionary<string, string>();
					_legacyBrandings["CLB"] = "CLB-361769-361977";
					_legacyBrandings["GRN-REACH"] = "GRN-REACH-361769-364250";
					_legacyBrandings["Papua Literacy"] = "Papua Literacy-361769-368425";
					_legacyBrandings["PNG-RISE"] = "PNG-RISE-361769-363798";
					_legacyBrandings["SIL-International"] = "SIL-International-361769-371916";
					_legacyBrandings["SIL-LEAD"] = "SIL-LEAD-361769-363644";
					_legacyBrandings["SIL-PNG"] = "SIL-PNG-361769-363265";
					_legacyBrandings["UBB-GMIT"] = "UBB-GMIT-361769-363797";
					_legacyBrandings["USAID-Guatemala"] = "USAID-Guatemala-3465-10906";
					_legacyBrandings["Default"] = null;
					_legacyBrandings["Local Community"] = null;
				}

				return _legacyBrandings;
			}
		}
		public string Key;
		public bool IsLegacyBranding => LegacyBrandings.ContainsKey(Key);
		public string BrandingCode {
			get
			{
				string result;
				LegacyBrandings.TryGetValue(Key, out result);
				return result;
			}
	} 
		public static IEnumerable<BrandingProject> GetProjectChoices()
		{
			var brandingDirectory = FileLocationUtilities.GetDirectoryDistributedWithApplication("branding");
			foreach (var brandDirectory in Directory.GetDirectories(brandingDirectory))
			{
				var brand = new BrandingProject {Key = Path.GetFileName(brandDirectory)};
				yield return brand;
			}
		}

		// "none" is a better localization id than "default"
		private string LocalizationKey => "CollectionSettingsDialog.BookMakingTab.Branding." +
										this.Key == "Default" ? "None" : this.Key;

		// Originally we were just selecting a "branding". This has grown into selecting a "project', which
		// includes things like default copyright/license.
		// At this point, we aren't re-labeling everything in code or the settings file, just the UI.
		// As part of that, the key "Default", will be presented as "None".
		private string EnglishLabel => Key == "Default" ? "None" : Key;

		// show the localized label when these are listed in a combo box
		public override string ToString()
		{
			// Only brandings that NEED translation should be added to the English xliff file,
			// the others will just get the English label.
			return LocalizationManager.GetString(LocalizationKey, EnglishLabel);
		}
	}
}
