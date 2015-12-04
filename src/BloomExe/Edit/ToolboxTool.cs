using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Extensions;
using SIL.IO;

namespace Bloom.Edit
{
	/// <summary>
	/// This class represents one tool in the Toolbox accordion which can show to the right of the
	/// page when the user expands it. There is a subclass for each tool.
	/// These objects are serialized as part of the meta.json file representing the state of a book.
	/// The State field is persisted in this way; it is also passed in to the JavaScript that manages
	/// the toolbox. New fields and properties should be kept non-public or marked with an
	/// appropriate attribute if they should NOT be persisted in JSON.
	/// New subclasses will typically require a new case in WithName and also in ToolboxToolConverter.ReadJson.
	/// Note that the values of the Name field are used in the json and therefore cannot readily be changed.
	/// (Migration would handle a change going forward, but older Blooms would lose the data at best.)
	/// </summary>
	public abstract class ToolboxTool
	{
		/// <summary>
		/// This is the id used to identify the tool in the meta.json file that accompanies the book.
		/// These files are included in the books in BloomLibrary, which means that it is likely both
		/// that current Bloom versions will see old meta.json, and more dangerously, older Bloom
		/// versions will see new meta.json as people publish books using a newer version of the tool.
		/// Older versions cope with unknown names, but they will not display the correct tool if they
		/// see an unrecognized name for a tool they know about; therefore, the actual names used for
		/// existing tools should be changed only with care and for very good reason.
		/// </summary>
		[JsonProperty("name")]
		public abstract string JsonToolId { get; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		/// <summary>
		/// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
		/// the stage or level a book belongs to (at least the one last active when editing it).
		/// </summary>
		[JsonProperty("state")]
		public string State { get; set; }

		public static ToolboxTool CreateFromJsonToolId(string jsonToolId)
		{
			switch (jsonToolId)
			{
				case DecodableReaderTool.ToolId: return new DecodableReaderTool();
				case LeveledReaderTool.ToolId: return new LeveledReaderTool();
				case TalkingBookTool.ToolId: return new TalkingBookTool();
			}
			throw new ArgumentException("Unexpected tool name");
		}

		/// <summary>
		/// The name used to identify this tool's state in RetrieveToolSettings (the state passed to the
		/// tool's initializer).
		/// </summary>
		internal string StateName
		{
			get { return JsonToolId + "State"; }
		}

		// May be overridden to save some information about the tool state during page save.
		// Default does nothing.
		internal virtual void SaveSettings(ElementProxy toolbox)
		{ }

		// May be overridden to restore the tool state during page initialization.
		// This is run after the page is otherwise idle.
		internal virtual void RestoreSettings(EditingView _view)
		{ }

		/// <summary>
		/// Some tool settings files may need moving to the correct locations when installing a bloompack.
		/// Currently only the reader tools needs to do this. We could try to do some trick where
		/// we call a method on all subclasses by reflection, but I think this is sufficient
		/// encapsulation.
		/// </summary>
		/// <param name="newlyAddedFolderOfThePack"></param>
		internal static void CopyToolSettingsForBloomPack(string newlyAddedFolderOfThePack)
		{
			DecodableReaderTool.CopyReaderToolsSettingsToWhereTheyBelong(newlyAddedFolderOfThePack);
		}

		/// <summary>
		/// Provides a hook for anything the toolbox wants to do when a project is opened.
		/// </summary>
		/// <param name="settings"></param>
		public static void SetupToolboxForCollection(CollectionSettings settings)
		{
			DecodableReaderTool.CopyRelevantNewReaderSettings(settings);
		}

		public static IEnumerable<string> GetToolboxServerDirectories()
		{
			yield return FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI/bookEdit/toolbox");
			yield return FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI/bookEdit/toolbox/decodableReader/readerSetup");
		}

		public static string MakeToolboxContent(Book.Book book)
		{
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit/toolbox", "Toolbox.htm");
			var toolboxFolder = Path.GetDirectoryName(path);

			var domForToolbox = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));

			// embed settings on the page
			var tools = book.BookInfo.Tools.Where(t => t.Enabled == true).ToList();

			var settings = new Dictionary<string, object>
			{
				{"current", book.BookInfo.CurrentTool}
			};

			RetrieveToolSettings(tools, "talkingBook", settings);
			RetrieveToolSettings(tools, "decodableReader", settings);
			RetrieveToolSettings(tools, "leveledReader", settings);

			var settingsStr = JsonConvert.SerializeObject(settings);
			settingsStr = String.Format("function GetToolboxSettings() {{ return {0};}}", settingsStr) +
				"\n$(document).ready(function() { restoreToolboxSettings(GetToolboxSettings()); });";

			var scriptElement = domForToolbox.RawDom.CreateElement("script");
			scriptElement.SetAttribute("type", "text/javascript");
			scriptElement.SetAttribute("id", "ui-accordionSettings");
			scriptElement.InnerText = settingsStr;

			domForToolbox.Head.InsertAfter(scriptElement, domForToolbox.Head.LastChild);

			// get additional tabs to load
			var checkedBoxes = new List<string>();

			LoadPanelIntoToolboxIfAvailable(domForToolbox, tools, checkedBoxes, "decodableReader", toolboxFolder);
			LoadPanelIntoToolboxIfAvailable(domForToolbox, tools, checkedBoxes, "leveledReader", toolboxFolder);
			LoadPanelIntoToolboxIfAvailable(domForToolbox, tools, checkedBoxes, "talkingBook", toolboxFolder);

			// Load settings into the toolbox panel
			AppendToolboxPanel(domForToolbox, FileLocator.GetFileDistributedWithApplication(Path.Combine(toolboxFolder, "settings", "Settings.htm")));

			// check the appropriate boxes
			foreach (var checkBoxId in checkedBoxes)
			{
				domForToolbox.Body.SelectSingleNode("//div[@id='" + checkBoxId + "']").InnerXml = "&#10004;";
			}

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForToolbox.RawDom);
			return TempFileUtils.CreateHtml5StringFromXml(domForToolbox.RawDom);
		}

		public static void RetrieveToolSettings(List<ToolboxTool> toolList, string toolName, Dictionary<string, object> settingsObject)
		{
			var toolObject = toolList.FirstOrDefault(t => t.JsonToolId == toolName);
			if (toolObject != null && !String.IsNullOrEmpty(toolObject.State))
				settingsObject.Add(toolObject.StateName, toolObject.State);
		}

		public static void LoadPanelIntoToolboxIfAvailable(HtmlDom domForToolbox, List<ToolboxTool> toolList, List<string> checkedBoxes, string toolName, string toolboxFolder)
		{
			if (toolList.Any(t => t.JsonToolId == toolName))
			{
				// For all the toolbox tools, the tool name is used as the name of both the folder where the
				// assets for that tool are kept, and the name of the main htm file that represents the tool.
				AppendToolboxPanel(domForToolbox, FileLocator.GetFileDistributedWithApplication(Path.Combine(
					toolboxFolder,
					toolName,
					toolName + ".htm")));
				checkedBoxes.Add(toolName + "Check");
			}
		}

		/// <summary>Loads the requested panel into the toolbox</summary>
		public static void AppendToolboxPanel(HtmlDom domForToolbox, string fileName)
		{
			var toolbox = domForToolbox.Body.SelectSingleNode("//div[@id='toolbox']");
			var subPanelDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(fileName));
			AppendAllChildren(subPanelDom.Body, toolbox);
		}

		public static void AppendAllChildren(XmlNode source, XmlNode dest)
		{
			// Not sure, but the ToArray MIGHT be needed because AppendChild MIGHT remove the node from the source
			// which MIGHT interfere with iterating over them.
			foreach (var node in source.ChildNodes.Cast<XmlNode>().ToArray())
			{
				// It's nice if the independent HMTL file we are copying can have its own title, but we don't want to duplicate that into
				// our page document, which already has its own.
				if (node.Name == "title")
					continue;
				// It's no good copying file references; they may be useful for independent testing of the control source,
				// but the relative paths won't work. Any needed scripts must be re-included.
				if (node.Name == "script" && node.Attributes != null && node.Attributes["src"] != null)
					continue;
				if (node.Name == "link" && node.Attributes != null && node.Attributes["rel"] != null)
					continue; // likewise stylesheets must be inserted
				dest.AppendChild(dest.OwnerDocument.ImportNode(node,true));
			}
		}
	}

	/// <summary>
	/// This gives us something to return if we encounter an unknown tool name when deserializing.
	/// </summary>
	public class UnknownTool : ToolboxTool
	{
		public override string JsonToolId { get { return "unknownTool"; } }
	}

	/// <summary>
	/// This class is used as the ItemConverterType for the Tools property of BookMetaData.
	/// It allows us to deserialize a sequence of polymorphic tools, creating the
	/// right subclass for each based on the name.
	/// </summary>
	public class ToolboxToolConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(ToolboxTool).IsAssignableFrom(objectType);
		}

		// Default writing is fine.
		public override bool CanWrite
		{ get { return false; } }

		public override object ReadJson(JsonReader reader,
			Type objectType, object existingValue, JsonSerializer serializer)
		{
			JObject item = JObject.Load(reader);
			switch ((string)item["name"])
			{
				case DecodableReaderTool.ToolId:
					return item.ToObject<DecodableReaderTool>();
				case LeveledReaderTool.ToolId:
					return item.ToObject<LeveledReaderTool>();
				case TalkingBookTool.ToolId:
					return item.ToObject<TalkingBookTool>();
			}
			// At this point we are either encountering a meta.json that has been modified by hand,
			// or more likely one from a more recent Bloom that has an additional tool.
			// We will ignore the unknown tool (see BookMetaData.FromString()). Here in the
			// deserialize process, however, we have to return something.
			// Enhance: in theory, we could keep at least the tool's state and enabled status
			// in case this book moves back to a version of Bloom that has the tool. But we'd
			// have to carefully ignore Unknown tools in many places. Hopefully YAGNI.
			return new UnknownTool();
		}

		// We don't need a real implementation of this because returning false from CanWrite
		// tells the converter to use the default write code.
		public override void WriteJson(JsonWriter writer,
			object value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
