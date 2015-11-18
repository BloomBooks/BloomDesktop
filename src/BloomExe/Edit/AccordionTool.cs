using System;
using Newtonsoft.Json;
using SIL.Extensions;

namespace Bloom.Edit
{
	public class AccordionTool
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		/// <summary>
		/// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
		/// the stage or level a book belongs to (at least the one last active when editing it).
		/// </summary>
		[JsonProperty("state")]
		public string State { get; set; }
	}

	internal static class AccordionCatalog
	{
		private static readonly string[] toolNames		= { "decodableReader", "leveledReader", "audioRecording", "pageElements" };

		private static readonly string[] directoryNames = { "DecodableRT", "LeveledRT", "AudioRecordingTool", "PageElements" };

		private static readonly string[] checkboxNames	= { "showDRT", "showLRT", "showART", "" };

		private static readonly string[] stateKeys		= { "decodableState", "leveledState", "audioRecordingState", "" };

		internal static string GetDirectoryFromToolName(string toolName)
		{
			var idx = toolNames.IndexOf(toolName);
			return idx < 0 ? string.Empty : directoryNames[idx];
		}

		internal static string GetToolNameFromDirectory(string dirName)
		{
			var idx = directoryNames.IndexOf(dirName);
			return idx < 0 ? string.Empty : toolNames[idx];
		}

		internal static string GetToolNameFromCheckbox(string checkbox)
		{
			var idx = checkboxNames.IndexOf(checkbox);
			return idx < 0 ? string.Empty : toolNames[idx];
		}

		internal static string GetStateKeyFromToolName(string toolName)
		{
			var idx = toolNames.IndexOf(toolName);
			return idx < 0 ? string.Empty : stateKeys[idx];
		}

		internal static string GetCheckboxFromToolName(string toolName)
		{
			var idx = toolNames.IndexOf(toolName);
			return idx < 0 ? string.Empty : checkboxNames[idx];
		}
	}
}
