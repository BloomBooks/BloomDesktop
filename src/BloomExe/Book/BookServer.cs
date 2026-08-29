using System;
using System.IO;
using Bloom.TeamCollection;
using SIL.Reporting;

namespace Bloom.Book
{
    public class BookServer
    {
        private readonly Book.Factory _bookFactory;
        private readonly BookStorage.Factory _storageFactory;
        private readonly BookStarter.Factory _bookStarterFactory;
        private TeamCollectionManager _tcManager;

        public BookServer(
            Book.Factory bookFactory,
            BookStorage.Factory storageFactory,
            BookStarter.Factory bookStarterFactory,
            TeamCollectionManager tcManager = null
        )
        {
            _bookFactory = bookFactory;
            _storageFactory = storageFactory;
            _bookStarterFactory = bookStarterFactory;
            _tcManager = tcManager;
        }

        public virtual Book GetBookFromBookInfo(BookInfo bookInfo)
        {
            //Review: Note that this isn't doing any caching yet... worried that caching will just eat up memory, but if anybody is holding onto these, then the memory won't be freed anyhow
            if (bookInfo is ErrorBookInfo)
            {
                return new ErrorBook(
                    ((ErrorBookInfo)bookInfo).Exception,
                    bookInfo.FolderPath,
                    true
                );
            }

            var book = _bookFactory(bookInfo, _storageFactory(bookInfo));
            return book;
        }

        public Book CreateFromSourceBook(Book sourceBook, string containingDestinationFolder)
        {
            return CreateFromSourceBook(sourceBook.FolderPath, containingDestinationFolder);
        }

        public Book CreateFromSourceBook(
            string sourceBookFolder,
            string containingDestinationFolder
        )
        {
            string pathToFolderOfNewBook = null;

            Logger.WriteMinorEvent("Starting CreateFromSourceBook({0})", sourceBookFolder);
            try
            {
                var starter = _bookStarterFactory();
                pathToFolderOfNewBook = starter.CreateBookOnDiskFromTemplate(
                    sourceBookFolder,
                    containingDestinationFolder
                );
                // We're creating a new book, so for now, it can certainly be saved. However, this bookInfo might
                // survive past where it gets checked in, so hook up the proper SaveContext.
                var sc =
                    _tcManager?.CurrentCollectionEvenIfDisconnected
                    ?? new AlwaysEditSaveContext() as ISaveContext;
                var newBookInfo = new BookInfo(pathToFolderOfNewBook, true, sc); // _bookInfos.Find(b => b.FolderPath == pathToFolderOfNewBook);
                if (newBookInfo is ErrorBookInfo)
                {
                    throw ((ErrorBookInfo)newBookInfo).Exception;
                }

                Book newBook = GetBookFromBookInfo(newBookInfo);

                //Hack: this is a bit of a hack, to handle problems where we make the book with the suggested initial name, but the title is still something else
                var name = Path.GetFileName(newBookInfo.FolderPath); // this way, we get "my book 1", "my book 2", etc.
                newBook.SetTitle(name);

                Logger.WriteMinorEvent("Finished CreateFromnewBook({0})", newBook.FolderPath);
                Logger.WriteEvent("CreateFromSourceBook({0})", newBook.FolderPath);
                return newBook;
            }
            catch (Exception)
            {
                Logger.WriteEvent(
                    "Cleaning up after error CreateFromSourceBook({0})",
                    pathToFolderOfNewBook
                );
                //clean up this ill-fated book folder up
                if (
                    !string.IsNullOrEmpty(pathToFolderOfNewBook)
                    && Directory.Exists(pathToFolderOfNewBook)
                )
                    SIL.IO.RobustIO.DeleteDirectory(pathToFolderOfNewBook, true);
                throw;
            }
        }
    }
}
