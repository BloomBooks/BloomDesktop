using Bloom.Book;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using System;
using System.Text;

public class BookSettings
{
	public BookSettings()
	{
		PageStylesCss = "";
	}


	[JsonProperty("pageStylesCss")]
	public string PageStylesCss;


	public static BookSettings FromString(string json)
	{
		var ps = new BookSettings();
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
		catch (Exception e) { throw new ApplicationException("book-settings of this book may be corrupt", e); }
	}


	[JsonIgnore]
	public string Json => JsonConvert.SerializeObject(this);

	public static string BookSettingsPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("book-settings.json");
	}

	public void WriteToFolder(string bookFolderPath)
	{
		var bookSettingsPath = BookSettingsPath(bookFolderPath);
		try
		{
			RobustFile.WriteAllText(bookSettingsPath, Json);
		}
		catch (Exception e)
		{
			ErrorReport.NotifyUserOfProblem(e, "Bloom could not save your publish settings.");
		}
	}

	/// <summary>
	/// Make a BookSettings by reading the json file in the book folder.
	/// If some exception is thrown while trying to do that, or if it doesn't exist,
	/// just return a default BookSettings.
	/// </summary>
	/// <param name="bookFolderPath"></param>
	/// <returns></returns>
	public static BookSettings FromFolder(string bookFolderPath)
	{
		var bookSettingsPath = BookSettingsPath(bookFolderPath);
		BookSettings ps;

		if (TryReadSettings(bookSettingsPath, out BookSettings result))
			ps = result;
		else
		{
			// We could implement a backup strategy like for MetaData, but I don't
			// think it's worth it. It's not that likely we will lose these, or very critical
			// if we do.
			return new BookSettings();
		}
		return ps;
	}

	private static bool TryReadSettings(string path, out BookSettings result)
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

}
