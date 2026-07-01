using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.TeamCollection;
using BloomTests.TestDoubles.Book;
using SIL.TestUtilities;

namespace BloomTests.TestDoubles.CollectionTab
{
    // A simplified implementation of CollectionModel that cuts some corners in order to ease setup.
    // Tries to provide enough functionality to allow construction of CollectionTabView without exceptions, to be able to return/load a collection, and instntiate a dummy SourceCollectionsList which has a dummy templates collection
    class FakeCollectionModel : CollectionModel
    {
        public readonly string TestFolderPath;

        public FakeCollectionModel(
            TemporaryFolder testFolder,
            CollectionSettings collectionSettings = null
        )
            : base(
                testFolder.Path,
                collectionSettings ?? new CollectionSettings(),
                new BookSelection(),
                GetDefaultSourceCollectionsList(),
                BookCollectionFactory,
                null,
                new CreateFromSourceBookCommand(),
                new FakeBookServer(),
                new CurrentEditableCollectionSelection(),
                null,
                new TeamCollectionManager(
                    testFolder.Path,
                    null,
                    new BookStatusChangeEvent(),
                    new BookSelection(),
                    null,
                    null
                ),
                // A real (unstarted) web socket server: CollectionModel's CollectionChanged handler
                // calls _webSocketServer.SendEvent when a book is added/removed (e.g. when an import
                // replaces an existing book), which would NullReferenceException if this were null.
                // With no sockets connected, SendEvent is a harmless no-op.
                new BloomWebSocketServer(),
                null,
                null
            )
        {
            TestFolderPath = testFolder.Path;
        }

        protected static SourceCollectionsList GetDefaultSourceCollectionsList()
        {
            var parentFolder = new TemporaryFolder("Parent");
            var templateCollectionFolder = new TemporaryFolder(parentFolder, "Templates");

            return new SourceCollectionsList(null, null, null, new string[] { parentFolder.Path });
        }

        public static BookCollection BookCollectionFactory(
            string path,
            BookCollection.CollectionType collectionType,
            CollectionSettings settings = null
        )
        {
            // A real BookSelection (not null) is required: BookCollection.AddBookInfo dereferences
            // it (_bookSelection.CurrentSelection) while enumerating books, so passing null makes
            // every book come back as an ErrorBookInfo (with a random Id), which in turn breaks
            // duplicate-by-bookInstanceId detection in tests that rely on GetBookInfos().
            // We deliberately leave collectionSettings null: with it set, GetBestDisplayTitle tries
            // to build a BookData from the (deliberately minimal) test book html and throws.
            return new BookCollection(path, collectionType, new BookSelection());
        }
    }
}
