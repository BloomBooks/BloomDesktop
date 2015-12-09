using System;
using System.Collections.Generic;
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
	/// <summary>
	/// This class supports the Edit Tab Toolbox as a whole. Eventually, all of this should be moved to javascript-land.
	/// Since the toolbox is mainly implemented in HTML/Javascript, there is no distinct .NET control for it.
	/// Thus, unlike other View classes in Bloom, ToolboxView does not inherit from a Control class,
	/// nor are there ever any instances; all methods are currently static.
	/// </summary>
	public class ToolboxView
	{
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
}
