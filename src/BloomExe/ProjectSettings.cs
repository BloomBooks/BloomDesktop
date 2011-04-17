using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Bloom
{
	/// <summary>
	/// A project corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class ProjectSettings
	{
		#region Persisted roperties

		public string Iso639Code { get; set; }
		public string LanguageName { get; set; }
		public bool IsShellMakingProject { get; set; }

		#endregion

		public ProjectSettings(NewProjectInfo projectInfo)
			:this(projectInfo.PathToSettingsFile)
		{
			Iso639Code = projectInfo.Iso639Code;
			LanguageName = projectInfo.LanguageName;
			IsShellMakingProject = projectInfo.IsShellMakingProject;
			Save();
		}
		/// <summary>
		/// can be used whether the project exists already, or not
		/// </summary>
		public ProjectSettings(string desiredOrExistingSettingsFilePath)
		{
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			ProjectName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
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
			project.Add(new XElement("LanguageName", LanguageName));
			project.Add(new XElement("IsShellMakingProject", IsShellMakingProject.ToString()));
			project.Save(SettingsFilePath);
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{

				XElement project = XElement.Load(SettingsFilePath);
				Iso639Code = project.Descendants("Iso639Code").First().Value;
				LanguageName = project.Descendants("LanguageName").First().Value;
				bool isShellMakingProject;
				var isShellMakingElement = project.Descendants("IsShellMakingProject");
				if (isShellMakingElement != null && isShellMakingElement.Count() > 0)
				{
					bool.TryParse(isShellMakingElement.First().Value, out isShellMakingProject);
					IsShellMakingProject = isShellMakingProject;
				}
			}
			catch (Exception e)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
																 "Ouch! There was an error reading the project settings file.  Please report this error to the developers; consider emailing them the offending file, {0}. To get access to your books, you should make a new project, then copy your book folders from this broken project into the new one, then run Bloom again.",
																 SettingsFilePath);
				throw;
			}
		}


		public string ProjectName { get; protected set; }

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
		public string LanguageName;
		public bool IsShellMakingProject;
	}
}
