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
			// Given a full path to the HTML file, there's no reason so search.
			// And sometimes now the template path is to the book itself (when creating a template),
			// which might be in a vernacular collection, and so not in ANY of the
			// source collections we search. So just create it directly.
			// This makes the method name somewhat deceptive. But I was trying not to
			// disrupt things too badly. And there's still a consistency with the fileName version.
			return CreateTemplateBookByFolderPath(Path.GetDirectoryName(path));
		}

		private Book.Book CreateTemplateBookByFolderPath(string folderPath)
		{
			return _bookFactory(new BookInfo(folderPath, false), _storageFactory(folderPath));
		}

		public Book.Book FindAndCreateTemplateBook(Func<string, bool> predicate)
		{
			return GetSourceBookFolders()
				.Where(predicate)
				.Select(CreateTemplateBookByFolderPath)
				.FirstOrDefault();
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
		public IEnumerable<string> GetCollectionFolders()
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
