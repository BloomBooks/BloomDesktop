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
			string initialBookName = GetInitialName(parentCollectionPath);
			var newBookFolder = Path.Combine(parentCollectionPath, initialBookName);
			CopyFolder(sourceTemplateFolder, newBookFolder);

			var oldNamedFile = Path.Combine(newBookFolder,"templatePages.htm");
			RequireThat.File(oldNamedFile).Exists();
			var newNamedFile = Path.Combine(newBookFolder, initialBookName + ".htm");
			File.Move(oldNamedFile, newNamedFile);

			SetupDocumentContents(newBookFolder);

			return newBookFolder;
		}

		private void SetupDocumentContents(string destinationPath)
		{
			var storage = _bookStorageFactory(destinationPath);

			foreach (XmlElement optionalNode in storage.Dom.SafeSelectNodes("/html/body/div[not(contains(@class,'required'))]"))
			{
				optionalNode.ParentNode.RemoveChild(optionalNode);
			}
			storage.Save();
		}

		private static string GetInitialName(string parentCollectionPath)
		{
			//todo: get a name from the template
			string name = "new";

			while (Directory.Exists(Path.Combine(parentCollectionPath, name)))
			{
				name += "_"; //todo: use number suffix
			}
			return name;
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
