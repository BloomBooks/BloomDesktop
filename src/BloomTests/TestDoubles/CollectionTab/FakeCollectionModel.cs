using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.TeamCollection;
using SIL.TestUtilities;

using BloomTests.TestDoubles.Book;

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
                null,
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
            BookCollection.CollectionType collectionType
        )
        {
            return new BookCollection(path, collectionType, null);
        }
    }
}
