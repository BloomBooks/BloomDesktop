using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Collection;
using SIL.IO;

namespace Bloom.Edit
{
	class DecodableReaderTool : ToolboxTool
	{
		public const string StaticToolId = "decodableReader"; // Avoid changing value; see ToolboxTool.JsonToolId
		public override string ToolId { get { return StaticToolId; } }

		internal static void CopyReaderToolsSettingsToWhereTheyBelong(string newlyAddedFolderOfThePack)
		{
			var destFolder = ProjectContext.GetBloomAppDataFolder();
			foreach (var readerSettingsFile in Directory.GetFiles(newlyAddedFolderOfThePack, ReaderToolsSettingsPrefix + "*.json")
				.Concat(Directory.GetFiles(newlyAddedFolderOfThePack,"ReaderToolsWords-*.json")))
			{
				try
				{
					RobustFile.Copy(readerSettingsFile, Path.Combine(destFolder, Path.GetFileName(readerSettingsFile)), true);
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
		public static string GetReaderToolsSettingsFilePath(CollectionSettings collectionSettings)
		{
			return Path.Combine(Path.GetDirectoryName(collectionSettings.SettingsFilePath),
				DecodableReaderTool.ReaderToolsSettingsPrefix + collectionSettings.Language1Iso639Code + ".json");
		}

		/// <summary>
		/// If the collection has no reader tools at all, or if ones that came with the program are newer,
		/// copy the ones that came with the program.
		/// This is language-dependent, we'll typically only overwrite settings for an English collection.
		/// </summary>
		/// <param name="settings"></param>
		public static void CopyRelevantNewReaderSettings(CollectionSettings settings)
		{
			var readerToolsPath = GetReaderToolsSettingsFilePath(settings);
			var bloomFolder = ProjectContext.GetBloomAppDataFolder();
			var newReaderTools = Path.Combine(bloomFolder, Path.GetFileName(readerToolsPath));
			if (!RobustFile.Exists(newReaderTools))
				return;
			if (RobustFile.Exists(readerToolsPath) && RobustFile.GetLastWriteTime(readerToolsPath) > RobustFile.GetLastWriteTime(newReaderTools))
				return; // don't overwrite newer existing settings?
			RobustFile.Copy(newReaderTools, readerToolsPath, true);
		}

		/// <remarks>About this file (e.g. ReaderToolsWords-en.json).
		/// In one sense, it is a confusing name because if you look in it, it's much more than words (e.g. orthography).
		/// On the other hand, if I (JH) am reading things correctly, only the sample word aspect of this is used by Bloom.
		/// See the API handler for more remarks on it.
		/// </remarks>
		public const string kSynphonyLanguageDataFileNameFormat = "ReaderToolsWords-{0}.json";
	}
}
