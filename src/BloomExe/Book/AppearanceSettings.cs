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

}
abstract class CssPropertyDef : PropertyDef
{
	public abstract string GetCssVariableDeclaration(dynamic property);
}
class StringPropertyDef : PropertyDef
{
	public string DefaultValue;
	public StringPropertyDef(string name, string defaultValue)
	{
		Name = name; DefaultValue = defaultValue;
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}

}

class CssStringVariableDef : CssPropertyDef
{
	public string DefaultValue;
	public CssStringVariableDef(string name, string defaultValue)
	{
		Name = name; DefaultValue = defaultValue;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		return $"--{Name}:{property.value}";
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}
}

/// <summary>
/// variables that can be used in rules like `display: var(--something)`
/// </summary>
class CssDisplayVariableDef : CssPropertyDef
{
	public string TrueValue;
	public string FalseValue;
	public bool DefaultValue;
	public CssDisplayVariableDef(string name, bool defaultValue)
	{
		Name = name; TrueValue = "ignore-this"; FalseValue = "none";
		DefaultValue = defaultValue;
	}

	public override string GetCssVariableDeclaration(dynamic property)
	{
		var value = property.value ? TrueValue : FalseValue;
		return $"--{Name}:{value}";
	}
	public override void SetDefault(ref dynamic prop)
	{
		prop = DefaultValue;
	}
}
public class AppearanceSettings
{

	private dynamic _properties;

	// create an array of properties and fill it in
	private PropertyDef[] propertyDefinitions = new PropertyDef[]
	{
		new StringPropertyDef("presetName","default"),
		new CssStringVariableDef("coverColor","yellow"),
		new CssDisplayVariableDef("coverShowTitleL2",true),
		new CssDisplayVariableDef("coverShowTitleL3",false),
		new CssDisplayVariableDef("coverShowTopic",true),
		new CssDisplayVariableDef("coverShowLanguageName",false),
	};
	private string PresetName { get { return _properties.presetName; } }

	public AppearanceSettings()
	{
		_properties = new ExpandoObject();
		foreach (var definition in propertyDefinitions)
		{
			var prop = ((IDictionary<string, object>)_properties)[definition.Name];
			definition.SetDefault(ref prop);
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
		cssBuilder.AppendLine(":root{");
		foreach (var property in _properties)
		{
			var definition = propertyDefinitions.First(d => d.Name == property.Name);
			if (definition is CssPropertyDef)
				cssBuilder.AppendLine(((CssPropertyDef)definition).GetCssVariableDeclaration(property));
		}
		cssBuilder.AppendLine("}");
		if (!string.IsNullOrEmpty(PresetName))
		{
			var sourcePath = Path.Combine(ProjectContext.GetFolderContainingAppearancePresetFiles(), PresetName + ".css");
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
		_properties = appearance;
	}
}
