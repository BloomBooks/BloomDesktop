using System;
using System.Collections.Generic;
using Bloom.Properties;
using BookInstance = Bloom.Book.Book;
using Bloom.WebLibraryIntegration;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.Progress;

namespace Bloom.Publish.BloomLibrary
{
	public class BloomLibraryPublishModel
	{
		private readonly Metadata _licenseMetadata;
		private readonly LicenseInfo _license;
		private readonly BookTransfer _transferrer;
		private readonly string _storedWebPassword;

		public BloomLibraryPublishModel(BookTransfer transferer, BookInstance book)
		{
			Book = book;
			_transferrer = transferer;

			_licenseMetadata = Book.GetLicenseMetadata();
			// This is usually redundant, but might not be on old books where the license was set before the new
			// editing code was written.
			Book.SetMetadata(_licenseMetadata);
			_license = _licenseMetadata.License;

			EnsureBookAndUploaderId();
			WebUserId = Settings.Default.WebUserId;
			_storedWebPassword = Settings.Default.WebPassword;
		}

		internal BookInstance Book { get; }

		private bool LicenseIsOkay => LicenseType != LicenseState.NeverOpened;

		internal string LicenseRights => _license.RightsStatement??string.Empty;

		// ReSharper disable once InconsistentNaming
		internal string CCLicenseUrl => (_license as CreativeCommonsLicense)?.Url;

		internal string LicenseToken => _license.Token.ToUpperInvariant();

		internal string Credits => Book.BookInfo.Credits;

		internal string Title => Book.BookInfo.Title;

		internal string Copyright => Book.BookInfo.Copyright;

		internal bool IsTemplate => Book.BookInfo.IsSuitableForMakingShells;

		internal string Summary
		{
			get { return Book.BookInfo.Summary; }
			set
			{
				Book.BookInfo.Summary = value;
				Book.BookInfo.Save();
			}
		}

		/// <summary>
		/// This is a difficult concept to implement. The current usage of this is in creating metadata indicating which languages
		/// the book contains. How are we to decide whether it contains enough of a particular language to be useful?
		/// Based on BL-2017, we now return a Dictionary of booleans indicating whether a language should be uploaded by default.
		/// The dictionary contains an entry for every language where the book contains non-x-matter text.
		/// The value is true if every non-x-matter field which contains text in any language contains text in this.
		/// </summary>
		internal Dictionary<string, bool> AllLanguages => Book.AllLanguages;

		/// <summary>
		/// Gets a user-friendly language name.
		/// </summary>
		/// <param name="code"></param>
		/// <returns></returns>
		internal string PrettyLanguageName(string code)
		{
			return Book.PrettyPrintLanguage(code);
		}

		private void EnsureBookAndUploaderId()
		{
			if (string.IsNullOrEmpty(Book.BookInfo.Id))
			{
				Book.BookInfo.Id = Guid.NewGuid().ToString();
			}
			Book.BookInfo.Uploader = Uploader;
		}

		internal bool IsBookPublicDomain => _license.Url.StartsWith("http://creativecommons.org/publicdomain/zero/");

		internal bool BookIsAlreadyOnServer => LoggedIn && _transferrer.IsBookOnServer(Book.FolderPath);

		private string Uploader => _transferrer.UserId;

		/// <summary>
		/// The model alone cannot determine whether a book is OK to publish, because the language requirements
		/// are different for single book upload and bulk upload (which use this same model).
		/// For bulk upload, a book is okay to publish if this property is true AND it has ANY language
		/// with complete data (meaning all non-xmatter fields have something in the language).
		/// For single book upload, a book is okay to publish if this property is true AND it is EITHER
		/// OkToUploadWithNoLanguages OR the user has checked a language checkbox.
		/// </summary>
		internal bool OkToPublish =>
			LicenseIsOkay &&
		    // Copyright info is not required if the book has been put in the public domain
		    (IsBookPublicDomain || !string.IsNullOrWhiteSpace(Copyright)) &&
		    !string.IsNullOrWhiteSpace(Title);

		internal bool LoggedIn => _transferrer.LoggedIn;

		/// <summary>
		/// Stored Web user Id
		/// </summary>
		internal string WebUserId { get; }

		internal bool OkToUploadWithNoLanguages => Book.BookInfo.IsSuitableForMakingShells;

		/// <summary>
		/// Need a property for when we don't have a BookTransfer object available (single book upload).
		/// </summary>
		internal bool IsThisVersionAllowedToUpload => _transferrer.IsThisVersionAllowedToUpload();

		internal string UploadOneBook(BookInstance book, LogBox progressBox, PublishView publishView, string[] languages, out string parseId)
		{
			return _transferrer.FullUpload(book, progressBox, publishView, languages, out parseId);
		}

		/// <summary>
		/// Try to login using stored userid and password
		/// Test LoggedIn property to verify.
		/// </summary>
		/// <returns></returns>
		internal void LogIn()
		{
			if (string.IsNullOrEmpty(WebUserId))
				return;
			_transferrer.LogIn(WebUserId, _storedWebPassword);
		}

		internal void Logout()
		{
			_transferrer.Logout();
		}

		internal LicenseState LicenseType
		{
			get
			{
				if (_license == null || _license is NullLicense && string.IsNullOrWhiteSpace(_licenseMetadata.CopyrightNotice))
				{
					// A null license and no copyright indicates they never even opened the ClearShare dialog to choose a license.
					return LicenseState.NeverOpened;
				}
				if (_license is CreativeCommonsLicense)
				{
					return LicenseState.CreativeCommons;
				}
				if (_license is NullLicense)
				{
					return LicenseState.Null;
				}
				return LicenseState.Custom;
			}
		}
	}

	internal enum LicenseState
	{
		NeverOpened,
		Null,
		CreativeCommons,
		Custom
	}
}
