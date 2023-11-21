using Bloom;
using Bloom.Book;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SIL.Code;


/// <summary>
/// This class manages the appearance settings for a book. This includes generating the appearance.css file that is used at display time.
/// It also has responsibilities related to detecting and migrating problem legacy custom css files.
/// TODO: needs major revision descibed in BL-12856. Accordingly, I have not tried to review or improve this old version.
/// </summary>
public class AppearanceSettings
{
	public AppearanceSettings()
	{
		_properties = new ExpandoObject();
		if (_substitutinator == null)
		{
			_substitutinator = new AppearanceCustomCssToThemeSubstitutinator();
		}

		// copy in the default values from each definition
		foreach (var definition in propertyDefinitions)
		{
			definition.SetDefault(_properties); // this is a workaround using ref because c# doesn't have union types, or `any`, sigh
		}
	}

	public static string kDoShowValueForDisplay = "doShow-css-will-ignore-this-and-use-default"; // by using an illegal value, we just get a no-op rule, which is what we want
	public static string kHideValueForDisplay = "none";
	public static string kOverrideGroupsArrayKey = "groupsToOverrideFromParent"; // e.g. "coverFields, xmatter"
	private static AppearanceCustomCssToThemeSubstitutinator _substitutinator;
	internal dynamic _properties;
	

	// it's a big hassle working directly with the ExpandoObject. By casting it this way, you can do the things you expect.
	internal IDictionary<string, object> Properties => (IDictionary<string, object>)_properties;

	public dynamic TestOnlyPropertiesAccess { get { return _properties; } }

	public string FirstPossiblyOffendingCssFile;
	public string OffendingCssRule;
	public string SubstitutedCssFile;

	// create an array of properties and fill it in
	private PropertyDef[] propertyDefinitions = new PropertyDef[]
	{
		new StringPropertyDef("cssThemeName","default", "cssThemeName"), // this one is special because it doesn't correspond to a CSS variable. Instead, we will copy the contents of named file as rules at the end of the CSS file.
		// Todo: when we implement this setting, we want to migrate the old record of color color.
		// See code commented out in BringBookUpToDateUnprotected.
		//new CssStringVariableDef("coverColor","yellow","colors"),
		new CssDisplayVariableDef("coverShowTitleL2",true, "coverFields"),
		new CssDisplayVariableDef("coverShowTitleL3",false,"coverFields"),
		new CssDisplayVariableDef("coverShowTopic",true, "coverFields"),
		new CssDisplayVariableDef("coverShowLanguageName",false, "coverFields"),
	};

	/// <summary>
	/// Note, we might not actually use this theme at runtime. If the book has css that is incompatible with the new system, we will use legacy-5-6 instead.
	/// </summary>
	private string CssThemeNameSelectedByUser { get { return _properties.cssThemeName; } set { _properties.cssThemeName = value; } }

	public string BasePageCssName => CssThemeWeWillActuallyUse == "legacy-5-6" ? "basePage-legacy-5-6.css" : "basePage.css";

	public string CssThemeWeWillActuallyUse;

	/// <summary>
	/// In version 5.7, we greatly simplified and modernize our basePage css. However, existing books that had custom css could rely on the old approach,
	/// specifically for using margins (and possible other things like page nubmer size/location). Therefore we provide a CSS theme that
	/// effectively just gives you the basePage.css that came with 5.6, now named "basePage-legacy-5-6.css"
	/// </summary>
	///
	public void ComputeThemeAndBasePageCssVersionToUse(Tuple<string, string>[] cssFilesToCheck)
	{
		// Note that just because a book *used* to conform to an Appearance version (i.e. get "default" for cssThemeName),
		// a change in Enterprise status or Xmatter could mean that it no longer does, and may need to go back to "legacy".
		this.CssThemeWeWillActuallyUse = CssThemeNameSelectedByUser;

		// here we're kinda conflating the legacy theme name with the legacy basePage css version, because both are called "legacy-5-6"
		if (CssThemeNameSelectedByUser.StartsWith("legacy"))
		{
			Debug.WriteLine($"{CssThemeNameSelectedByUser} theme is explicitly set by user, so we'll use that for basePage");
			return;
		}
		bool foundUnsubstitutableCss = false;

		// otherwise, we have to slog through all the css files that might be incompatible with the new system.
		cssFilesToCheck.Where(css => !string.IsNullOrWhiteSpace(css.Item2)).AsParallel().FirstOrDefault(css =>
		{
			// Note: we can only cope with one subsitution, so if the customBookStyles and the customCollectionStyles both have a substitution
			// we just can't subsitute at all. So we'll let the second one we encounter, which finds that it is incompatible, cause
			// us to use the legacy theme.
			var substitutionThemeName =
				// we aren't already trying to do a substitute...
				string.IsNullOrEmpty(SubstitutedCssFile)
				// and this is a substitutable file...
				&& (css.Item1=="customBookStyles.css" || css.Item1== "customCollectionStyles.css" )
				? _substitutinator.GetThemeThatSubstitutesForCustomCSS(css.Item2)
				: null;
			if (substitutionThemeName != null)
			{
				SubstitutedCssFile = css.Item1;
				CssThemeWeWillActuallyUse = substitutionThemeName; // don't celebrate just yet... any other css file could still be incompatible
				SIL.Reporting.Logger.WriteEvent($"** Could use {CssThemeWeWillActuallyUse} BasePage and Theme");
				return false;
			}
			else if (MayBeIncompatible(css.Item1, css.Item2, out OffendingCssRule))
			{
				// ok, we can't substitute a theme for a custom css if some other css is incompatible with Appearance
				SubstitutedCssFile = null;
				foundUnsubstitutableCss = true;
				FirstPossiblyOffendingCssFile = css.Item1;
				CssThemeWeWillActuallyUse = "legacy-5-6";
				SIL.Reporting.Logger.WriteEvent($"** Will use {CssThemeWeWillActuallyUse} BasePage and Theme");
				return true;
			}
			return false;
		});

		// If we found theme that was designed to substitute for the custom file they have,
		// make it as though they had selected that theme. Note the the BookStorage is responsible for
		// not adding a link the custom css matching the name of SubstitutedCssFile.
		if (!string.IsNullOrEmpty(SubstitutedCssFile))
		{
			Properties["cssThemeName"] = CssThemeWeWillActuallyUse;
		}

		Debug.WriteLine($"** Will use {CssThemeWeWillActuallyUse}");
	}

	internal static bool MayBeIncompatible(string label, string css, out string offendingRule)
	{
		offendingRule = null;

		if(string.IsNullOrEmpty(css))
			return false;

		// note: "AppearanceVersion" uses the version number of Bloom, but isn't intended to increment with each new release of Bloom.
		// E.g., we do not expect to break CSS files very often. So initially we will have the a.v. = 5.7, and maybe the next one
		// will be 6.2.  Note that there is current some discussion of jumping from 5.6 to 6.0 to make it easier to remember which
		// Bloom version changed to the new css system.
		var v = Regex.Match(css, @"compatibleWithAppearanceVersion:\s*(\d+(\.\d+)?)")?.Groups[1]?.Value ?? "0";

		if (double.TryParse(v, out var appearanceVersion) && appearanceVersion >= 5.7)
			return false; // this is a 5.7+ theme, so it's fine

		// See if the css contains rules that nowadays should be using css variables, and would likely interfere with 5.6 and up
		const string kProbablyWillInterfere = @"\.marginBox\s*{[^}]*?"+
			"(?<![-\\w])" + // prevent matching things like --page-margin-left
			"(padding[-:]|left:|top:|right:|bottom:|margin[-:]|width:|height:)[^}]*}";
		var match = Regex.Match(css, kProbablyWillInterfere, RegexOptions.IgnoreCase);
		if (match.Success)
		{
			offendingRule = match.Value;
			var s = $"** {label} matched regex for a css rule that is potentially incompatible with this version of the default Bloom Css system: \r\n{offendingRule}";
			Debug.WriteLine(s);
			SIL.Reporting.Logger.WriteEvent(s);
			return true;
		}
		else return false;
	}

	/// <summary>
	/// Each book gets an "appearance.css" that is a combination of the appearance-theme-default.css and the css from selected appearance theme.
	/// We only write that file, we don't read it (the browser does, of course). This is the path to that file in the book folder.
	/// </summary>
	public static string AppearanceCssPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("appearance.css");
	}

	/// <summary>
	/// Each book has an "appearance.json" file in its folder that contains the settings for that book.
	/// These are not used at display time; at display time, we rely entirely on the appearance.css file.
	/// </summary>
	private static string AppearanceJsonPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("appearance.json");
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
		var jsonPath = AppearanceJsonPath(bookFolderPath);
		AppearanceSettings settings = new AppearanceSettings();

		Debug.WriteLine($"--- FromFolderOrNew({bookFolderPath})");
		if (RobustFile.Exists(jsonPath))
		{
			var json = RobustFile.ReadAllText(jsonPath);
			settings.UpdateFromJson(json);
			Debug.WriteLine($"Found existing appearance json. CssThemeName is currently {settings.CssThemeNameSelectedByUser}");
		}

		return settings;
	}
	/// <summary>
	/// Create something like this:
	/// ":root{
	///		--coverShowTitleL2: bogus-value-so-default-is-used;
	///		--coverShowTitleL3: none;
	///		--coverShowTopic: bogus-value-so-default-is-used;
	///		--coverShowLanguageName: none;
	///	}"
	/// </summary>
	public string GetCssRootDeclaration(AppearanceSettings parent = null)
	{
		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(":root{");
		var overrides = Properties.ContainsKey(kOverrideGroupsArrayKey) ? (System.Collections.Generic.List<object>)Properties[kOverrideGroupsArrayKey] : null;

		//foreach (var property in _properties.Properties())
		foreach (var property in (IDictionary<string, object>)_properties)
		{
			if (property.Key == kOverrideGroupsArrayKey)
				continue;
			var definition = propertyDefinitions.FirstOrDefault(d => d.Name == property.Key);
			if (definition == null)
			{
				// Note that we intentionally don't *remove* the property, because maybe someone set it using a later version of Bloom.
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, $"Unexpected field {property.Key}", $"appearance.json has a field that this version of Bloom does not have a definition for: {property.Key}. This is not necessarily a problem.");
			}

			var keyValuePair = property; // start by saying the value is whatever the child has
			if (parent != null)  // then if there is a parent...
			{
				// and this property is part of an override group...
				if (!string.IsNullOrEmpty(definition.OverrideGroup))
				{
					// if _properties has a value for kOverrideGroupListPropertyKey
					if (Properties.ContainsKey(kOverrideGroupsArrayKey))
					{
						// but that group is not listed as something to override...
						if (overrides == null || !overrides.Contains(definition.OverrideGroup))
						{
							if (!((IDictionary<string, object>)parent._properties).ContainsKey(property.Key))
							{
								Debug.Assert(false, $"The property '{property.Key}' is defined as part of the override group '{definition.OverrideGroup}', but the parent does not have a value for it.");
								SIL.Reporting.Logger.WriteMinorEvent($"Warning: The property '{property.Key}' is defined as part of the override group '{definition.OverrideGroup}', but the parent does not have a value for it.");
								// we'll just use the child's value
							}
							else
							{
								// OK, the property is not part of a group that is overridden & the parent has a value, so use the parent's value.
								var parentValue = ((IDictionary<string, object>)parent._properties)[property.Key];
								keyValuePair = new KeyValuePair<string, object>(property.Key, parentValue);
							}
						}
					}
				}
			}

			if (definition is CssPropertyDef)
				cssBuilder.AppendLine(((PropertyDef)definition).GetCssVariableDeclaration(keyValuePair));
		}


		cssBuilder.AppendLine("}");
		return cssBuilder.ToString();
	}

	/// <summary>
	/// Save both the css and the json version of the settings to the book folder.
	/// </summary>
	public void WriteToFolder(string folder)
	{
		var targetPath = AppearanceCssPath(folder);

		if (Program.RunningHarvesterMode && RobustFile.Exists(targetPath))
		{
			// Would overwrite, but overwrite not allowed in Harvester mode.
			// Review: (this logic just copied from CreateOrUpdateDefaultLangStyles() above, I don't know if it's still valid)
			return;
		}

		var cssBuilder = new StringBuilder();

		// Add in all the user's settings
		cssBuilder.AppendLine("/* From this book's appearance settings */");
		cssBuilder.AppendLine(GetCssRootDeclaration(null));

		if (CssThemeWeWillActuallyUse == null)
		{
			Debug.WriteLine("** TODO Appearance.WriteToFolder() called before Appearance.ComputeThemeAndBasePageCssVersionToUse()");
		}
		// TODO: ideally, this is set before we get here. This is so hard!
		var theme = CssThemeWeWillActuallyUse == null ? "default" : CssThemeWeWillActuallyUse;

		if (!theme.StartsWith("legacy"))
		{
			// Add in the var declarations of the default, so that the display doesn't collapse just because a theme is missing
			// some var that basepage.css relies on.

			var defaultThemeSourcePath = Path.Combine(BloomFileLocator.GetFolderContainingAppearanceThemeFiles(), "appearance-theme-default.css");
			cssBuilder.AppendLine("/* From appearance-theme-default.css */");
			cssBuilder.AppendLine(RobustFile.ReadAllText(defaultThemeSourcePath, Encoding.UTF8));
		}

		// Now add the user's chosen theme if it isn't the default, which we already added above.
		if (!string.IsNullOrEmpty(theme) && theme != "default")
		{
			var sourcePath = Path.Combine(BloomFileLocator.GetFolderContainingAppearanceThemeFiles(), $"appearance-theme-{theme}.css");
			if (!RobustFile.Exists(sourcePath))
			{
				// TODO: We should toast I suppose?
			}
			else
			{
				cssBuilder.AppendLine($"/* From the current appearance theme, '{theme}' */");
				cssBuilder.AppendLine(RobustFile.ReadAllText(sourcePath, Encoding.UTF8));
			}
		}

		RobustFile.WriteAllText(targetPath, cssBuilder.ToString());
		var settings = new JsonSerializerSettings
		{
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.Indented,
			/* doesn't work: CreateProperty() is never called
			  ContractResolver = new PropertiesContractResolver()
			*/
		};

		var s = JsonConvert.SerializeObject(_properties,settings);

		RobustFile.WriteAllText(AppearanceJsonPath(folder), s);
	}

	/// <summary>
	/// Read in our settings from JSON
	/// </summary>
	internal void UpdateFromJson(string json)
	{
		// parse the json into an object
		var x = JsonConvert.DeserializeObject<ExpandoObject>(json);
		//and then for each property, copy into the _properties object
		// For backwards capabilty, if the json we are reading has a null for a value,
		// do not override the default value that we already have loaded.
		foreach (var property in (IDictionary<string, object>)x)
		{
			Properties[property.Key] = property.Value;
		}
	}
	internal void UpdateFromDynamic(Newtonsoft.Json.Linq.JObject replacement)
	{
		foreach (var property in replacement)
		{
			((IDictionary<string, object>)_properties)[property.Key] = property.Value;
		}

		// If we are forced to be in legacy mode, then we can ignore any change to the
		// theme that the UI may have let through (currently it doesn't let you change it).
		// But if we are not in legacy mode, then go ahead and change the theme to match
		// whatever the UI asked for, no need to check all the css files again.
		if (string.IsNullOrEmpty(FirstPossiblyOffendingCssFile))
		{
			this.CssThemeWeWillActuallyUse = this.CssThemeNameSelectedByUser;
		}
	}

	public static IEnumerable<string> GetPathsToThemeFiles()
	{
		return Directory.EnumerateFiles(BloomFileLocator.GetFolderContainingAppearanceThemeFiles(), "*.css");
	}

	public static IEnumerable<string> GetAppearanceThemeFileNames()
	{
		string[] themes = { };
		RetryUtility.Retry(() =>
		{
			var x = from f in Directory.EnumerateFiles(BloomFileLocator.GetFolderContainingAppearanceThemeFiles(),
					"*.css")
				select Path.GetFileNameWithoutExtension(f);
			themes = x.ToArray();
		});
		return themes;
	}

	static IEnumerable<string> GetAppearanceThemeNames()
	{
		return from path in GetAppearanceThemeFileNames() select Path.GetFileName(path).Replace("appearance-theme-", "");
	}

	// things that aren't settings but are used by the BookSettings UI
	public string AppearanceUIOptions
	{
		get
		{
			var names = GetAppearanceThemeNames();
			var x = new ExpandoObject() as IDictionary<string, object>;

			x["themeNames"] = from name in names.ToArray<string>() select new { label = name, value = name };
			x["firstPossiblyLegacyCss"] = FirstPossiblyOffendingCssFile;
			return JsonConvert.SerializeObject(x); }
	}

	public object ChangeableSettingsForUI
	{
		get
		{
			return _properties;
		}
	}
}


abstract class PropertyDef
{
	public string Name;
	public dynamic DefaultValue;
	public void SetDefault(dynamic prop)
	{
		((IDictionary<string, object>)prop)[Name] = DefaultValue;
	}

	/// <summary>
	/// The name of the group of properties that can a book can override from a collection, or a page can override from a book.
	/// </summary>
	public string OverrideGroup;

	public abstract string GetCssVariableDeclaration(dynamic property);
}
abstract class CssPropertyDef : PropertyDef
{

}
class StringPropertyDef : PropertyDef
{

	public StringPropertyDef(string name, string defaultValue, string overrideGroup)
	{
		Name = name;
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		return $"--{Name}: {property.Value};";
	}
}

class CssStringVariableDef : CssPropertyDef
{

	public CssStringVariableDef(string name, string defaultValue, string overrideGroup)
	{
		Name = name; DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		return $"--{Name}: {property.Value};";
	}

}

/// <summary>
/// variables that can be used in rules like ` .something { display: var(--coverShowTopic) }`
/// </summary>
class CssDisplayVariableDef : CssPropertyDef
{
	public string TrueValue;
	public string FalseValue;
	public CssDisplayVariableDef(string name, bool defaultValue, string overrideGroup)
	{
		Name = name;
		TrueValue = AppearanceSettings.kDoShowValueForDisplay; // by using an illegal value, we just get a no-op rule, which is what we want
		FalseValue = AppearanceSettings.kHideValueForDisplay;
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		var value = (bool)property.Value ? TrueValue : FalseValue;
		return $"--{Name}: {value};";
	}
}
