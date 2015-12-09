using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Edit
{
	class DecodableReaderTool : ToolboxTool
	{
		public const string ToolId = "decodableReader"; // Avoid changing value; see ToolboxTool.JsonToolId
		public override string JsonToolId { get { return ToolId; } }

		internal static void CopyReaderToolsSettingsToWhereTheyBelong(string newlyAddedFolderOfThePack)
		{
			var destFolder = ProjectContext.GetBloomAppDataFolder();
			foreach (var readerSettingsFile in Directory.GetFiles(newlyAddedFolderOfThePack, ReaderToolsSettingsPrefix + "*.json")
				.Concat(Directory.GetFiles(newlyAddedFolderOfThePack,"ReaderToolsWords-*.json")))
			{
				try
				{
					File.Copy(readerSettingsFile, Path.Combine(destFolder, Path.GetFileName(readerSettingsFile)), true);
				}
				catch (IOException e)
				{
					// If we can't do it, we can't. Don't worry about it in production.
#if DEBUG
					Debug.Fail("Some file error copying reader settings");
#endif
				}
			}
		}

		public const string ReaderToolsSettingsPrefix = "ReaderToolsSettings-";

		/// <summary>
		/// The file (currently at a fixed location in every settings folder) where we store any settings
		/// related to Decodable and Leveled Readers.
		/// </summary>
		/// <param name="collectionSettings"></param>
		public static string GetDecodableLevelPathName(CollectionSettings collectionSettings)
		{
			return Path.Combine(Path.GetDirectoryName(collectionSettings.SettingsFilePath),
				DecodableReaderTool.ReaderToolsSettingsPrefix + collectionSettings.Language1Iso639Code + ".json");
		}

		/// <summary>
		/// If the collection has no reader tools at all, or if ones that came with the program are newer,
		/// copy the ones that came with the program.
		/// This is language-dependent, we'll typcially only overwrite settings for an English collection.
		/// </summary>
		/// <param name="settings"></param>
		public static void CopyRelevantNewReaderSettings(CollectionSettings settings)
		{
			var readerToolsPath = GetDecodableLevelPathName(settings);
			var bloomFolder = ProjectContext.GetBloomAppDataFolder();
			var newReaderTools = Path.Combine(bloomFolder, Path.GetFileName(readerToolsPath));
			if (!File.Exists(newReaderTools))
				return;
			if (File.Exists(readerToolsPath) && File.GetLastWriteTime(readerToolsPath) > File.GetLastWriteTime(newReaderTools))
				return; // don't overwrite newer existing settings?
			File.Copy(newReaderTools, readerToolsPath);
		}

		public const string kReaderToolsWordsFileNameFormat = "ReaderToolsWords-{0}.json";
	}
}
