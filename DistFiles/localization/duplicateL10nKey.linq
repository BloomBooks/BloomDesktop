<Query Kind="Program" />

// This is a quick and dirty Linqpad program
// It duplicates a l10nkey and gives it a new name
// It will modify the XLF files for each language in-place. That allows the translations to be propagated over sooner.
//
// This may be useful when wanting to rename a l10n key to a different name.
// The process for that is as follows:
// 1) add the new value without deleting the old value
// 2) copy every instance in every language's .xlf file properly, preserving approval flags / status.  Make sure the new copy exists in the same relative position in every file.
// 3) After the new English .xlf file has been merged, wait a bit for Crowdin to pick it up.  Then go to every language that has already translated the term and fix the new value to match the old value exactly, including translation status.  This is the hard part of the job, and why we don't do this kind of thing lightly.
// 4) Change the C# code to use the new value throughout.
// 5) Remove the old value from the English .xlf file.  You can remove it from the other .xlf files as well, but that will happen automatically the next time I (or someone) merges from Crowdin.
//
// This script will modify the XLF files (accomplishing Step 1 and 2). (You can check in these changes)

void Main()
{
	////////////////
	// PARAMETERS //
	////////////////
	// Modify me before each run
	string oldId = "BookMetadata.WhatsThis";
	string newId = "Common.WhatsThis";
	string idToInsertNewIdAfter = "Common.Warning";	
	string localizationFolderPath = @"C:\src\BloomDesktop5.1\DistFiles\localization\";
	
	
	var directories = Directory.GetDirectories(localizationFolderPath);
	var langNameDirs = directories.Where(dir => Path.GetFileName(dir).Length <= 3 || Path.GetFileName(dir).StartsWith("zh-"));
	
	foreach (var langNameDir in langNameDirs)
	{
		var langName = Path.GetFileName(langNameDir);
		var xlfFilePath = Path.Combine(langNameDir, "Bloom.xlf");
		var fileContents = File.ReadAllText(xlfFilePath);
		string oldTransUnitXml = GetTransUnitXmlWithId(fileContents, oldId);
		if (oldTransUnitXml == null)
		{
			Console.Out.WriteLine($"Failed to get <trans-unit> for ${oldId} in file ${xlfFilePath}");
			continue;
		}
		
		// Hopefully oldId is not super generic
		var newTransUnitXml = oldTransUnitXml.Replace(oldId, newId);
		
		string transUnitToInsertAfter = GetTransUnitXmlWithId(fileContents, idToInsertNewIdAfter);
		
		string padding = "      ";	// ENHANCE: Or you could determine it programatically, I guess.
		var replacementXml = transUnitToInsertAfter.Replace("</trans-unit>", $"</trans-unit>{Environment.NewLine}{padding}{newTransUnitXml}");		
		// replacementXml.Dump();
		
		var newFileContents = fileContents.Replace(transUnitToInsertAfter, replacementXml);
		File.WriteAllText(xlfFilePath, newFileContents);
	}
}

// Define other methods and classes here
string GetTransUnitXmlWithId(string fileContents, string id)
{
	if (fileContents == null)
		return null;
	
	var startIndex = fileContents.IndexOf($"<trans-unit id=\"{id}\"");
	if (startIndex < 0)
	{
		return null;
	}
	
	string endTag = "</trans-unit>";
	var endIndex = fileContents.IndexOf(endTag, startIndex + 1);
	if (endIndex < 0)
	{
		Console.Error.WriteLine("Failed to find endIndex");
		return null;
	}
	
	endIndex += endTag.Length;
	
	return fileContents.Substring(startIndex, endIndex - startIndex);
}