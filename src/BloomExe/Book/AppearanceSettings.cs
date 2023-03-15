using Bloom;
using Bloom.Book;
using Bloom.Utils;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using System;
using System.Drawing;
using System.IO;
using System.Text;

public class AppearanceSettings
{
	public AppearanceSettings()
	{
		CustomCssRules = "";
		CoverColor = ""; // book will migrate its legacy cover into this
		ImageTextGapMillimeters = 50; 
	}


	[JsonProperty("customCssRules")]
	public string CustomCssRules;

	[JsonProperty("coverColor")]
	public string CoverColor;

	[JsonProperty("imageTextGapMillimeters")]
	public int ImageTextGapMillimeters; // review is one enough? Or do we need horizontal vs verical?

	public static AppearanceSettings FromString(string json)
	{
		var ps = new AppearanceSettings();
		ps.LoadNewJson(json);
		return ps;
	}
	public void LoadNewJson(string json)
	{
		try
		{
			JsonConvert.PopulateObject(json, this,
				// Previously, various things could be null. As part of simplifying the use of BookSettings,
				// we now never have nulls; everything gets defaults when it is created.
				// For backwards capabilty, if the json we are reading has a null for a value,
				// do not override the default value that we already have loaded.
				new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Ignore });
		}
		catch (Exception e) { throw new ApplicationException("appearance.json of this book may be corrupt", e); }
	}


	[JsonIgnore]
	public string Json => JsonConvert.SerializeObject(this);

	public static string AppearanceSettingsPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("appearance.json");
	}

	public void WriteToFolder(string bookFolderPath)
	{

		var appearanceSettingsPath = AppearanceSettingsPath(bookFolderPath);
		try
		{
			RobustFile.WriteAllText(appearanceSettingsPath, Json);
		}
		catch (Exception e)
		{
			ErrorReport.NotifyUserOfProblem(e, "Bloom could not save your publish settings.");
		}
	}

	/// <summary>
	/// Make a AppearanceSettings by reading the json file in the book folder.
	/// If some exception is thrown while trying to do that, or if it doesn't exist,
	/// just return a default BookSettings.
	/// </summary>
	/// <param name="bookFolderPath"></param>
	/// <returns></returns>
	public static AppearanceSettings FromFolder(string bookFolderPath)
	{
		var appearanceSettingsPath = AppearanceSettingsPath(bookFolderPath);
		AppearanceSettings ps;

		if (TryReadSettings(appearanceSettingsPath, out AppearanceSettings result))
			ps = result;
		else
		{
			// We could implement a backup strategy like for MetaData, but I don't
			// think it's worth it. It's not that likely we will lose these, or very critical
			// if we do.
			return new AppearanceSettings();
		}
		return ps;
	}

	private static bool TryReadSettings(string path, out AppearanceSettings result)
	{
		result = null;
		if (!RobustFile.Exists(path))
			return false;
		try
		{
			result = FromString(RobustFile.ReadAllText(path, Encoding.UTF8));
			return true;
		}
		catch (Exception e)
		{
			Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);
			return false;
		}
	}


	public void WriteAppearanceCss(string folder)
	{
		var targetPath = Path.Combine(folder, "appearance.css");
		
		if (Program.RunningHarvesterMode && RobustFile.Exists(targetPath))
		{
			// Would overwrite, but overwrite not allowed in Harvester mode.
			// Review: (this logic just copied from CreateOrUpdateDefaultLangStyles() above, I don't know if it's still valid)
			return;
		}

		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(":root{");
		cssBuilder.AppendLine($"--cover-color:{CoverColor};");
		cssBuilder.AppendLine($"--image-text-gap:{ImageTextGapMillimeters}mm;");
		cssBuilder.AppendLine("}");
		if (!string.IsNullOrEmpty(CustomCssRules))
		{
			var sourcePath = Path.Combine(ProjectContext.GetFolderContainingPageStyleFiles(), CustomCssRules);
			if (!RobustFile.Exists(sourcePath))
			{
				// TODO: We should toast I suppose?
			}
			else
			{
				cssBuilder.AppendLine($"/* From the current CustomCssRules, {CustomCssRules} */");
				cssBuilder.AppendLine(RobustFile.ReadAllText(sourcePath, Encoding.UTF8));
			}
		}
		RobustFile.WriteAllText(targetPath, cssBuilder.ToString());
	}
}
