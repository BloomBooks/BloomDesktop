using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using L10NSharp;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.WritingSystems;
using Palaso.WritingSystems;
using Palaso.Extensions;

namespace Bloom.Collection
{

	public class NewCollectionSettings : CollectionSettings
	{
		public string PathToSettingsFile;
	}

	/// <summary>
	/// A library corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class CollectionSettings
	{
		private string _language1Iso639Code;
		private LookupIsoCodeModel _lookupIsoCode = new LookupIsoCodeModel();
		private Dictionary<string, string> _isoToLangNameDictionary = new Dictionary<string, string>();


		/// <summary>
		/// for moq in unit tests only
		/// </summary>
		public CollectionSettings()
		{
			XMatterPackName = "Factory";
			Language2Iso639Code = "en";
			AllowNewBooks = true;
		}

		public CollectionSettings(NewCollectionSettings collectionInfo)
			:this(collectionInfo.PathToSettingsFile)
		{
			AllowNewBooks = true;
			DefaultLanguage1FontName = GetDefaultFontName();

			Language1Iso639Code = collectionInfo.Language1Iso639Code;
			Language2Iso639Code = collectionInfo.Language2Iso639Code;
			Language3Iso639Code = collectionInfo.Language3Iso639Code;
			Language1Name = collectionInfo.Language1Name;
			Country = collectionInfo.Country;
			Province = collectionInfo.Province;
			District = collectionInfo.District;
			IsSourceCollection = collectionInfo.IsSourceCollection;
			XMatterPackName = collectionInfo.XMatterPackName;
			Save();
		}

		/// <summary>
		/// can be used whether the library exists already, or not
		/// </summary>
		public CollectionSettings(string desiredOrExistingSettingsFilePath)
		{
			AllowNewBooks = true;
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			CollectionName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
			var libraryDirectory = Path.GetDirectoryName(desiredOrExistingSettingsFilePath);
			var parentDirectoryPath = Path.GetDirectoryName(libraryDirectory);

			if (File.Exists(desiredOrExistingSettingsFilePath))
			{
				Load();
			}
			else
			{
				if (!Directory.Exists(parentDirectoryPath))
					Directory.CreateDirectory(parentDirectoryPath);

				if (!Directory.Exists(libraryDirectory))
					Directory.CreateDirectory(libraryDirectory);

				Save();
			}
		}

		#region Persisted properties

		//these are virtual for the sake of the unit test mock framework
		public virtual string Language1Iso639Code
		{
			get { return _language1Iso639Code; }
			set
			{
				_language1Iso639Code = value;
				Language1Name = GetLanguage1Name(Language2Iso639Code);
			}
		}

		public virtual string Language2Iso639Code { get; set; }
		public virtual string Language3Iso639Code { get; set; }
		public virtual string Language1Name { get; set; }

		/// <summary>
		/// Intended for making shell books and templates, not vernacular
		/// </summary>
		public virtual bool IsSourceCollection { get; set; }

		public string GetLanguage1Name(string inLanguage)
		{
			if(!string.IsNullOrEmpty(this.Language1Name))
				return Language1Name;

			Iso639LanguageCode exactLanguageMatch = _lookupIsoCode.GetExactLanguageMatch(Language1Iso639Code);
			if (exactLanguageMatch == null)
				return "L1-Unknown-" + Language1Iso639Code;
			return GetLanguageNameInUILangIfPossible(exactLanguageMatch.Name, inLanguage);
		}

		public string GetLanguage2Name(string inLanguage)
		{
			try
			{
				//TODO: we are going to need to show "French" as "Français"... but if the name isn't available, we should have a fall-back mechanism, at least to english
				//So, we'd rather have GetBestLanguageMatch()


				//profiling showed we were spending a lot of time looking this up, hence the cache
				if (!_isoToLangNameDictionary.ContainsKey(Language2Iso639Code))
				{
					_isoToLangNameDictionary.Add(Language2Iso639Code, _lookupIsoCode.GetExactLanguageMatch(Language2Iso639Code).Name);
				}

				return GetLanguageNameInUILangIfPossible(_isoToLangNameDictionary[Language2Iso639Code], inLanguage);

			}
			catch (Exception)
			{
				Debug.Fail("check this out. BL-193 Reproduction");
				// a user reported this, and I saw it happen once: had just installed 0.8.38, made a new vernacular
				//project, added a picture dictionary, the above failed (no debugger, so I don't know why).
				return "L2-Unknown-" + Language2Iso639Code;
			}
		}

		private string GetLanguageNameInUILangIfPossible(string name, string codeOfDesiredLanguage)
		{
			//we don't have a general way to get the language names translated yet. But at least we can show a few languages properly

			if (codeOfDesiredLanguage == "fr" && name == "French")
				return "français";
			return name;
		}

		public string GetLanguage3Name(string inLanguage)
		{
			try
			{
				if (string.IsNullOrEmpty(Language3Iso639Code))
					return string.Empty;

				//profiling showed we were spending a lot of time looking this up, hence the cache
				if (!_isoToLangNameDictionary.ContainsKey(Language3Iso639Code))
				{
					_isoToLangNameDictionary.Add(Language3Iso639Code, _lookupIsoCode.GetExactLanguageMatch(Language3Iso639Code).Name);
				}
				return GetLanguageNameInUILangIfPossible(_isoToLangNameDictionary[Language3Iso639Code],inLanguage);
			}
			catch (Exception)
			{
				return "L2N-Unknown-" + Language3Iso639Code;
			}
		}
		#endregion

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			Logger.WriteEvent("Saving Collection Settings");

			XElement library = new XElement("Collection");
			library.Add(new XAttribute("version", "0.2"));
			library.Add(new XElement("Language1Iso639Code", Language1Iso639Code));
			library.Add(new XElement("Language1Name", Language1Name));
			library.Add(new XElement("DefaultLanguage1FontName", DefaultLanguage1FontName));
			library.Add(new XElement("Language2Iso639Code", Language2Iso639Code));
			library.Add(new XElement("Language3Iso639Code", Language3Iso639Code));
			library.Add(new XElement("IsSourceCollection", IsSourceCollection.ToString()));
			library.Add(new XElement("XMatterPack", XMatterPackName));
			library.Add(new XElement("Country", Country));
			library.Add(new XElement("Province", Province));
			library.Add(new XElement("District", District));

			library.Save(SettingsFilePath);

			SavesettingsCollectionStylesCss();
		}

		private void SavesettingsCollectionStylesCss()
		{
			string path = FolderPath.CombineForPath("settingsCollectionStyles.css");

			try
			{
				var sb = new StringBuilder();
				sb.AppendLine("/* These styles are controlled by the Settings dialog box in Bloom. */");
				sb.AppendLine("/* They many be over-ridden by rules in customCollectionStyles.css or customBookStyles.css */");
				sb.AppendLine();
				sb.AppendLine("BODY");
				sb.AppendLine("{");
				sb.AppendLine(" font-family: '" + DefaultLanguage1FontName + "';");
				sb.AppendLine("}");
				File.WriteAllText(path, sb.ToString());
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom was unable to update this file: {0}",path);
			}
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{
				XElement library = XElement.Load(SettingsFilePath);
				Language1Iso639Code = GetValue(library, "Language1Iso639Code", /* old name */GetValue(library, "Language1Iso639Code", ""));
				Language2Iso639Code = GetValue(library, "Language2Iso639Code",  /* old name */GetValue(library, "National1Iso639Code", "en"));
				Language3Iso639Code = GetValue(library, "Language3Iso639Code",  /* old name */GetValue(library, "National2Iso639Code", ""));
				XMatterPackName = GetValue(library, "XMatterPack", "Factory");
				Language1Name = GetValue(library, "Language1Name",  /* old name */GetValue(library, "LanguageName", ""));
				DefaultLanguage1FontName = GetValue(library, "DefaultLanguage1FontName", GetDefaultFontName());

				Country = GetValue(library, "Country", "");
				Province = GetValue(library, "Province", "");
				District = GetValue(library, "District", "");
				AllowNewBooks = GetBoolValue(library, "AllowNewBooks", true);
				IsSourceCollection = GetBoolValue(library, "IsSourceCollection", GetBoolValue(library, "IsShellLibrary" /*the old name*/, GetBoolValue(library, "IsShellMakingProject" /*an even older name*/, false)));
			}
			catch (Exception e)
			{
				ApplicationException a = new ApplicationException(File.ReadAllText(SettingsFilePath), e);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
																 "There was an error reading the library settings file.  Please report this error to the developers. To get access to your books, you should make a new library, then copy your book folders from this broken library into the new one, then run Bloom again.");
				throw;
			}

			try
			{
				string oldcustomCollectionStylesPath = FolderPath.CombineForPath("collection.css");
				if(File.Exists(oldcustomCollectionStylesPath))
				{
					string newcustomCollectionStylesPath = FolderPath.CombineForPath("customCollectionStyles.css");

					File.Move(oldcustomCollectionStylesPath, newcustomCollectionStylesPath);
				}
			}
			catch (Exception)
			{
				//ah well, we tried, no big deal, only a couple of beta testers used this old name
			}
		}

		private bool GetBoolValue(XElement library, string id, bool defaultValue)
		{
			string s = GetValue(library, id, defaultValue.ToString());
			bool b;
			bool.TryParse(s, out b);
			return b;
		}

		private string GetValue(XElement document, string id, string defaultValue)
		{
			var nodes = document.Descendants(id);
			if (nodes != null && nodes.Count() > 0)
				return nodes.First().Value;
			else
			{
				return defaultValue;
			}
		}


		public virtual string CollectionName { get; protected set; }

		[XmlIgnore]
		public string FolderPath
		{
			get { return Path.GetDirectoryName(SettingsFilePath); }
		}

		[XmlIgnore]
		public string SettingsFilePath { get; set; }

		/// <summary>
		/// for the "Factory-XMatter.htm", this would be named "Factory"
		/// </summary>
		virtual public string XMatterPackName { get; set; }

		virtual public string Country { get; set; }
		virtual public string Province { get; set; }
		virtual public string District { get; set; }

		public string VernacularCollectionNamePhrase
		{
			get
			{
				//review: in June 2013, I made it just use the collectionName regardless of the type. I wish I'd make a comment with the previous approach
				//explaining *why* we would wnat to just say, for example, "Foobar Books". Probably for some good reason.
				//But it left us with the weird situation of being able to chang the collection name in the settings, and have that only affect the  title
				//bar of the window (and the on-disk name). People wanted to change to a language name they want to see. (We'll probably have to do something
				//to enable that anyhow because it shows up elsewhere, but this is a step).
				//if(IsSourceCollection)
					return CollectionName;
				//var fmt = L10NSharp.LocalizationManager.GetString("CollectionTab.Vernacular Collection Heading", "{0} Books", "The {0} is where we fill in the name of the Vernacular");
				//return string.Format(fmt, Language1Name);
			}
		}

		public string DefaultLanguage1FontName { get; set; }

		public bool AllowNewBooks { get; set; }

		public bool AllowDeleteBooks
		{
			get { return AllowNewBooks; } //at the moment, we're combining these two concepts; we can split them if a good reason to comes along
		}


		public static string GetPathForNewSettings(string parentFolderPath, string newCollectionName)
		{
			return parentFolderPath.CombineForPath(newCollectionName, newCollectionName + ".bloomCollection");
		}

		public void AttemptSaveAsToNewName(string name)
		{
			name = name.Trim().SanitizeFilename('-');
			var newName = name + ".BloomCollection";

			if (name == CollectionName.Trim())
				return;

			//first try renaming the collections settings file
			try
			{   Save();
				var current = SettingsFilePath;
				var newPath = Path.Combine(Path.GetDirectoryName(SettingsFilePath), newName);
				File.Move(current, newPath);
				SettingsFilePath = newPath;
			}
			catch (Exception e1)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e1,
																 "Bloom was unable to change the collection name to '{0}'",
																 name);
			}

			//now try renaming the directory
			try
			{
				var existingDirectory = Path.GetDirectoryName(SettingsFilePath);
				var parentDirectory = Path.GetDirectoryName(existingDirectory);
				var newDirectory = Path.Combine(parentDirectory, name);
				Directory.Move(existingDirectory, newDirectory);
				SettingsFilePath = Path.Combine(newDirectory, newName);
			}
			catch (Exception e2)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e2,
																 "Bloom did change the collection name to {0}, but was unable to change name of the folder. Perhaps there is already a folder with that name?",
																 name);
			}
		}

		private string GetDefaultFontName()
		{
			foreach (var candidate in new[] { "Andika", "Gentium", "Charis", "Paduak"/*Myanmar*/})
			{
				string lower = candidate.ToLower();
				if (FontFamily.Families.FirstOrDefault(f =>
														   {
															   return f.Name.ToLower() == lower;
														   }) != null)
				{
					return candidate;
				}
			}
			return SystemFonts.DefaultFont.Name;
		}

		public static string FindSettingsFileInFolder(string folderPath)
		{
			try
			{
				return Directory.GetFiles(folderPath, "*.BloomCollection").First();
			}
			catch (Exception)
			{
				throw new ApplicationException(string.Format("Bloom expected to find a .BloomCollectionFile in {0}, but there isn't one.", folderPath));
			}
		}
	}
}
