using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using L10NSharp;
using Newtonsoft.Json;

namespace Bloom.TeamCollection
{
	// Implements functions used by the HTML/Typescript parts of the Team Collection code.
	// Review: should this be in web/controllers with all the other API classes, or here with all the other sharing code?
	public class TeamCollectionApi
	{
		private TeamRepo _repo; // null if we are not sharing (no TeamCollection is configured)
		private BookSelection _bookSelection; // configured by autofac, tells us what book is selected
		private string CurrentUser => _repo?.CurrentUser;
		private string _collectionFolder;

		public static TeamCollectionApi TheOneInstance { get; private set; }

		// Called by autofac, which creates the one instance and registers it with the server.
		public TeamCollectionApi(CollectionSettings settings, BookSelection bookSelection)
		{
			_collectionFolder = settings.FolderPath;
			if (settings.SharingFolder != null)
			{
				_repo = new FolderTeamRepo(_collectionFolder, settings.SharingFolder);
				_repo.SetupMonitoringBehavior();
			}
			_bookSelection = bookSelection;
			TheOneInstance = this;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("teamCollection/isTeamCollectionEnabled", HandleIsTeamCollectionEnabled, false);
			apiHandler.RegisterEndpointHandler("teamCollection/currentBookStatus", HandleCurrentBookStatus, false);
			apiHandler.RegisterEndpointHandler("teamCollection/attemptLockOfCurrentBook", HandleAttemptLockOfCurrentBook, false);
			apiHandler.RegisterEndpointHandler("teamCollection/checkInCurrentBook", HandleCheckInCurrentBook, false);
			apiHandler.RegisterEndpointHandler("teamCollection/createTeamCollection", HandleCreateTeamCollection, true);
		}

		public void HandleIsTeamCollectionEnabled(ApiRequest request)
		{
			// We don't need any of the Sharing UI if the selected book isn't in the editable
			// collection (or if the collection doesn't have a Team Collection at all).
			request.ReplyWithBoolean(_repo != null && _bookSelection.CurrentSelection.IsEditable);
		}

		public void HandleCurrentBookStatus(ApiRequest request)
		{
			var whoHasBookLocked = _repo.WhoHasBookLocked(BookName);
			var whenLocked = _repo.WhenWasBookLocked(BookName);
			// review: or better to pass on to JS? We may want to show slightly different
			// text like "This book is not yet shared. Check it in to make it part of the team collection"
			if (whoHasBookLocked == TeamRepo.FakeUserIndicatingNewBook)
				whoHasBookLocked = CurrentUser;
			request.ReplyWithJson(JsonConvert.SerializeObject(
				new
				{
					who = whoHasBookLocked,
					when=whenLocked.ToLocalTime().ToShortDateString(),
					where=_repo.WhatComputerHasBookLocked(BookName),
					currentUser=_repo.CurrentUser,
					currentMachine=Environment.MachineName
				}));
		}

		private string BookName => Path.GetFileNameWithoutExtension(_bookSelection.CurrentSelection?.FolderPath);

		public void HandleAttemptLockOfCurrentBook(ApiRequest request)
		{
			// Could be a problem if there's no current book or it's not in the collection folder.
			// But in that case, we don't show the UI that leads to this being called.
			var success = _repo.AttemptLock(BookName);
			if (success)
				UpdateUiForBook();
			request.ReplyWithBoolean(success);
		}

		public void HandleCheckInCurrentBook(ApiRequest request)
		{
			_bookSelection.CurrentSelection.Save();
			_repo.PutBook(_bookSelection.CurrentSelection.FolderPath, true);
			UpdateUiForBook();
			request.PostSucceeded();
		}

		// This supports sending a notification to the CollectionSettings dialog when the Create link is used
		// to connect to a newly created shared folder and make this collection a Team Collection.
		private Action _createCallback;

		public void SetCreateCallback(Action callback)
		{
			_createCallback = callback;
		}

		public void HandleCreateTeamCollection(ApiRequest request)
		{
			using (var dlg = new FolderBrowserDialog())
			{
				dlg.ShowNewFolderButton = true;
				dlg.Description = LocalizationManager.GetString("TeamCollection.SelectFolder","Select or create the folder where this collection will be shared");
				if (DialogResult.OK != dlg.ShowDialog())
				{
					request.Failed();
					return;
				}

				var sharingFolder = dlg.SelectedPath;
				Directory.CreateDirectory(sharingFolder); // may not be needed, harmless.
				// One of the few places that knows we're using a particular implementation
				// of TeamRepo. But we have to know that to create it. And of course the user
				// just chose a folder to get things started.
				// We'll need a different API or something similar if we ever want to create
				// some other kind of repo.
				var repo = new FolderTeamRepo(_collectionFolder, sharingFolder);
				repo.CreateJoinCollectionFile();
				repo.CreateTeamSettingsFile(_collectionFolder);
				repo.CopySharedCollectionFilesFromLocal(_collectionFolder);

				_createCallback?.Invoke();
			}
			request.PostSucceeded();
		}


		// Called when we cause the book's status to change, so things outside the HTML world, like visibility of the
		// "Edit this book" button, can change appropriately. Pretending the user chose a different book seems to
		// do all the necessary stuff for now.
		private void UpdateUiForBook()
		{
			// Todo: This is not how we want to do this. Probably the UI should listen for changes to the status of books,
			// whether selected or not, talking to the repo directly.
			Form.ActiveForm.Invoke((Action) (() => _bookSelection.InvokeSelectionChanged(false)));
		}

		// Some pre-existing logic for whether the user can edit the book, combined with checking
		// that it is checked-out to this user 
		public bool CanEditBook()
		{
			var bookName = BookName;
			if (string.IsNullOrEmpty(bookName) || !_bookSelection.CurrentSelection.IsEditable || _bookSelection.CurrentSelection.HasFatalError)
			{
				return false; // no book, no editing
			}
			if (_repo == null)
			{
				return true; // no team collection, no problem.
			}

			return _repo.IsCheckedOutHereBy(_repo.GetStatus(bookName));
		}
	}
}
