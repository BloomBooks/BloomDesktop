using SIL.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Bloom.Book;

namespace BloomTests.DataBuilders
{
    /// <summary>
    /// Uses a DataBuilder pattern to help write test books to disk for tests
    /// Example 1: new BookFolderBuilder(...).SetFolder(...).SetTitle("Book Title").SetHtm("<html></html>").Build();
    /// Example 2:  new BookFolderBuilder(...).SetFolder(...).WithDefaultValues().Build();
    /// </summary>
    /// <remarks>Originally intended for use in TeamCollection tests, but should be general enough to use elsewhere as well</remarks>
    class BookFolderBuilder
    {
        private string _containingFolder = null;
        private string _bookFolderName = null; // Optional. Not needed if same as _bookTitle
        private string _bookTitle;
        private string _htmContents;
        private string _metaDataJson;

        #region Post-Build Getter properties
        internal string BuiltBookFolderPath;
        internal string BuiltBookHtmPath;
        #endregion

        /// <summary>
        /// Creates a new builder object to facilitate setting up books on the filesystem
        /// </summary>
        public BookFolderBuilder() { }

        /// <summary>
        /// Causes the specified book contents to be created on disk.
        /// </summary>
        /// <returns>A string containing the path to the book folder</returns>
        /// <remarks>This is a little different than a normal DataBuilder Build(), because we're not really
        /// returning a "book" object like we normally would (we don't have a book object),
        /// but instead we're causing the book to be written to disk.
        /// </remarks>
        public BookFolderBuilder Build()
        {
            Debug.Assert(
                _containingFolder != null,
                "_containingFolder is required! Set it via InFolder(...)"
            );
            Debug.Assert(
                _bookTitle != null,
                "BookTitle is required. Set it either via WithTitle() or via WithDefaultValues()"
            );

            var bookFolderPath = Path.Combine(_containingFolder, _bookFolderName ?? _bookTitle);
            Directory.CreateDirectory(bookFolderPath);
            BuiltBookFolderPath = bookFolderPath;

            if (_htmContents != null)
            {
                var htmPath = Path.Combine(bookFolderPath, $"{_bookTitle}.htm");
                RobustFile.WriteAllText(htmPath, _htmContents);
                BuiltBookHtmPath = htmPath;
            }

            var metaDataPath = BookMetaData.MetaDataPath(bookFolderPath);
            RobustFile.WriteAllText(metaDataPath, _metaDataJson ?? new BookMetaData().Json);

            // Returns the builder object again so that callers can call post-build properties
            return this;
        }

        #region Pre-Build setters
        /// <summary>
        /// Provides reasonable default values for a book on the filesystem that would be used in unit tests
        /// </summary>
        public BookFolderBuilder WithDefaultValues()
        {
            WithTitle("Book Title");
            WithHtm("<html></html>");
            WithMetaDataJson(new BookMetaData().Json);
            return this;
        }

        /// <summary>Sets the folder which contains the book</summary>
        /// <param name="containingFolder">The folder that should contain the book. Usually, this should be the collection folder.</param>
        public BookFolderBuilder WithRootFolder(string containingFolder)
        {
            this._containingFolder = containingFolder;
            return this;
        }

        public BookFolderBuilder WithBookFolderName(string folderName)
        {
            this._bookFolderName = folderName;
            return this;
        }

        public BookFolderBuilder WithTitle(string title)
        {
            this._bookTitle = title;
            return this;
        }

        public BookFolderBuilder WithHtm(string htmContents)
        {
            this._htmContents = htmContents;
            return this;
        }

        public BookFolderBuilder WithMetaDataJson(string metaDataJson)
        {
            if (metaDataJson != null)
                _metaDataJson = metaDataJson;
            return this;
        }

        #endregion
    }
}
