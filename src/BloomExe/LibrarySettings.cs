using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using Palaso.UI.WindowsForms.WritingSystems;
using Palaso.WritingSystems;

namespace Bloom
{
	/// <summary>
	/// A library corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class LibrarySettings
	{
		private string _vernacularIso639Code;


		#region Persisted roperties

		//these are virtual for the sake of the unit test mock framework
		public virtual string VernacularIso639Code
		{
			get { return _vernacularIso639Code; }
			set
			{
				_vernacularIso639Code = value;
				VernacularLanguageName = GetVernacularName(NationalLanguage1Iso639Code);
			}
		}

		public virtual string NationalLanguage1Iso639Code { get; set; }
		public virtual string NationalLanguage2Iso639Code { get; set; }
		public virtual string VernacularLanguageName { get; set; }

		public virtual bool IsShellLibrary { get; set; }

		public string GetVernacularName(string inLanguage)
		{
			var lookup = new LookupIsoCodeModel();
			Iso639LanguageCode exactLanguageMatch = lookup.GetExactLanguageMatch(VernacularIso639Code);
			if (exactLanguageMatch == null)
				return "???";
			return exactLanguageMatch.Name;
		}

		public string GetNationalLanguage1Name(string inLanguage)
		{
			var lookup = new LookupIsoCodeModel();
			//TODO: we are going to need to show "French" as "Français"... but if the name isn't available, we should have a fall-back mechanism, at least to english
			//So, we'd rather have GetBestLanguageMatch()
			return lookup.GetExactLanguageMatch(NationalLanguage1Iso639Code).Name;
		}
		public string GetNationalLanguage2Name(string inLanguage)
		{
			if(string.IsNullOrEmpty(NationalLanguage2Iso639Code))
				return string.Empty;
			var lookup = new LookupIsoCodeModel();
			return lookup.GetExactLanguageMatch(NationalLanguage2Iso639Code).Name;
		}
		#endregion

		/// <summary>
		/// for moq in unit tests only
		/// </summary>
		public LibrarySettings()
		{
			XMatterPackName = "Factory";
			NationalLanguage1Iso639Code = "en";
		}

		public LibrarySettings(NewLibraryInfo libraryInfo)
			:this(libraryInfo.PathToSettingsFile)
		{
			VernacularIso639Code = libraryInfo.VernacularIso639Code;
			NationalLanguage1Iso639Code = libraryInfo.NationalLanguage1Iso639Code;
			NationalLanguage2Iso639Code = libraryInfo.NationalLanguage2Iso639Code;
			VernacularLanguageName = libraryInfo.LanguageName;
			IsShellLibrary = libraryInfo.IsShellLibary;
			XMatterPackName = libraryInfo.XMatterPackName;
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
			library.Add(new XElement("National2Iso639Code", NationalLanguage2Iso639Code));
			library.Add(new XElement("LanguageName", VernacularLanguageName));
			library.Add(new XElement("IsShellLibrary", IsShellLibrary.ToString()));
			library.Add(new XElement("XMatterPack", XMatterPackName));
			library.Add(new XElement("Country", Country));
			library.Add(new XElement("Province", Province));
			library.Add(new XElement("District", District));
			library.Save(SettingsFilePath);
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{
				XElement library = XElement.Load(SettingsFilePath);
				VernacularIso639Code = GetValue(library, "VernacularIso639Code", "");
				NationalLanguage1Iso639Code = GetValue(library, "National1Iso639Code", "en");
				NationalLanguage2Iso639Code = GetValue(library, "National2Iso639Code", "");
				XMatterPackName = GetValue(library, "XMatterPack", "Factory");
				VernacularLanguageName = GetValue(library, "LanguageName", "");
				Country = GetValue(library, "Country","");
				Province = GetValue(library, "Province", "");
				District = GetValue(library, "District", "");
				IsShellLibrary = GetBoolValue(library, "IsShellLibrary", false);
			}
			catch (Exception e)
			{
				ApplicationException a = new ApplicationException(File.ReadAllText(SettingsFilePath), e);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
																 "There was an error reading the library settings file.  Please report this error to the developers. To get access to your books, you should make a new library, then copy your book folders from this broken library into the new one, then run Bloom again.");
				throw;
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
		virtual public string XMatterPackName { get; set; }

		virtual public string Country { get; set; }
		virtual public string Province { get; set; }
		virtual public string District { get; set; }


		public static string GetPathForNewSettings(string parentFolderPath, string newLibraryName)
		{
			return Path.Combine(parentFolderPath, newLibraryName + ".bloomLibrary");
		}
	}

	public class NewLibraryInfo
	{
		public string PathToSettingsFile;
		public string VernacularIso639Code;
		public string NationalLanguage1Iso639Code="en";
		public string NationalLanguage2Iso639Code;
		public string LanguageName;
		public string XMatterPackName= "Factory";
		public bool IsShellLibary;
	}
}
