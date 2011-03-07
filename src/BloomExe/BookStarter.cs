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
			foreach (XmlElement initialPageDiv in storage.Dom.SafeSelectNodes("/html/body/div[contains(@class,'extraPage')]"))
			{
				initialPageDiv.ParentNode.RemoveChild(initialPageDiv);
			}
			//If this is a shell book, make elements to hold the vernacular
			foreach (XmlElement div in storage.Dom.SafeSelectNodes("//div[contains(@class,'page')]"))
			{
				MakeVernacularTextAreasForPage(div);
			}
			storage.Save();
		}

		/// <summary>
		/// For each group of textareas in the div which have lang attributes, make a new text area
		/// with the lang code of the vernacular
		/// </summary>
		/// <param name="pageDiv"></param>
		private void MakeVernacularTextAreasForPage(XmlElement pageDiv)
		{
			foreach (var groupId in GetIdsOfTextAreaGroupsInSinglePageDiv(pageDiv))
			{
				//there may be several (english, Tok Pisin, etc.), but we just grab the first one and copy it
				//for the vernacular
			   //could not get this to work: var textareas = SafeSelectNodes(pageDiv, string.Format("//textarea[@id='{0}']", groupId));

				var textareas = pageDiv.SafeSelectNodes(string.Format("//div[@id='{0}']//textarea[@id='{1}']", pageDiv.GetAttribute("id"), groupId));
				if(textareas.Count==0)
					continue;
				var alreadyInVernacular = from XmlElement x in textareas
							where x.GetAttribute("lang") == _languageSettings.VernacularIso639Code
							select x;
				if(alreadyInVernacular.Count()>0)
						continue;//don't mess with this set, it already has a vernacular (this will happen when we're editing a shellbook, not just using it to make a vernacular edition)

				XmlElement prototype = textareas[0] as XmlElement;
				//no... shellbooks should have lang on all, but what would we do for simple templates? //Debug.Assert(prototype.HasAttribute("lang"));
				if (prototype.HasAttribute("lang"))
				{
					var vernacularCopy = prototype.ParentNode.InsertAfter(prototype.Clone(), prototype);
					vernacularCopy.Attributes["lang"].Value = _languageSettings.VernacularIso639Code;
					vernacularCopy.InnerText = string.Empty;
				}
			}

			//any text areas which still don't have a language, set them to the vernacular (this is used for simple templates (non-shell pages)
			foreach (XmlElement textarea in pageDiv.SafeSelectNodes(string.Format("//div[@id='{0}']//textarea[not(@lang)]", pageDiv.GetAttribute("id"))))
			{
				textarea.SetAttribute("lang", _languageSettings.VernacularIso639Code);
			}
		}

	   /// <summary>
		/// All textareas which are just the same thing in different languages must share the same @id.
		/// </summary>
		/// <param name="pageDiv"></param>
		/// <returns></returns>
		private List<string> GetIdsOfTextAreaGroupsInSinglePageDiv(XmlElement pageDiv)
		{
			List<string> groups = new List<string>();
			foreach (XmlElement textArea in pageDiv.SafeSelectNodes("//textarea"))
			{
				var id = textArea.GetAttribute("id");
				if (string.IsNullOrEmpty(id))
				{
					Debug.Fail("all textareas need ids");
					continue;
				}
				if (!groups.Contains(id))
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
