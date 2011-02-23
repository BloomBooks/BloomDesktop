using System;
using System.Collections.Generic;
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

		public delegate BookStarter Factory();//autofac uses this

		public BookStarter(BookStorage.Factory bookStorageFactory)
		{
			_bookStorageFactory = bookStorageFactory;
		}

		public  string CreateBookOnDiskFromTemplate(string sourceTemplateFolder, string parentCollectionPath)
		{
			string initialBookName = GetInitialName(sourceTemplateFolder, parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceTemplateFolder, newBookFolder);

			var oldNamedFile = Path.Combine(newBookFolder, Path.GetFileName(GetPathToHtmlFile(sourceTemplateFolder)));
			var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
			File.Move(oldNamedFile, newNamedFile);

			SetupDocumentContents(newBookFolder);

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

			foreach (XmlElement initialPageDiv in storage.Dom.SafeSelectNodes("/html/body/div[(contains(@class,'extraPage'))]"))
			{
				initialPageDiv.ParentNode.RemoveChild(initialPageDiv);
			}
			storage.Save();
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
				File.Copy(filePath, Path.Combine(destinationPath, Path.GetFileName(filePath)));
			}
			foreach (var dirPath in Directory.GetDirectories(sourcePath))
			{
				CopyFolder(dirPath, Path.Combine(destinationPath, Path.GetFileName(dirPath)));
			}
		}
	}
}
