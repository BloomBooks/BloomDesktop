using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Xml;

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
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit/toolbox", "toolbox.htm");
			var toolboxFolder = Path.GetDirectoryName(path);

			var domForToolbox = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));

			//enhance: this is yet another "place you have to register a new tool"
			var idsOfToolsThisVersionKnowsAbout = new[] { DecodableReaderTool.StaticToolId, LeveledReaderTool.StaticToolId, TalkingBookTool.StaticToolId, BookSettingsTool.StaticToolId };

			var toolsToDisplay = GetToolsToDisplay(book, idsOfToolsThisVersionKnowsAbout);

			EmbedSettings(book, toolsToDisplay, domForToolbox);

			// get additional tools to load
			var checkedBoxes = new List<string>();
			foreach (var tool in toolsToDisplay)
			{
				LoadPanelIntoToolbox(domForToolbox, tool, checkedBoxes, toolboxFolder);
			}

			// Load settings into the toolbox panel
			AppendToolboxPanel(domForToolbox, FileLocator.GetFileDistributedWithApplication(Path.Combine(toolboxFolder, "settings", "Settings.htm")));

			// check the appropriate boxes
			foreach (var checkBoxId in checkedBoxes)
			{
				// Review: really? we have to use a special character to make a check? can't we add "checked" attribute?
				var node = domForToolbox.Body.SelectSingleNode("//div[@id='" + checkBoxId + "']");
				if(node!=null)
					node.InnerXml = "&#10004;";
			}

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForToolbox.RawDom);
			return TempFileUtils.CreateHtml5StringFromXml(domForToolbox.RawDom);
		}

		private static List<ToolboxTool> GetToolsToDisplay(Book.Book book, string[] idsOfToolsThisVersionKnowsAbout)
		{
			var toolsThatHaveDataInBookInfo =
				book.BookInfo.Tools.Where(t => idsOfToolsThisVersionKnowsAbout.Contains(t.ToolId)).ToList();
			var toolsToDisplay = toolsThatHaveDataInBookInfo;
			toolsToDisplay.AddRange(
				idsOfToolsThisVersionKnowsAbout.Except(
					toolsThatHaveDataInBookInfo.Select(t => t.ToolId)).Select(ToolboxTool.CreateFromToolId));
			return toolsToDisplay.Where(t => t.Enabled || t.AlwaysEnabled).ToList();
		}

		private static void EmbedSettings(Book.Book book, IEnumerable<ToolboxTool> enabledTools, HtmlDom domForToolbox)
		{
			//enhance: providing settings by injecting javascript is a c# kludge. Let's move to ajax requests that get the data from the server at the point that
			//it needs it (if ever). See BookSettings.ts
			var settings = new Dictionary<string, object>
			{
				{"current", book.BookInfo.CurrentTool}
			};

			foreach (var tool in enabledTools)
			{
				RetrieveToolSettings(tool, settings);
			}

			var settingsStr = JsonConvert.SerializeObject(settings);
			settingsStr = String.Format("function GetToolboxSettings() {{ return {0};}}", settingsStr) +
				"\n$(document).ready(function() { restoreToolboxSettings(GetToolboxSettings()); });";

			var scriptElement = domForToolbox.RawDom.CreateElement("script");
			scriptElement.SetAttribute("type", "text/javascript");
			scriptElement.SetAttribute("id", "ui-accordionSettings");
			scriptElement.InnerText = settingsStr;
			domForToolbox.Head.InsertAfter(scriptElement, domForToolbox.Head.LastChild);
		}

		public static void RetrieveToolSettings(ToolboxTool tool, Dictionary<string, object> settingsObject)
		{
			if (tool != null && !String.IsNullOrEmpty(tool.State))
				settingsObject.Add(tool.StateName, tool.State);
		}

		public static void LoadPanelIntoToolbox(HtmlDom domForToolbox, ToolboxTool tool, List<string> checkedBoxes, string toolboxFolder)
		{
			// For all the toolbox tools, the tool name is used as the name of both the folder where the
			// assets for that tool are kept, and the name of the main htm file that represents the tool.
			AppendToolboxPanel(domForToolbox, FileLocator.GetFileDistributedWithApplication(Path.Combine(
				toolboxFolder,
				tool.ToolId,
				tool.ToolId + ".html")));
			checkedBoxes.Add(tool.ToolId + "Check");
		}

		/// <summary>Loads the requested panel into the toolbox</summary>
		public static void AppendToolboxPanel(HtmlDom domForToolbox, string fileName)
		{
			var toolbox = domForToolbox.Body.SelectSingleNode("//div[@id='toolbox']");
			var toolDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(fileName));
			AddToolDependencies(toolDom);
			AppendAllChildren(toolDom.Body, toolbox);
		}

		private static void AddToolDependencies(HtmlDom toolDom)
		{
			//TODO: I'm disabling this because the existence of the <script> element messes up the Accordion (it trips over it somehow).
			//We can return to this approach if/when we fix that or, more likely, abandon the accordion for a better control
			return;

			//Enhance: this can currently handle a single <script> element in the head of the component html
			//Enhance: Load in a different way so that the linked files can be debugged in a browser.
			//See http://stackoverflow.com/questions/690781/debugging-scripts-added-via-jquery-getscript-function
			var scriptInHead = toolDom.Head.SelectSingleNode("script");
			if (scriptInHead != null)
			{
				var scriptElement = toolDom.RawDom.CreateElement("script");
				var path = scriptInHead.GetStringAttribute("src");
				// use jquery getScript to dynamically load the script
				scriptElement.InnerText =
					"$.getScript('" + path + "').done(function() {}).fail(function() {alert('failed to load " + path + "')});";
				toolDom.Body.AppendChild(scriptElement);
			}
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


		/// <summary>
		/// Used to save various settings relating to the toolbox. Passed a string which is typically two or three elements
		/// divided by a tab.
		/// - may be passed 'active' followed by the ID of one of the check boxes that indicates whether the DR, LR, or TB tools are
		/// in use, followed by "1" if it is used, or "0" if not. These IDs are arranged to be the tool name followed by "Check".
		/// - may be passed 'current' followed by the name of one of the toolbox tools
		/// - may be passed 'state' followed by the name of one of the tools and its current state string.
		/// </summary>
		public static void SaveToolboxSettings(Book.Book book, string data)
		{
			var args = data.Split(new[] { '\t' });

			switch (args[0])
			{
				case "active":
					UpdateActiveToolSetting(book, args[1].Substring(0, args[1].Length - "Check".Length), args[2] == "1");
					return;

				case "current":
					book.BookInfo.CurrentTool = args[1];
					return;

				case "state":
					UpdateToolState(book, args[1], args[2]);
					return;
			}
		}

		private static void UpdateToolState(Book.Book book, string toolName, string state)
		{
			var tools = book.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.ToolId == toolName);

			if (item != null)
				item.State = state;
		}

		private static void UpdateActiveToolSetting(Book.Book book, string toolName, bool enabled)
		{
			var tools = book.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.ToolId == toolName);

			if (item == null)
			{
				item = ToolboxTool.CreateFromToolId(toolName);
				tools.Add(item);
			}
			item.Enabled = enabled;
		}
	}
}
