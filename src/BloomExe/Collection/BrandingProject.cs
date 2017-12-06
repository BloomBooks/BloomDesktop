﻿using System.Collections.Generic;
using System.IO;
using L10NSharp;

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
		public string Key;
		public static IEnumerable<BrandingProject> GetProjectChoices()
		{
			var brandingDirectory = BloomFileLocator.GetDirectoryDistributedWithApplication("branding");
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
