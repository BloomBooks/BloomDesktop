using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;
using Bloom.ToPalaso;

namespace Bloom
{
	/// <summary>
	/// A project corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class ProjectSettings
	{
	   // public delegate ProjectSettings Factory(string desiredOrExistingFilePath);

		#region Persisted roperties

		public string Iso639Code { get; set; }

		#endregion

		public ProjectSettings(NewProjectInfo projectInfo)
			:this(projectInfo.PathToSettingsFile)
		{
			Iso639Code = projectInfo.Iso639Code;
			Save();
		}
		/// <summary>
		/// can be used whether the project exists already, or not
		/// </summary>
		public ProjectSettings(string desiredOrExistingSettingsFilePath)
		{
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			Name = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
			var projectDirectory = Path.GetDirectoryName(desiredOrExistingSettingsFilePath);
			var parentDirectoryPath = Path.GetDirectoryName(projectDirectory);

			if (File.Exists(desiredOrExistingSettingsFilePath))
			{
				Load();
			}
			else
			{
				if (!Directory.Exists(parentDirectoryPath))
					Directory.CreateDirectory(parentDirectoryPath);

				if (!Directory.Exists(projectDirectory))
					Directory.CreateDirectory(projectDirectory);

				Save();
			}
		}

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			XElement project = new XElement("Project");
			project.Add(new XAttribute("version", "0.1"));
			project.Add(new XElement("Iso639Code", Iso639Code));
			project.Save(SettingsFilePath);
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			XElement project = XElement.Load(SettingsFilePath);
			var elements = project.Descendants("Iso639Code");
			Iso639Code = elements.First().Value;
		}

		/// <summary>
		/// Note: while the folder name will match the settings file name when it is first
		/// created, it needn't remain that way. A user can copy the project folder, rename
		/// it "blah (old)", whatever, and this will still work.
		/// </summary>

		[XmlIgnore]
		public string Name { get; protected set; }

		[XmlIgnore]
		public string FolderPath
		{
			get { return Path.GetDirectoryName(SettingsFilePath); }
		}

		[XmlIgnore]
		public string SettingsFilePath { get; set; }


		public static string GetPathForNewSettings(string parentFolderPath, string newProjectName)
		{
			return Path.Combine(parentFolderPath, newProjectName + ".bloomLibrary");
		}
	}

	public class NewProjectInfo
	{
		public string PathToSettingsFile;
		public string Iso639Code;
	}
}
