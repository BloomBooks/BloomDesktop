using Bloom;
using SIL.Extensions;
using SIL.IO;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;

abstract class PropertyDef
{
	public string Name;
	public abstract void SetDefault(ref dynamic prop);
	public string OverrideGroup;
}
abstract class CssPropertyDef : PropertyDef
{
	public abstract string GetCssVariableDeclaration(dynamic property);
}
class StringPropertyDef : PropertyDef
{
	public string DefaultValue;
	public StringPropertyDef(string name, string defaultValue, string overrideGroup)
	{
		Name = name;
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}
}

class CssStringVariableDef : CssPropertyDef
{
	public string DefaultValue;
	public CssStringVariableDef(string name, string defaultValue, string overrideGroup)
	{
		Name = name; DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		return $"--{Name}: {property.value};";
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}
}

/// <summary>
/// variables that can be used in rules like ` .something { display: var(--coverShowTopic) }`
/// </summary>
class CssDisplayVariableDef : CssPropertyDef
{
	public string TrueValue;
	public string FalseValue;
	public bool DefaultValue;
	public CssDisplayVariableDef(string name, bool defaultValue, string overrideGroup)
	{
		Name = name;
		TrueValue = "ignore-this"; // by using an illegal value, we just get a no-op rule, which is what we want
		FalseValue = "none";
		DefaultValue = defaultValue;
		OverrideGroup = overrideGroup;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		var value = property.value ? TrueValue : FalseValue;
		return $"--{Name}: {value};";
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}
}
public class AppearanceSettings
{

	private dynamic _properties;
	public dynamic TestOnlyPropertyAccess(string name)
	{
		return _properties.Get(name);
	}

	// create an array of properties and fill it in
	private PropertyDef[] propertyDefinitions = new PropertyDef[]
	{
		new StringPropertyDef("cssThemeName","default", "cssThemeName"), // this one is special because it doesn't correspond to a CSS variable. Instead, we will copy the contents of named file as rules at the end of the CSS file.
		new CssStringVariableDef("coverColor","yellow","colors"),
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
			var prop = ((IDictionary<string, object>)_properties)[definition.Name];
			definition.SetDefault(ref prop); // this is a workaround using ref because c# doesn't have union types, or `any`, sigh
		}
	}

	public static string AppearanceCssPath(string bookFolderPath)
	{
		return bookFolderPath.CombineForPath("appearance.css");
	}
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
			settings.Update(json);
		}

		return settings;
	}
	public string GetCssRootDeclaration(AppearanceSettings parent = null)
	{
		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(":root{");
		foreach (var property in _properties)
		{
			//if (key == "overrides") continue;
			var definition = propertyDefinitions.First(d => d.Name == property.Name);
			if (definition is CssPropertyDef)
				cssBuilder.AppendLine(((CssPropertyDef)definition).GetCssVariableDeclaration(property));
		}
		cssBuilder.AppendLine("}");
		return cssBuilder.ToString();
	}
	public void WriteAppearanceCss(string folder)
	{
		var targetPath = AppearanceCssPath(folder);

		if (Program.RunningHarvesterMode && RobustFile.Exists(targetPath))
		{
			// Would overwrite, but overwrite not allowed in Harvester mode.
			// Review: (this logic just copied from CreateOrUpdateDefaultLangStyles() above, I don't know if it's still valid)
			return;
		}

		var cssBuilder = new StringBuilder();
		cssBuilder.AppendLine(GetCssRootDeclaration(null));
		if (!string.IsNullOrEmpty(CssThemeName))
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
	}

	internal void Update(dynamic replacement)
	{
		_properties = replacement;
	}
}
