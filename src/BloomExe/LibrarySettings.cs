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

		public string Iso639Code { get; set; }
		public string LanguageName { get; set; }
		public bool IsShellLibrary { get; set; }

		#endregion

		public LibrarySettings(NewLibraryInfo libraryInfo)
			:this(libraryInfo.PathToSettingsFile)
		{
			Iso639Code = libraryInfo.Iso639Code;
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
			library.Add(new XElement("Iso639Code", Iso639Code));
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
				Iso639Code = library.Descendants("Iso639Code").First().Value;
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


		public static string GetPathForNewSettings(string parentFolderPath, string newLibraryName)
		{
			return Path.Combine(parentFolderPath, newLibraryName + ".bloomLibrary");
		}
	}

	public class NewLibraryInfo
	{
		public string PathToSettingsFile;
		public string Iso639Code;
		public string LanguageName;
		public bool IsShellLibary;
	}
}
