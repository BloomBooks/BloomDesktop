using Amazon.Runtime.Internal.Util;
using Bloom;
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

public class AppearanceSettings
{
	public AppearanceSettings()
	{
		_properties = new ExpandoObject();

		// copy in the default values from each definition
		foreach (var definition in propertyDefinitions)
		{
			definition.SetDefault(_properties); // this is a workaround using ref because c# doesn't have union types, or `any`, sigh
		}
	}

	public static string kDoShowValueForDisplay = "doshow-css-will-ignore-this-and-use-default"; // by using an illegal value, we just get a no-op rule, which is what we want
	public static string kHideValueForDisplay = "none";
	public static string kOverrideGroupsArrayKey = "groupsToOverrideFromParent"; // e.g. "coverFields, xmatter"

	internal dynamic _properties;

	// it's a big hassle working directly with the ExpandoObject. By casting it this way, you can do the things you expect.
	internal IDictionary<string, object> Properties => (IDictionary<string, object>)_properties;

	public dynamic TestOnlyPropertiesAccess { get { return _properties; } }

	// create an array of properties and fill it in
	private PropertyDef[] propertyDefinitions = new PropertyDef[]
	{
		new StringPropertyDef("cssThemeName","default", "cssThemeName"), // this one is special because it doesn't correspond to a CSS variable. Instead, we will copy the contents of named file as rules at the end of the CSS file.
		//new CssStringVariableDef("coverColor","yellow","colors"),
		new CssDisplayVariableDef("coverShowTitleL2",true, "coverFields"),
		new CssDisplayVariableDef("coverShowTitleL3",false,"coverFields"),
		new CssDisplayVariableDef("coverShowTopic",true, "coverFields"),
		new CssDisplayVariableDef("coverShowLanguageName",false, "coverFields"),
	};
	private string CssThemeName { get { return _properties.cssThemeName; } }

	/// <summary>
	/// In version 5.6, we greatly simplified and modernize our basePage css. However, existing books that had custom css could rely on the old approach,
	/// specifically for using margins (and possible other things like page nubmer size/location). Therefore we provide a CSS theme that
	/// effectively just gives you the basePage.css that came with 5.5.
	/// </summary>
	internal string BasePageCssName => CssThemeName == "legacy-5-5" ? "basePage-legacy-5-5.css" : "basePage.css";


	/// <summary>
	/// Each book gets an "appearance.css" that is a combination of the appearance-page-default.css and the css from selected appearance preset.
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
		if (RobustFile.Exists(jsonPath))
		{
			var json = RobustFile.ReadAllText(jsonPath);
			settings.UpdateFromJson(json);
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
		var overrides = Properties.ContainsKey(kOverrideGroupsArrayKey) ? (System.Collections.Generic.List<object>)Properties[kOverrideGroupsArrayKey]: null;

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
				cssBuilder.AppendLine(((CssPropertyDef)definition).GetCssVariableDeclaration(keyValuePair));
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

		// Add in the var declarations of the default, so that the display doesn't collapse just because a preset is missing
		// some var that basepage.css relies on.

		var defaultPresetSourcePath = Path.Combine(ProjectContext.GetFolderContainingAppearancePresetFiles(), "appearance-page-default.css");
		cssBuilder.AppendLine("/* From appearance-page-default.css */");
		cssBuilder.AppendLine(RobustFile.ReadAllText(defaultPresetSourcePath, Encoding.UTF8));

		// Now add the user's chosen preset if it isn't the default, which we already added above.
		if (!string.IsNullOrEmpty(CssThemeName) && CssThemeName != "default")
		{
			var sourcePath = Path.Combine(ProjectContext.GetFolderContainingAppearancePresetFiles(), $"appearance-page-{CssThemeName}.css");
			if (!RobustFile.Exists(sourcePath))
			{
				// TODO: We should toast I suppose?
			}
			else
			{
				cssBuilder.AppendLine($"/* From the current appearance preset, '{CssThemeName}' */");
				cssBuilder.AppendLine(RobustFile.ReadAllText(sourcePath, Encoding.UTF8));
			}
		}
		RobustFile.WriteAllText(targetPath, cssBuilder.ToString());
		RobustFile.WriteAllText(AppearanceJsonPath(folder), JsonConvert.SerializeObject(_properties, Formatting.Indented));
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
			((IDictionary<string, object>)_properties)[property.Key] = property.Value;
		}
	}
	internal void UpdateFromDynamic(Newtonsoft.Json.Linq.JObject replacement)
	{
		foreach (var property in replacement)
		{
			((IDictionary<string, object>)_properties)[property.Key] = property.Value;
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
}
abstract class CssPropertyDef : PropertyDef
{
	public abstract string GetCssVariableDeclaration(dynamic property);
}
class StringPropertyDef : PropertyDef
{

	public StringPropertyDef(string name, string defaultValue, string overrideGroup)
	{
		Name = name;
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
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
