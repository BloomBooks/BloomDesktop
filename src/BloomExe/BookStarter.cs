using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Palaso.Code;
using Palaso.IO;
using Palaso.Xml;

namespace Bloom
{
	/// <summary>
	/// Creates the files for a new blank book from a template book
	/// </summary>
	public class BookStarter
	{
		private readonly BookStorage.Factory _bookStorageFactory;
		private LanguageSettings _languageSettings;

		public delegate BookStarter Factory();//autofac uses this

		public BookStarter(BookStorage.Factory bookStorageFactory, LanguageSettings languageSettings)
		{
			_bookStorageFactory = bookStorageFactory;
			_languageSettings = languageSettings;
		}

		public  string CreateBookOnDiskFromTemplate(string sourceTemplateFolder, string parentCollectionPath)
		{
			string initialBookName = GetInitialName(sourceTemplateFolder, parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceTemplateFolder, newBookFolder);
			//if something bad happens from here on out, we need to delete that folder we just made
			try
			{
				var oldNamedFile = Path.Combine(newBookFolder, Path.GetFileName(GetPathToHtmlFile(sourceTemplateFolder)));
				var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
				File.Move(oldNamedFile, newNamedFile);

				SetupDocumentContents(newBookFolder);
			}
			catch (Exception)
			{
				Directory.Delete(newBookFolder,true);
				throw;
			}
			return newBookFolder;
		}

		private string GetPathToHtmlFile(string folder)
		{
			var candidates = Directory.GetFiles(folder, "*.htm");
			if (candidates.Length == 1)
				return candidates[0];
			else
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					"There should only be a single htm file in each folder ({0}).", folder);
				throw new ApplicationException();
			}

		}

		private void SetupDocumentContents(string destinationPath)
		{
			var storage = _bookStorageFactory(destinationPath);

			//Remove from the new book an div-pages labelled as "extraPage"
			foreach (XmlElement initialPageDiv in storage.Dom.SafeSelectNodes("/html/body/div[contains(@class,'-bloom-extraPage')]"))
			{
				initialPageDiv.ParentNode.RemoveChild(initialPageDiv);
			}
			//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'-bloom-page')]"))
			{
				SetupIdAndLineage(div, div);
				SetupPage(div, _languageSettings.VernacularIso639Code);
			}
			storage.Save();
		}

		public static void SetupPage(XmlElement pageDiv, string isoCode)
		{
			MakeVernacularElementsForPage(pageDiv, isoCode);

			// a page might be "extra" as far as the template is concerned, but
			// once a page is inserted into book (which may become a shell), it's
			// just a normal page
			pageDiv.SetAttribute("class", pageDiv.GetAttribute("class").Replace("-bloom-extraPage", "").Trim());


			// the "descendant-or-self access is broken on our SafeSelectNodes, so we have to check self independently
			//this is needed when we're actually setting up a single page that was just inserted from a template
		   //     MakeVernacularElementsForPage((XmlElement)pageDiv,isoCode);

			BookStorage.HideAllTextAreasThatShouldNotShow(pageDiv, isoCode, string.Empty);
		}

		public static void SetupIdAndLineage(XmlElement parentPageDiv, XmlElement childPageDiv)
		{
			//NB: this works even if the parent and child are the same, which is the case when making a new book
			//but not when we're adding an individual template page.

			string parentId = parentPageDiv.GetAttribute("id");
			childPageDiv.SetAttribute("id", Guid.NewGuid().ToString());

			string parentLineage = GetParentLineage(parentPageDiv);

			XmlElement childLineageElement = (XmlElement) childPageDiv.SelectSingleNodeHonoringDefaultNS("a[@class='-bloom-pageLineage']");

			if (childLineageElement == null)
			{
				//stick an <a> in there, with an empty lineage
				childLineageElement = childPageDiv.OwnerDocument.CreateElement("a");

				var doc = new XmlDocument(childPageDiv.OwnerDocument.NameTable);

				doc.LoadXml("<a href='' class='-bloom-pageLineage' style='visibility:hidden'/>");
				childLineageElement = (XmlElement)childPageDiv.OwnerDocument.ImportNode(doc.SelectSingleNodeHonoringDefaultNS("//a"), true);
				childPageDiv.AppendChild(childLineageElement);
			}

			childLineageElement.SetAttribute("href", (parentLineage + ";" + parentId).Trim(new char[] {';'}));
		}

		private static string GetParentLineage(XmlElement parentPageDiv)
		{
			string parentLineage = string.Empty;
			XmlElement parentLineageElement = (XmlElement)parentPageDiv.SelectSingleNodeHonoringDefaultNS("a[@class='-bloom-pageLineage']");
			if(parentLineageElement!=null)
				parentLineage=parentLineageElement.GetAttribute("href");
			return parentLineage;
		}

		private static bool ContainsClass(XmlNode element, string className)
		{
			return ((XmlElement) element).GetAttribute("class").Contains(className);
		}

		/// <summary>
		/// For each group of textareas in the div which have lang attributes, make a new text area
		/// with the lang code of the vernacular
		/// </summary>
		/// <param name="pageDiv"></param>
		private static void MakeVernacularElementsForPage(XmlElement pageDiv, string isoCode)
		{
			foreach (var groupId in GetParagraphIdTextAreaGroupsInSinglePageDiv(pageDiv))
			{
				MakeVernacularElementForOneGroup(pageDiv, groupId, isoCode, "textarea");
			}
			foreach (var groupId in GetIdsOfParagraphsWithVariablesInClassAndTextInSinglePageDiv(pageDiv))
			{
				MakeVernacularElementForOneGroup(pageDiv, groupId, isoCode, "p");
			}
			//any text areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages)
			XmlNodeList textareasWithoutLang =
				pageDiv.SafeSelectNodes(string.Format("//div[@id='{0}']//textarea[not(@lang)]", pageDiv.GetAttribute("id")));
			if (textareasWithoutLang.Count == 0)
			{
				//TODO: this is a repeat of the problem described above
				//hack, which should only bear fruit when we're being called with a single page during template page insertion
				textareasWithoutLang = pageDiv.SafeSelectNodes(string.Format("//textarea[not(@lang)]"));
			}
			foreach (XmlElement textarea in textareasWithoutLang)
			{
				textarea.SetAttribute("lang", isoCode);
			}
		}

		private static void MakeVernacularElementForOneGroup(XmlElement pageDiv, string groupId, string isoCode, string elementName)
		{
			//there may be several (english, Tok Pisin, etc.), but we just grab the first one and copy it
			//for the vernacular
			//could not get this to work: var textareas = SafeSelectNodes(pageDiv, string.Format("//textarea[@id='{0}']", groupId));

			string nonParagraphElementSelector = "/" + elementName;

			/* we aren't fishing for something underneath the paragraph level,
			 * we're actuallylooking for simple paragraphs that are in a
			 * language (e.g. they'd be non-editable areas where we're repeating
			 * the value of some variable you can edit elsewhere)
			 */
			if (elementName.ToLower() == "p")
				nonParagraphElementSelector = "";

			//TODO: This is Broken, so when we pass in a single page, it never finds any text areas
			XmlNodeList editableElementsWithinTheIndicatedParagraph =
				pageDiv.SafeSelectNodes(string.Format("//div[@id='{0}']//p[@id='{1}']" + nonParagraphElementSelector,
													  pageDiv.GetAttribute("id"), groupId));
			if (editableElementsWithinTheIndicatedParagraph.Count == 0)
			{
				//hack, which should only bear fruit when we're being called with a single page during template page insertion

				editableElementsWithinTheIndicatedParagraph = pageDiv.SafeSelectNodes(string.Format("//p[@id='{0}']"+nonParagraphElementSelector, groupId));

				if (editableElementsWithinTheIndicatedParagraph.Count == 0)
					return;
			}
			var alreadyInVernacular = from XmlElement x in editableElementsWithinTheIndicatedParagraph
									  where x.GetAttribute("lang") == isoCode
									  select x;
			if (alreadyInVernacular.Count() > 0)
				return;
			//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)

			if (ContainsClass(editableElementsWithinTheIndicatedParagraph[0], "-bloom-showNational"))
				return;

			XmlElement prototype = editableElementsWithinTheIndicatedParagraph[0] as XmlElement;
			//no... shellbooks should have lang on all, but what would we do for simple templates? //Debug.Assert(prototype.HasAttribute("lang"));
			if (prototype.HasAttribute("lang"))
			{
				if (elementName == "p") // don't leave copies around from the template language
				{
					prototype.SetAttribute("lang", isoCode);
				}
				else // for textareas, we *do* want copies around, because they are used for prompting in shellbooks
				{
					XmlElement vernacularCopy = (XmlElement) prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
					vernacularCopy.SetAttribute("lang",isoCode);
					vernacularCopy.SetAttribute("id", Guid.NewGuid().ToString());
					vernacularCopy.InnerText = string.Empty;
				}
			}
		}

		/// <summary>
		/// All textareas which are just the same thing in different languages must share the same @id.
		/// </summary>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
		private static List<string> GetParagraphIdTextAreaGroupsInSinglePageDiv(XmlElement pageDiv)
		{
			List<string> groups = new List<string>();
			foreach (XmlElement textArea in pageDiv.SafeSelectNodes("//textarea"))
			{
				if (textArea.ParentNode.Name.ToLower() != "p")
				{
					//maybe not... if we don't want it to be editable but stay in the national language....
					//Debug.Faile("All textareas need to be wrapped in a paragaraph");
					continue;//ignore it
				}

				var groupId = ((XmlElement)textArea.ParentNode).GetAttribute("id");
				if (string.IsNullOrEmpty(groupId))
				{   //we're happy to create the guid id's for incoming documents
					groupId = Guid.NewGuid().ToString();
					((XmlElement) textArea.ParentNode).SetAttribute("id", groupId);
				}
				if (!groups.Contains(groupId))
					groups.Add(groupId);
			}
			return groups;
		}


		/// <summary>
		/// Get those paragraphs which look like we're supposed to localize them via variables (not via editing)
		/// </summary>
		/// <remarks>maybe the "AndText" part won't be desirable...</remarks>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
		private static List<string> GetIdsOfParagraphsWithVariablesInClassAndTextInSinglePageDiv(XmlElement pageDiv)
		{
			List<string> groups = new List<string>();
			foreach (XmlElement paragraph in pageDiv.SafeSelectNodes("//p[contains(@class,'_') or contains(@class, '-bloom-')]"))
			{
			   var id = paragraph.GetAttribute("id");
				//we're happy to add guids if they're missing.
				if(string.IsNullOrEmpty(id))
				{
					id = Guid.NewGuid().ToString();
					paragraph.SetAttribute("id", id);
				}
				var text = paragraph.InnerText.Trim();
			   if (!string.IsNullOrEmpty(text))
					groups.Add(id);
			}
			return groups;
		}
		private string GetInitialName(string sourcePath, string parentCollectionPath)
		{

			string name = Path.GetFileName(sourcePath);

			var storage = _bookStorageFactory(sourcePath);
			var nameSuggestion = storage.Dom.SafeSelectNodes("//head/meta[@id='defaultNameForDerivedBooks']");
			if(nameSuggestion.Count>0)
			{
				name = ((XmlElement) nameSuggestion[0]).GetAttribute("content");
			}

			int i = 0;
			string suffix = "";

			while (Directory.Exists(Path.Combine(parentCollectionPath, name+suffix)))
			{
				++i;
				suffix = i.ToString();
			}
			return name+suffix;
		}


		private static void CopyFolder(string sourcePath, string destinationPath)
		{
			Directory.CreateDirectory(destinationPath);
			foreach (var filePath in Directory.GetFiles(sourcePath))
			{
				//better to not just copy the old thumbnail, as the on in the library may well need to look different
				if (Path.GetFileNameWithoutExtension(filePath).ToLower() == "thumbnail")
					continue;
				File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}
	}
}
