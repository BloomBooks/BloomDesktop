using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Collection;

namespace Bloom.Edit
{
	class DecodableReaderTool : ToolboxTool
	{
		public const string ToolId = "decodableReader"; // Avoid changing value; see ToolboxTool.JsonToolId
		public override string JsonToolId { get { return ToolId; } }

		internal static void CopyReaderToolsSettingsToWhereTheyBelong(string newlyAddedFolderOfThePack)
		{
			var destFolder = ProjectContext.GetBloomAppDataFolder();
			foreach (var readerSettingsFile in Directory.GetFiles(newlyAddedFolderOfThePack, CollectionSettings.ReaderToolsSettingsPrefix + "*.json")
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
	}
}
