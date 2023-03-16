using Bloom;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using System;
using System.IO;
using System.Text;

public class AppearanceSettings
{
	public AppearanceSettings()
	{
		PresetName = "default";
		CoverColor = "yellow"; // book will migrate its legacy cover into this
	}

	[JsonProperty("presetName")]
	public string PresetName;

	[JsonProperty("coverColor")]
	public string CoverColor;


	public static AppearanceSettings FromString(string json)
	{
		var ps = new AppearanceSettings();
		return ps;
	}

	public static string AppearanceSettingsPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("appearance.css");
	}

	/// <summary>
	/// Make a AppearanceSettings by reading the json file in the book folder.
	/// If some exception is thrown while trying to do that, or if it doesn't exist,
	/// just return a default BookSettings.
	/// </summary>
	/// <param name="bookFolderPath"></param>
	/// <returns></returns>
	public static AppearanceSettings FromFolderOrNew(string bookFolderPath)
	{
		var appearanceSettingsPath = AppearanceSettingsPath(bookFolderPath);
		AppearanceSettings settings = new AppearanceSettings();
		if (RobustFile.Exists(appearanceSettingsPath))
		{
			
			var css = RobustFile.ReadAllText(appearanceSettingsPath);
			// regex to select the value of the --cover-color property in css
			var match = System.Text.RegularExpressions.Regex.Match(css, @"--cover-color:\s*(\w+)");
			if (match.Success)
			{
				settings.CoverColor = match.Groups[1].Value;
			}

			match = System.Text.RegularExpressions.Regex.Match(css, @"--preset-name:\s*""(\w+)""");
			if (match.Success)
			{
				settings.PresetName = match.Groups[1].Value;
			}

		} 
		
		return settings;
	}

	public void WriteAppearanceCss(string folder)
	{
		var targetPath = AppearanceSettingsPath(folder);

		if (Program.RunningHarvesterMode && RobustFile.Exists(targetPath))
		{
			// Would overwrite, but overwrite not allowed in Harvester mode.
			// Review: (this logic just copied from CreateOrUpdateDefaultLangStyles() above, I don't know if it's still valid)
			return;
		}

		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(":root{");
		cssBuilder.AppendLine($"--cover-color:{CoverColor};");
		cssBuilder.AppendLine($"--preset-name:\"{PresetName}\";");
		cssBuilder.AppendLine("}");
		if (!string.IsNullOrEmpty(PresetName))
		{
			var sourcePath = Path.Combine(ProjectContext.GetFolderContainingAppearancePresetFiles(), PresetName+".css");
			if (!RobustFile.Exists(sourcePath))
			{
				// TODO: We should toast I suppose?
			}
			else
			{
				cssBuilder.AppendLine($"/* From the current appearance preset, '{PresetName}' */");
				cssBuilder.AppendLine(RobustFile.ReadAllText(sourcePath, Encoding.UTF8));
			}
		}
		RobustFile.WriteAllText(targetPath, cssBuilder.ToString());
	}

	internal void Update(dynamic appearance)
	{
		this.CoverColor=appearance.coverColor;
		this.PresetName=appearance.presetName;
	}
}
