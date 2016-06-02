using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Book;
using Bloom.Collection;

namespace Bloom
{
	public interface ITemplateFinder
	{
		Book.Book FindAndCreateTemplateBookByFileName(string key);
	}

	public class SourceCollectionsList : ITemplateFinder
	{
		private readonly Book.Book.Factory _bookFactory;
		private readonly BookStorage.Factory _storageFactory;
		private readonly string _editableCollectionDirectory;
		private readonly IEnumerable<string> _sourceRootFolders;

		//for moq'ing
		public SourceCollectionsList(){}

		public SourceCollectionsList(Book.Book.Factory bookFactory, BookStorage.Factory storageFactory, 
			string editableCollectionDirectory, 
			IEnumerable<string> sourceRootFolders)
		{
			_bookFactory = bookFactory;
			_storageFactory = storageFactory;
			_editableCollectionDirectory = editableCollectionDirectory;
			_sourceRootFolders = sourceRootFolders;
		}

		public Book.Book FindAndCreateTemplateBookByFileName(string fileName)
		{
			return FindAndCreateTemplateBook(templateDirectory => Path.GetFileName(templateDirectory) == fileName);
		}
		public Book.Book FindAndCreateTemplateBookByFullPath(string path)
		{
			return FindAndCreateTemplateBook(templateDirectory =>templateDirectory == Path.GetDirectoryName(path));
		}

		public Book.Book FindAndCreateTemplateBook(Func<string, bool> predicate)
		{
			return GetSourceBookFolders()
				.Where(predicate)
				.Select(dir => _bookFactory(new BookInfo(dir, false), _storageFactory(dir)))
				.FirstOrDefault();
		}

		/// <summary>
		/// Gives paths to the html files for all source books
		/// </summary>
		public IEnumerable<string> GetSourceBookPaths()
		{
			return GetCollectionFolders()
				.SelectMany(Directory.GetDirectories)
					.Select(BookStorage.FindBookHtmlInFolder);
		}

		/// <summary>
		/// Gives paths to each source book folder
		/// </summary>
		public IEnumerable<string> GetSourceBookFolders()
		{
			return GetCollectionFolders().SelectMany(Directory.GetDirectories);
		}

		public virtual IEnumerable<string> GetSourceCollectionsFolders()
		{
			return from dir in GetCollectionFolders()
				where dir != _editableCollectionDirectory
				      && !Path.GetFileName(dir).StartsWith(".")
				select dir;
		}

		/// <summary>
		/// Look in each of the roots and find the collection folders
		/// </summary>
		/// <returns></returns>
		private IEnumerable<string> GetCollectionFolders()
		{
			foreach(var root in _sourceRootFolders.Where(Directory.Exists))
			{
				foreach(var collectionDir in Directory.GetDirectories(root))
				{
					yield return collectionDir;
				}

				//dereference shortcuts to folders living elsewhere

				foreach(var collectionDir in Directory.GetFiles(root, "*.lnk", SearchOption.TopDirectoryOnly)
					.Select(ResolveShortcut.Resolve).Where(Directory.Exists))
				{
					yield return collectionDir;
				}
			}
		}

	}
}