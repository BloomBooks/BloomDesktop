using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Bloom
{
	/// <summary>
	/// Creates the files for a new blank book from a template book
	/// </summary>
	public class BookFactory
	{
		public static string CreateBookOnDiskFromTemplate(string parentCollectionPath, Book templateBook)
		{

			//todo: get a good name
			string newBookFolder = Path.Combine(parentCollectionPath, "new");

			//todo: something we actually want

			while(Directory.Exists(newBookFolder))
			{
				newBookFolder += "_";
			}
			templateBook.CopyToFolder(newBookFolder);
			return newBookFolder;
		}
	}
}
