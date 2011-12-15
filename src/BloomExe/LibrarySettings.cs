using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Bloom
{
	/// <summary>
	/// A library corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class LibrarySettings
	{
		#region Persisted roperties

		public string VernacularIso639Code { get; set; }
		public string NationalLanguage1Iso639Code { get; set; }
		public string NationalLanguage2Iso639Code { get; set; }
		public string LanguageName { get; set; }
		public virtual bool IsShellLibrary { get; set; }

		#endregion

		/// <summary>
		/// for moq in unit tests only
		/// </summary>
		public LibrarySettings()
		{
		}

		public LibrarySettings(NewLibraryInfo libraryInfo)
			:this(libraryInfo.PathToSettingsFile)
		{
			VernacularIso639Code = libraryInfo.VernacularIso639Code;
			NationalLanguage1Iso639Code = libraryInfo.NationalLanguage1Iso639Code;
			LanguageName = libraryInfo.LanguageName;
			IsShellLibrary = libraryInfo.IsShellLibary;
			Save();
		}
		/// <summary>
		/// can be used whether the library exists already, or not
		/// </summary>
		public LibrarySettings(string desiredOrExistingSettingsFilePath)
		{
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			LibraryName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
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

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			XElement library = new XElement("Library");
			library.Add(new XAttribute("version", "0.1"));
			library.Add(new XElement("VernacularIso639Code", VernacularIso639Code));
			library.Add(new XElement("National1Iso639Code", NationalLanguage1Iso639Code));
			library.Add(new XElement("National2Iso639Code", NationalLanguage1Iso639Code));
			library.Add(new XElement("LanguageName", LanguageName));
			library.Add(new XElement("IsShellLibrary", IsShellLibrary.ToString()));
			library.Save(SettingsFilePath);
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{

				XElement library = XElement.Load(SettingsFilePath);
				var vernacular = library.Descendants("VernacularIso639Code");
				if(vernacular ==null || vernacular.Count()==0)
					vernacular = library.Descendants("Iso639Code");//old version (dec 2011, v 0.3)
				VernacularIso639Code = vernacular.First().Value;

				var national = library.Descendants("National1Iso639Code");
				if (national != null && national.Count() > 0)
					NationalLanguage1Iso639Code = national.First().Value;
				else
				{
					NationalLanguage1Iso639Code = "en";
				}

				national = library.Descendants("National2Iso639Code");
				if (national != null && national.Count() > 0)
					NationalLanguage2Iso639Code = national.First().Value;
				else
				{
					NationalLanguage2Iso639Code = "";
				}

				LanguageName = library.Descendants("LanguageName").First().Value;
				bool isShellMakingLibrary;
				var isShellMakingElement = library.Descendants("IsShellLibrary");
				if (isShellMakingElement != null && isShellMakingElement.Count() > 0)
				{
					bool.TryParse(isShellMakingElement.First().Value, out isShellMakingLibrary);
					IsShellLibrary = isShellMakingLibrary;
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
																 "Ouch! There was an error reading the library settings file.  Please report this error to the developers; consider emailing them the offending file, {0}. To get access to your books, you should make a new library, then copy your book folders from this broken library into the new one, then run Bloom again.",
																 SettingsFilePath);
				throw;
			}
		}


		public string LibraryName { get; protected set; }

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
		public string NameOfXMatterTemplate
		{
			get { return "Factory"; }
		}

		public static string GetPathForNewSettings(string parentFolderPath, string newLibraryName)
		{
			return Path.Combine(parentFolderPath, newLibraryName + ".bloomLibrary");
		}
	}

	public class NewLibraryInfo
	{
		public string PathToSettingsFile;
		public string VernacularIso639Code;
		public string NationalLanguage1Iso639Code;
		public string NationalLanguage2Iso639Code;
		public string LanguageName;
		public bool IsShellLibary;
	}
}
