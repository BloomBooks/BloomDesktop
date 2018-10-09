﻿using System;
using System.Collections.Generic;
using Bloom.Properties;
using BookInstance = Bloom.Book.Book;
using Bloom.WebLibraryIntegration;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.Progress;

namespace Bloom.Publish.BloomLibrary
{
	/// <summary>
	/// Puts all of the business logic of whether a book's metadata is complete enough to publish and handling some of the login
	/// process in one place so that the regular single-book Publish tab upload and the command line bulk upload can use the
	/// same verification logic.
	/// </summary>
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

		internal string LicenseRights => _license.RightsStatement??string.Empty;

		// ReSharper disable once InconsistentNaming
		internal string CCLicenseUrl => (_license as CreativeCommonsLicense)?.Url;

		internal string LicenseToken => _license.Token.ToUpperInvariant();

		internal string Credits => Book.BookInfo.Credits;

		internal string Title => Book.BookInfo.Title;

		internal string Copyright => Book.BookInfo.Copyright;

		internal bool HasOriginalCopyrightInfoInSourceCollection => Book.HasOriginalCopyrightInfoInSourceCollection;

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

		internal bool IsBookPublicDomain => _license?.Url != null && _license.Url.StartsWith("http://creativecommons.org/publicdomain/zero/");

		internal bool BookIsAlreadyOnServer => LoggedIn && _transferrer.IsBookOnServer(Book.FolderPath);

		private string Uploader => _transferrer.UserId;

		/// <summary>
		/// The model alone cannot determine whether a book is OK to upload, because the language requirements
		/// are different for single book upload and bulk upload (which use this same model).
		/// For bulk upload, a book is okay to upload if this property is true AND it has ANY language
		/// with complete data (meaning all non-xmatter fields have something in the language).
		/// For single book upload, a book is okay to upload if this property is true AND it is EITHER
		/// OkToUploadWithNoLanguages OR the user has checked a language checkbox.
		/// This property just determines whether the book's metadata is complete enough to publish
		/// LoggedIn is not part of this, because the two users of the model check for login status in different
		/// parts of the process.
		/// </summary>
		internal bool MetadataIsReadyToPublish =>
		    // Copyright info is not required if the book has been put in the public domain
			// Also, (BL-5563) if there is an original copyright and we're publishing from a source collection,
			// we don't need to have a copyright.
		    (IsBookPublicDomain || !string.IsNullOrWhiteSpace(Copyright) || HasOriginalCopyrightInfoInSourceCollection) &&
		    !string.IsNullOrWhiteSpace(Title);

		internal bool LoggedIn => _transferrer.LoggedIn;

		/// <summary>
		/// Stored Web user Id
		/// </summary>
		internal string WebUserId { get; }

		internal bool OkToUploadWithNoLanguages => Book.BookInfo.IsSuitableForMakingShells;

		internal bool IsThisVersionAllowedToUpload => _transferrer.IsThisVersionAllowedToUpload();

		internal string UploadOneBook(BookInstance book, LogBox progressBox, PublishView publishView, string[] languages, bool excludeNarrationAudio, bool excludeMusic, out string parseId)
		{
			return _transferrer.FullUpload(book, progressBox, publishView, languages, excludeNarrationAudio, excludeMusic, out parseId);
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

		/// <summary>
		/// Used by bulk uploader to tell the user why we aren't uploading their book.
		/// </summary>
		/// <returns></returns>
		public string GetReasonForNotUploadingBook()
		{
			const string couldNotUpload = "Could not upload book. ";
			// It might be because we're missing required metadata.
			if (!MetadataIsReadyToPublish)
			{
				if (string.IsNullOrWhiteSpace(Title))
				{
					return couldNotUpload + "Required book Title is empty.";
				}
				if (string.IsNullOrWhiteSpace(Copyright))
				{
					return couldNotUpload + "Required book Copyright is empty.";
				}
			}
			// Or it might be because a non-template book doesn't have any 'complete' languages.
			// every non-x - matter field which contains text in any language contains text in this
			return couldNotUpload + "A non-template book needs at least one language where every non-xmatter field contains text in that language.";
		}
	}

	internal enum LicenseState
	{
		Null,
		CreativeCommons,
		Custom
	}
}
