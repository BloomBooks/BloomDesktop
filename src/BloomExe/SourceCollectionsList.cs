using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.Book;

namespace Bloom
{
    public interface ITemplateFinder
    {
        Book.Book FindAndCreateTemplateBookFromDerivative(Book.Book derivativeBook);
        Book.Book FindAndCreateTemplateBookByFullPath(string path);
    }

    public class SourceCollectionsList : ITemplateFinder
    {
        private readonly Book.Book.Factory _bookFactory;
        private readonly BookStorage.Factory _storageFactory;
        private readonly string _editableCollectionDirectory;
        private readonly IEnumerable<string> _sourceRootFolders;

        //for moq'ing
        public SourceCollectionsList() { }

        public SourceCollectionsList(
            Book.Book.Factory bookFactory,
            BookStorage.Factory storageFactory,
            string editableCollectionDirectory,
            IEnumerable<string> sourceRootFolders
        )
        {
            _bookFactory = bookFactory;
            _storageFactory = storageFactory;
            _editableCollectionDirectory = editableCollectionDirectory;
            _sourceRootFolders = sourceRootFolders;
        }

        /// <summary>
        /// The purpose of this method is to create a template book (for AddPage/ChangeLayout dialogs) from a book
        /// that has been derived from that template.
        /// </summary>
        public Book.Book FindAndCreateTemplateBookFromDerivative(Book.Book derivativeBook)
        {
            // Finding a template book:
            // - First of all, a template book is its own primary template
            // - Next, we look for the template in all the usual 'source' places
            // - as of 5.5 it is possible to create a template and then create a book from it in the same collection,
            //   so we need to look elsewhere in our own editable collection, if 'Find by FileName' didn't find it.
            if (derivativeBook.IsSuitableForMakingShells)
                return derivativeBook;
            var templateKey = derivativeBook.PageTemplateSource;
            var collectionFolder = Path.GetDirectoryName(derivativeBook.FolderPath);
            return FindAndCreateTemplateBookByFileName(templateKey)
                ?? (
                    collectionFolder == null
                        ? null
                        : FindAndCreateTemplateBookByFullPath(
                            Path.Combine(collectionFolder, templateKey, templateKey + ".htm")
                        )
                );
        }

        private Book.Book FindAndCreateTemplateBookByFileName(string fileName)
        {
            return FindAndCreateTemplateBook(
                templateDirectory => Path.GetFileName(templateDirectory) == fileName
            );
        }

        public Book.Book FindAndCreateTemplateBookByFullPath(string path)
        {
            // Given a full path to the HTML file, there's no reason so search.
            // And sometimes now the template path is to the book itself (when creating a template),
            // which might be in a vernacular collection, and so not in ANY of the
            // source collections we search. So just create it directly.
            // This makes the method name somewhat deceptive. But I was trying not to
            // disrupt things too badly. And there's still a consistency with the fileName version.
            var folderPath = Path.GetDirectoryName(path);
            if (!Directory.Exists(folderPath))
                // This can happen if we're opening the AddPage dialog and the template that this book is
                // derived from doesn't exist on this machine.
                return null;
            return CreateTemplateBookByFolderPath(folderPath);
        }

        private Book.Book CreateTemplateBookByFolderPath(string folderPath)
        {
            var bookInfo = new BookInfo(folderPath, false);
            return _bookFactory(bookInfo, _storageFactory(bookInfo));
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
                where dir != _editableCollectionDirectory && !Path.GetFileName(dir).StartsWith(".")
                select dir;
        }

        /// <summary>
        /// Look in each of the roots and find the collection folders
        /// </summary>
        /// <returns></returns>
        public IEnumerable<string> GetCollectionFolders()
        {
            return GetCollectionFolders(_sourceRootFolders);
        }

        public static IEnumerable<string> GetCollectionFolders(
            IEnumerable<string> sourceRootFolders
        )
        {
            foreach (var root in sourceRootFolders.Where(Directory.Exists))
            {
                foreach (var collectionDir in Directory.GetDirectories(root))
                {
                    yield return collectionDir;
                }

                //dereference shortcuts to folders living elsewhere

                foreach (
                    var collectionDir in Directory
                        .GetFiles(root, "*.lnk", SearchOption.TopDirectoryOnly)
                        .Select(ResolveShortcut.Resolve)
                        .Where(Directory.Exists)
                )
                {
                    yield return collectionDir;
                }
            }
        }
    }
}
