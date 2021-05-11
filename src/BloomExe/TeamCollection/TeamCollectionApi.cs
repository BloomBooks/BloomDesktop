using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.MiscUI;
using Bloom.Utils;
using L10NSharp;
using Newtonsoft.Json;
using Sentry;
using SIL.Reporting;

namespace Bloom.TeamCollection
{
	// Implements functions used by the HTML/Typescript parts of the Team Collection code.
	// Review: should this be in web/controllers with all the other API classes, or here with all the other sharing code?
	public class TeamCollectionApi
	{
		private ITeamCollectionManager _tcManager;
		private BookSelection _bookSelection; // configured by autofac, tells us what book is selected
		private BookServer _bookServer;
		private string CurrentUser => TeamCollectionManager.CurrentUser;
		private string _folderForCreateTC;
		private BloomWebSocketServer _socketServer;
		private CollectionSettings _settings;

		public static TeamCollectionApi TheOneInstance { get; private set; }

		// Called by autofac, which creates the one instance and registers it with the server.
		public TeamCollectionApi(CollectionSettings settings, BookSelection bookSelection, ITeamCollectionManager tcManager, BookServer bookServer, BloomWebSocketServer socketServer)
		{
			_settings = settings;
			_tcManager = tcManager;
			_tcManager.CurrentCollection?.SetupMonitoringBehavior();
			_bookSelection = bookSelection;
			_socketServer = socketServer;
			_bookServer = bookServer;
			TheOneInstance = this;
		}

		public void RegisterWithApiHandler(BloomApiHandler apiHandler)
		{
			apiHandler.RegisterEndpointHandler("teamCollection/repoFolderPath", HandleRepoFolderPath, false);
			apiHandler.RegisterEndpointHandler("teamCollection/isTeamCollectionEnabled", HandleIsTeamCollectionEnabled, false);
			apiHandler.RegisterEndpointHandler("teamCollection/currentBookStatus", HandleCurrentBookStatus, false);
			apiHandler.RegisterEndpointHandler("teamCollection/attemptLockOfCurrentBook", HandleAttemptLockOfCurrentBook, true);
			apiHandler.RegisterEndpointHandler("teamCollection/checkInCurrentBook", HandleCheckInCurrentBook, true);
			apiHandler.RegisterEndpointHandler("teamCollection/chooseFolderLocation", HandleChooseFolderLocation, true);
			apiHandler.RegisterEndpointHandler("teamCollection/createTeamCollection", HandleCreateTeamCollection, true);
			apiHandler.RegisterEndpointHandler("teamCollection/joinTeamCollection", HandleJoinTeamCollection, true);
			apiHandler.RegisterEndpointHandler("teamCollection/getLog", HandleGetLog, false);
		}

		private void HandleGetLog(ApiRequest request)
		{
			/* keeping this around as a comment to make it easier to work on the display

			
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "blah blah blah blah");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.Error, "", "An error of some sort. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.Error, "", "An error of some sort. I just simplified this English, but the surrounding code would lead me to think. I just simplified this English, but the surrounding code would lead me to think.");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message.");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.NewStuff, "", "a new stuff message.");
			_tcManager.MessageLog.WriteMessage(MessageAndMilestoneType.History, "", "Another message.");
			*/
			try
			{
				if (_tcManager.MessageLog == null)
				{
					request.Failed();
					return;
				}

				request.ReplyWithJson(JsonConvert.SerializeObject(_tcManager.MessageLog.GetProgressMessages()));
			}
			catch (Exception e)
			{
				// Not sure what to do here: getting the log should never crash.
				Logger.WriteError("TeamCollectionApi.HandleGetLog() crashed", e);
				SentrySdk.AddBreadcrumb($"Something went wrong for {request.LocalPath()}");
				SentrySdk.CaptureException(e);
				request.Failed("get log failed");
			}
		}

		public void HandleRepoFolderPath(ApiRequest request)
		{
			try
			{
				Debug.Assert(request.HttpMethod == HttpMethods.Get, "only get is implemented for the teamCollection/repoFolderPath api endpoint");
				request.ReplyWithText(_tcManager.CurrentCollectionEvenIfDisconnected?.RepoDescription ?? "");
			}
			catch (Exception e)
			{
				// Not sure what to do here: getting the repo's folder path should never crash.
				Logger.WriteError("TeamCollectionApi.HandleRepoFolderPath() crashed", e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("get repo folder path failed");
			}
		}

		private void HandleJoinTeamCollection(ApiRequest request)
		{
			try
			{
				FolderTeamCollection.JoinCollectionTeam();
				ReactDialog.CloseCurrentModal();
				request.PostSucceeded();
			}
			catch (Exception e)
			{
				// Not sure what to do here: joining the collection crashed.
				Logger.WriteError("TeamCollectionApi.HandleJoinTeamCollection() crashed", e);
				var msg = LocalizationManager.GetString("TeamCollection.ErrorJoining", "Could not join Team Collection");
				ErrorReport.NotifyUserOfProblem(e, msg);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("join team failed");
			}
		}

		public void HandleIsTeamCollectionEnabled(ApiRequest request)
		{
			try
			{
				// We don't need any of the Sharing UI if the selected book isn't in the editable
				// collection (or if the collection doesn't have a Team Collection at all).
				request.ReplyWithBoolean(_tcManager.CurrentCollectionEvenIfDisconnected != null &&
					(_bookSelection.CurrentSelection == null || _bookSelection.CurrentSelection.IsEditable));
			}
			catch (Exception e)
			{
				// Not sure what to do here: checking whether TeamCollection is enabled should never crash.
				Logger.WriteError("TeamCollectionApi.HandleIsTeamCollectionEnabled() crashed", e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("checking if Team Collections are enabled failed");
			}
		}

		public void HandleCurrentBookStatus(ApiRequest request)
		{
			try
			{
				if (!TeamCollectionManager.IsRegistrationSufficient())
				{
					request.Failed("not registered");
					return;
				}

				var whoHasBookLocked = _tcManager.CurrentCollectionEvenIfDisconnected?.WhoHasBookLocked(BookFolderName);
				// It's debatable whether to use CurrentCollectionEvenIfDisconnected everywhere. For now, I've only changed
				// it for the two bits of information actually needed by the status panel when disconnected.
				var whenLocked = _tcManager.CurrentCollection?.WhenWasBookLocked(BookFolderName) ?? DateTime.MaxValue;
				// review: or better to pass on to JS? We may want to show slightly different
				// text like "This book is not yet shared. Check it in to make it part of the Team Collection"
				if (whoHasBookLocked == TeamCollection.FakeUserIndicatingNewBook)
					whoHasBookLocked = CurrentUser;
				var problem = _tcManager.CurrentCollection?.HasLocalChangesThatMustBeClobbered(BookFolderName);
				request.ReplyWithJson(JsonConvert.SerializeObject(
					new
					{
						who = whoHasBookLocked,
						whoFirstName = _tcManager.CurrentCollection?.WhoHasBookLockedFirstName(BookFolderName),
						whoSurname = _tcManager.CurrentCollection?.WhoHasBookLockedSurname(BookFolderName),
						when = whenLocked.ToLocalTime().ToShortDateString(),
						where = _tcManager.CurrentCollectionEvenIfDisconnected?.WhatComputerHasBookLocked(BookFolderName),
						currentUser = CurrentUser,
						currentMachine = TeamCollectionManager.CurrentMachine,
						problem,
						changedRemotely = _tcManager.CurrentCollection?.HasBeenChangedRemotely(BookFolderName),
						disconnected = _tcManager.CurrentCollectionEvenIfDisconnected?.IsDisconnected
					}));
			}
			catch (Exception e)
			{
				// Not sure what to do here: getting the current book status crashed.
				Logger.WriteError("TeamCollectionApi.HandleCurrentBookStatus() crashed", e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("getting the current book status failed");
			}
		}

		private string BookFolderName => Path.GetFileName(_bookSelection.CurrentSelection?.FolderPath);

		public void HandleAttemptLockOfCurrentBook(ApiRequest request)
		{
			if (!_tcManager.CheckConnection())
			{
				request.Failed();
				return;
			}
			try
			{
				// Could be a problem if there's no current book or it's not in the collection folder.
				// But in that case, we don't show the UI that leads to this being called.
				var success = _tcManager.CurrentCollection.AttemptLock(BookFolderName);
				if (success)
					UpdateUiForBook();
				request.ReplyWithBoolean(success);
			}
			catch (Exception e)
			{
				var msgId = "TeamCollection.ErrorLockingBook";
				var msgEnglish = "Error locking access to {0}: {1}";
				var log = _tcManager?.CurrentCollection?.MessageLog;
				// Pushing an error into the log will show the Reload Collection button. It's not obvious this
				// is useful here, since we don't know exactly what went wrong. However, it at least gives the user
				// the option to try it.
				if (log != null)
					log.WriteMessage(MessageAndMilestoneType.Error, msgId, msgEnglish, BookFolderName, e.Message);
				Logger.WriteError(String.Format(msgEnglish, BookFolderName, e.Message), e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("lock failed");
			}
		}

		public void HandleCheckInCurrentBook(ApiRequest request)
		{
			try
			{
				_bookSelection.CurrentSelection.Save();
				if (!_tcManager.CheckConnection())
				{
					request.Failed();
					return;
				}
				var bookName = Path.GetFileName(_bookSelection.CurrentSelection.FolderPath);
				if (_tcManager.CurrentCollection.OkToCheckIn(bookName))
				{
					_tcManager.CurrentCollection.PutBook(_bookSelection.CurrentSelection.FolderPath, true);
				}
				else
				{
					// We can't check in! The system has broken down...perhaps conflicting checkouts while offline.
					// Save our version in Lost-and-Found
					_tcManager.CurrentCollection.PutBook(_bookSelection.CurrentSelection.FolderPath, false, true);
					// overwrite it with the current repo version.
					_tcManager.CurrentCollection.CopyBookFromRepoToLocal(bookName);
					// Force a full reload of the book from disk and update the UI to match.
					_bookSelection.SelectBook(_bookServer.GetBookFromBookInfo(_bookSelection.CurrentSelection.BookInfo, true));
					var msg = LocalizationManager.GetString("TeamCollection.ConflictingEditOrCheckout",
						"Someone else has edited this book or checked it out even though you were editing it! Your changes have been saved to Lost and Found");
					ErrorReport.NotifyUserOfProblem(msg);
				}

				UpdateUiForBook();
				request.PostSucceeded();
			}
			catch (Exception e)
			{
				var msgId = "TeamCollection.ErrorCheckingBookIn";
				var msgEnglish = "Error checking in {0}: {1}";
				var log = _tcManager?.CurrentCollection?.MessageLog;
				// Pushing an error into the log will show the Reload Collection button. It's not obvious this
				// is useful here, since we don't know exactly what went wrong. However, it at least gives the user
				// the option to try it.
				if (log != null)
					log.WriteMessage(MessageAndMilestoneType.Error, msgId, msgEnglish, _bookSelection?.CurrentSelection?.FolderPath, e.Message);
				Logger.WriteError(String.Format(msgEnglish, _bookSelection?.CurrentSelection?.FolderPath, e.Message), e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0} ({1})",
					request.LocalPath(), _bookSelection?.CurrentSelection?.FolderPath));
				SentrySdk.CaptureException(e);
				request.Failed("checkin failed");
			}
		}

		// Tell the CollectionSettingsDialog that we should reopen the collection now
		private Action _callbackToReopenCollection;

		public void SetCallbackToReopenCollection(Action callback)
		{
			_callbackToReopenCollection = callback;
		}

		public void HandleChooseFolderLocation(ApiRequest request)
		{
			try
			{
				// One of the few places that knows we're using a particular implementation
				// of TeamRepo. But we have to know that to create it. And of course the user
				// has to chose a folder to get things started.
				// We'll need a different API or something similar if we ever want to create
				// some other kind of repo.
				using (var dlg = new FolderBrowserDialog())
				{
					// Default to the Dropbox folder if one is found.
					var dropboxFolder = DropboxUtils.GetDropboxFolderPath();
					if (!String.IsNullOrEmpty(dropboxFolder))
						dlg.SelectedPath = dropboxFolder;
					dlg.ShowNewFolderButton = true;
					dlg.Description = LocalizationManager.GetString("TeamCollection.SelectFolder",
						"Select or create the folder where this collection will be shared");
					if (DialogResult.OK != dlg.ShowDialog())
					{
						request.Failed();
						return;
					}

					_folderForCreateTC = dlg.SelectedPath;
				}
				// We send the result through a websocket rather than simply returning it because
				// if the user is very slow (one site said FF times out after 90s) the browser may
				// abandon the request before it completes. The POST result is ignored and the
				// browser simply listens to the socket.
				// We'd prefer this request to return immediately and set a callback to run
				// when the dialog closes and handle the results, but FolderBrowserDialog
				// does not offer such an API. Instead, we just ignore any timeout
				// in our Javascript code.
				dynamic messageBundle = new DynamicJson();
				messageBundle.repoFolderPath = _folderForCreateTC;
				messageBundle.problem = ProblemsWithLocation(_folderForCreateTC);
				// This clientContext must match what is being listened for in CreateTeamCollection.tsx
				_socketServer.SendBundle("teamCollectionCreate", "shared-folder-path", messageBundle);

				request.PostSucceeded();
			}
			catch (Exception e)
			{
				// Not sure what to do here: choosing the collection folder should never crash.
				Logger.WriteError("TeamCollectionApi.HandleChooseFolderLocation() crashed", e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("choose folder location failed");
			}
		}

		internal string ProblemsWithLocation(string sharedFolder)
		{
			// For now we use this generic message, because it's too hard to come up with concise
			// understandable messages explaining why these locations are a problem.
			var defaultMessage = LocalizationManager.GetString("TeamCollection.ProblemLocation",
				"There is a problem with this location");
			try
			{
				if (Directory.EnumerateFiles(sharedFolder, "*.JoinBloomTC").Any())
				{
					return defaultMessage;
					//return LocalizationManager.GetString("TeamCollection.AlreadyTC",
					//	"This folder appears to already be in use as a Team Collection");
				}

				if (Directory.EnumerateFiles(sharedFolder, "*.bloomCollection").Any())
				{
					return defaultMessage;
					//return LocalizationManager.GetString("TeamCollection.LocalCollection",
					//	"This appears to be a local Bloom collection. The Team Collection must be created in a distinct place.");
				}

				if (Directory.Exists(_tcManager.PlannedRepoFolderPath(sharedFolder)))
				{
					return defaultMessage;
					//return LocalizationManager.GetString("TeamCollection.TCExists",
					//	"There is already a Folder in that location with the same name as this collection");
				}

				// We're not in a big hurry here, and the most decisive test that we can actually put things in this
				// folder is to do it.
				var testFolder = Path.Combine(sharedFolder, "test");
				Directory.CreateDirectory(testFolder);
				File.WriteAllText(Path.Combine(testFolder, "test"), "This is a test");
				SIL.IO.RobustIO.DeleteDirectoryAndContents(testFolder);
			}
			catch (Exception ex)
			{
				// This might also catch errors such as not having permission to enumerate things
				// in the directory.
				return LocalizationManager.GetString("TeamCollection.NoWriteAccess",
					"Bloom does not have permission to write to the selected folder. The system reported " +
					ex.Message);
			}

			return "";
		}

		public void HandleCreateTeamCollection(ApiRequest request)
		{
			try
			{
				if (!TeamCollection.PromptForSufficientRegistrationIfNeeded())
				{
					request.PostSucceeded();
					return;
				}

				_tcManager.ConnectToTeamCollection(_folderForCreateTC, _settings.CollectionId);
				_callbackToReopenCollection?.Invoke();

				request.PostSucceeded();
			}
			catch (Exception e)
			{
				var msgEnglish = "Error creating Team Collection {0}: {1}";
				var msgFmt = LocalizationManager.GetString("TeamCollection.ErrorCreating", msgEnglish);
				ErrorReport.NotifyUserOfProblem(e, msgFmt, _folderForCreateTC, e.Message);
				Logger.WriteError(String.Format(msgEnglish, _folderForCreateTC, e.Message), e);
				SentrySdk.AddBreadcrumb(string.Format("Something went wrong for {0}", request.LocalPath()));
				SentrySdk.CaptureException(e);
				request.Failed("create team failed");
			}
		}


		// Called when we cause the book's status to change, so things outside the HTML world, like visibility of the
		// "Edit this book" button, can change appropriately. Pretending the user chose a different book seems to
		// do all the necessary stuff for now.
		private void UpdateUiForBook()
		{
			// Todo: This is not how we want to do this. Probably the UI should listen for changes to the status of books,
			// whether selected or not, talking to the repo directly.
			if (Form.ActiveForm == null)
			{
				// On Linux (at least for Bionic), Form.ActiveForm can sometimes be null when
				// this executes.  The following loop seems to be as simple a fix as possible.
				foreach (var form in Application.OpenForms)
				{
					if (form is Shell shell)
					{
						shell.Invoke((Action)(() => _bookSelection.InvokeSelectionChanged(false)));
						return;
					}
				}
			}
			Form.ActiveForm.Invoke((Action) (() => _bookSelection.InvokeSelectionChanged(false)));
		}

		// Some pre-existing logic for whether the user can edit the book, combined with checking
		// that it is checked-out to this user 
		public bool CanEditBook()
		{
			if (_bookSelection.CurrentSelection == null || !_bookSelection.CurrentSelection.IsEditable)
			{
				return false; // no book, or the book's own logic says it's not editable
			}

			// We can edit it unless TC says we need a checkout to do it.
			return !_tcManager.NeedCheckoutToEdit(_bookSelection.CurrentSelection.FolderPath);
		}
	}
}
