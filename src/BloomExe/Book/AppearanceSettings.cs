using Bloom;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;

public class AppearanceSettings
{

	internal dynamic _properties;
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

	public AppearanceSettings()
	{
		_properties = new ExpandoObject();

		// copy in the default values from each definition
		foreach (var definition in propertyDefinitions)
		{
			definition.SetDefault(_properties); // this is a workaround using ref because c# doesn't have union types, or `any`, sigh
		}
	}

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
	public string GetCssRootDeclaration(AppearanceSettings parent = null)
	{
		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(":root{");

		//foreach (var property in _properties.Properties())
		foreach (var property in (IDictionary<string, object>)_properties)
		{
			if (property.Key == "overrides")
				continue;
			var definition = propertyDefinitions.FirstOrDefault(d => d.Name == property.Key);
			if (definition == null)
			{
				// Note that we intentionally don't *remove* the property, because maybe someone set it using a later version of Bloom.
				NonFatalProblem.Report(ModalIf.None, PassiveIf.Alpha, $"Unexpected field {property.Key}", $"appearance.json has a field that this version of Bloom does not have a definition for: {property.Key}. This is not necessarily a problem.");
			}

			var keyValuePair = property;
			if (parent != null)
			{
				var overrides = this._properties.overrides;

				// unless this group is listed as something to override, use the parent's value
				if (overrides == null || !((string[])overrides.ToObject<string[]>()).Contains(definition.OverrideGroup))
				{
					var v = ((IDictionary<string, object>)parent._properties)[property.Key];
					keyValuePair = new KeyValuePair<string, object>(property.Key, v);
				}
			}

			if (definition is CssPropertyDef)
				cssBuilder.AppendLine(((CssPropertyDef)definition).GetCssVariableDeclaration(keyValuePair));
		}
		cssBuilder.AppendLine("}");
		return cssBuilder.ToString();
	}
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
			var sourcePath = Path.Combine(ProjectContext.GetFolderContainingAppearancePresetFiles(), CssThemeName + ".css");
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
		TrueValue = "bogus-value-so-default-is-used"; // by using an illegal value, we just get a no-op rule, which is what we want
		FalseValue = "none";
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		var value = (bool)property.Value ? TrueValue : FalseValue;
		return $"--{Name}: {value};";
	}
}
