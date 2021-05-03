using SIL.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel.Channels;

namespace BloomTests.DataBuilders
{
	/// <summary>
	/// Uses a DataBuilder pattern to help write test books to disk for Team Collections tests
	/// Example 1: new TCBookBuilder(...).SetFolder(...).SetTitle("Book Title").SetHtm("<html></html>").Build();
	/// Example 2:  new TCBookBuilder(...).SetFolder(...).WithTestValues().Build();
	/// </summary>
	class TeamCollectionBookBuilder
	{
		private string _containingFolder = null;
		private string _bookFolderName = null;	// Optional. Not needed if same as _bookTitle
		private string _bookTitle;
		private string _htmContents;

		#region Post-Build Getter properties
		internal string BuiltBookFolderPath;
		internal string BuiltBookHtmPath;
		#endregion

		/// <summary>
		/// Creates a new builder object to facilitate setting up books for Team Collections
		/// </summary>
		public TeamCollectionBookBuilder()
		{
		}

		/// <summary>
		/// Causes the specified book contents to be created on disk.
		/// </summary>
		/// <returns>A string containing the path to the book folder</returns>
		/// <remarks>This is a little different than a normal DataBuilder Build(), because we're not really
		/// returning a "book" object like we normally would (we don't have a book object),
		/// but instead we're causing the book to be written to disk.
		/// </remarks>
		public TeamCollectionBookBuilder Build()
		{
			Debug.Assert(_containingFolder != null, "_containingFolder is required! Set it via InFolder(...)");
			Debug.Assert(_bookTitle != null, "BookTitle is required. Set it either via WithTitle() or via WithTestValues()");

			var bookFolderPath = Path.Combine(_containingFolder, _bookFolderName ?? _bookTitle);
			Directory.CreateDirectory(bookFolderPath);
			BuiltBookFolderPath = bookFolderPath;

			if (_htmContents != null)
			{
				var htmPath = Path.Combine(bookFolderPath, $"{_bookTitle}.htm");
				RobustFile.WriteAllText(htmPath, _htmContents);
				BuiltBookHtmPath = htmPath;
			}

			// Returns the builder object again so that callers can call post-build properties
			return this;
		}

		#region Pre-Build setters
		/// <summary>
		/// Provides reasonable default values for a TeamCollectionBook that would be used in unit tests
		/// </summary>
		public TeamCollectionBookBuilder WithDefaultValues()
		{
			WithTitle("Book Title");
			WithHtm("<html></html>");
			return this;
		}

		/// <summary>Sets the folder which contains the book</summary>
		/// <param name="containingFolder">The folder that should contain the book. Usually, this should be the collection folder.</param>
		public TeamCollectionBookBuilder WithRootFolder(string containingFolder)
		{
			this._containingFolder = containingFolder;
			return this;
		}

		public TeamCollectionBookBuilder WithBookFolderName(string folderName)
		{
			this._bookFolderName = folderName;
			return this;
		}

		public TeamCollectionBookBuilder WithTitle(string title)
		{
			this._bookTitle = title;
			return this;
		}

		public TeamCollectionBookBuilder WithHtm(string htmContents)
		{
			this._htmContents = htmContents;
			return this;
		}
		#endregion
	}
}
