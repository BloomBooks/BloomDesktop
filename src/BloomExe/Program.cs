using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Collection.BloomPack;
using Bloom.CollectionCreating;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using PalasoUIWinforms.Registration;
using DesktopAnalytics;
using L10NSharp;
using Palaso.IO;
using Palaso.Reporting;
using Skybound.Gecko;
using System.Linq;

namespace Bloom
{
	static class Program
	{
		private const string _mutexId = "bloom";

		//static HttpListener listener = new HttpListener();

		/// <summary>
		/// We have one project open at a time, and this helps us bootstrap the project and
		/// properly dispose of various things when the project is closed.
		/// </summary>
		private static ProjectContext _projectContext;
		private static ApplicationContainer _applicationContainer;
		public static bool StartUpWithFirstOrNewVersionBehavior;

#if PerProjectMutex
		private static Mutex _oneInstancePerProjectMutex;
#else
		private static Mutex _onlyOneBloomMutex;
		private static DateTime _earliestWeShouldCloseTheSplashScreen;
		private static SplashScreen _splashForm;
		private static bool _alreadyHadSplashOnce;
		private static BookDownloadSupport _bookDownloadSupport;
#endif
		internal static string PathToBookDownloadedAtStartup { get; set; }
		[STAThread]
		[HandleProcessCorruptedStateExceptions]
		static void Main(string[] args)
		{
			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

#if !DEBUG //the exception you get when there is no other BLOOM is pain when running debugger with break-on-exceptions
				if (!GrabMutexForBloom())
					return;
#endif

				if (Palaso.PlatformUtilities.Platform.IsWindows)
				{
					OldVersionCheck();
				}
				//bring in settings from any previous version
				if (Settings.Default.NeedUpgrade)
				{
					//see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
					Settings.Default.Upgrade();
					Settings.Default.Reload();
					Settings.Default.NeedUpgrade = false;
					Settings.Default.Save();
					StartUpWithFirstOrNewVersionBehavior = true;
				}
#if DEBUG
				if (args.Length > 0)
				{
					// This allows us to debug things like  interpreting a URL.
					MessageBox.Show("Attach debugger now");
				}
#endif

#if DEBUG
				using (new DesktopAnalytics.Analytics("sje2fq26wnnk8c2kzflf", RegistrationDialog.GetAnalyticsUserInfo(), true))

#else
				string feedbackSetting = System.Environment.GetEnvironmentVariable("FEEDBACK");

				//default is to allow tracking
				var allowTracking = string.IsNullOrEmpty(feedbackSetting) || feedbackSetting.ToLower() == "yes" || feedbackSetting.ToLower() == "true";

				using (new Analytics("c8ndqrrl7f0twbf2s6cv", RegistrationDialog.GetAnalyticsUserInfo(), allowTracking))

#endif

				{
					if (args.Length == 1 && args[0].ToLower().EndsWith(".bloompack"))
					{
						using (var dlg = new BloomPackInstallDialog(args[0]))
						{
							dlg.ShowDialog();
						}
						return;
					}
					if (IsBloomBookOrder(args))
					{
						// We will start up just enough to download the book. This avoids the code that normally tries to keep only a single instance running.
						// There is probably a pathological case here where we are overwriting an existing template just as the main instance is trying to
						// do something with it. The time interval would be very short, because download uses a temp folder until it has the whole thing
						// and then copies (or more commonly moves) it over in one step, and making a book from a template involves a similarly short
						// step of copying the template to the new book. Hopefully users have (or will soon learn) enough sense not to
						// try to use a template while in the middle of downloading a new version.
						SetUpErrorHandling();
						_applicationContainer = new ApplicationContainer();
						SetUpLocalization();
						Logger.Init();
						new BookDownloadSupport();
						Browser.SetUpXulRunner();
						L10NSharp.LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
						var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
							_applicationContainer.HtmlThumbnailer, new BookDownloadStartingEvent())/*not hooked to anything*/;
						transfer.HandleBloomBookOrder(args[0]);
						PathToBookDownloadedAtStartup = transfer.LastBookDownloadedPath;
						// If another instance is running, this one has served its purpose and can exit right away.
						// Otherwise, carry on with starting up normally.
						if (TryToGrabMutexForBloom())
							FinishStartup();
						else
						{
							_onlyOneBloomMutex = null; // we don't own it, so ReleaseMutexForBloom must not try to release it.
							string caption = LocalizationManager.GetString("Download.CompletedCaption", "Download complete");
							string message = LocalizationManager.GetString("Download.Completed",
								@"Your download ({0}) is complete. You can see it in the 'Books from the Bloom Library' section of your Collections. "
								+ "If you don't seem to be in the middle of doing something, Bloom will select it for you.");
							message = string.Format(message, Path.GetFileName(PathToBookDownloadedAtStartup));
							MessageBox.Show(message, caption);
						}
						return;
					}

					if (!GrabMutexForBloom())
						return;

					OldVersionCheck();

					SetUpErrorHandling();

					_applicationContainer = new ApplicationContainer();

					if (args.Length == 2 && args[0].ToLowerInvariant() == "--upload")
					{
						// A special path to upload chunks of stuff. This is not currently documented and is not very robust.
						// - User must log in before running this
						// - For best results each bloom book needs to be part of a collection in its parent folder
						// - little error checking (e.g., we don't apply the usual constaints that a book must have title and licence info)
						SetUpLocalization();
						Browser.SetUpXulRunner();
						var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
							_applicationContainer.HtmlThumbnailer, new BookDownloadStartingEvent())/*not hooked to anything*/;
						transfer.UploadFolder(args[1], _applicationContainer);
						return;
					}

					new BookDownloadSupport(); // creating this sets some things up so we can download.

					SetUpLocalization();
					Logger.Init();


					if (args.Length == 1)
					{
						Debug.Assert(args[0].ToLower().EndsWith(".bloomcollection")); // Anything else handled above.
						Settings.Default.MruProjects.AddNewPath(args[0]);
					}

					if (args.Length > 0 && args[0] == "--rename")
					{
						try
						{
							var pathToNewCollection = CollectionSettings.RenameCollection(args[1], args[2]);
							//MessageBox.Show("Your collection has been renamed.");
							Settings.Default.MruProjects.AddNewPath(pathToNewCollection);
						}
						catch (ApplicationException error)
						{
							Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, error.Message);
							Environment.Exit(-1);
						}
						catch (Exception error)
						{
							Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,
								"Bloom could not finish renaming your collection folder. Restart your computer and try again.");
							Environment.Exit(-1);
						}

					}
					Browser.SetUpXulRunner();
					L10NSharp.LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);

					FinishStartup();
				}
			}
			finally
			{
				ReleaseMutexForBloom();
			}
		}

		private static void FinishStartup()
		{
			_earliestWeShouldCloseTheSplashScreen = DateTime.Now.AddSeconds(3);

			Settings.Default.Save();

			Application.Idle += Startup;

			try
			{
				Application.Run();
			}
			catch (System.AccessViolationException nasty)
			{
				Logger.ShowUserATextFileRelatedToCatastrophicError(nasty);
				System.Environment.FailFast("AccessViolationException");
			}

			Settings.Default.Save();

			Logger.ShutDown();


			if (_projectContext != null)
				_projectContext.Dispose();
		}

		private static bool IsBloomBookOrder(string[] args)
		{
			return args.Length == 1 && !args[0].EndsWith(".bloomcollection");
		}

		private static void Startup(object sender, EventArgs e)
		{
			Application.Idle -= Startup;
			CareForSplashScreenAtIdleTime(null, null);
			Application.Idle += new EventHandler(CareForSplashScreenAtIdleTime);
			StartUpShellBasedOnMostRecentUsedIfPossible();
		}


		private static void CareForSplashScreenAtIdleTime(object sender, EventArgs e)
		{
			//this is a hack... somehow this is getting called again, haven't been able to track down how
			//to reproduce, remove the user settings so that we get first-run behavior. Instead of going through the
			//wizard, cancel it and open an existing project. After the new collectino window is created, this
			//fires *again* and would try to open a new splashform
			if (_alreadyHadSplashOnce)
			{
				Application.Idle -= CareForSplashScreenAtIdleTime;
				return;
			}
			if(_splashForm==null)
				_splashForm = SplashScreen.CreateAndShow();//warning: this does an ApplicationEvents()
			else if (DateTime.Now > _earliestWeShouldCloseTheSplashScreen)
			{
				_alreadyHadSplashOnce = true;
				Application.Idle -= CareForSplashScreenAtIdleTime;
				CloseSplashScreenAndCheckRegistration();
				if (_projectContext!=null && _projectContext.ProjectWindow != null)
				{
					var shell = _projectContext.ProjectWindow as Shell;
					if (shell != null)
					{
						shell.ReallyComeToFront();
					}
				}
			}
		}

		private static void CloseSplashScreenAndCheckRegistration()
		{
			if (_splashForm != null)
			{
				_splashForm.FadeAndClose(); //it's going to hang around while it fades,
				_splashForm = null; //but we are done with it
			}

			if (RegistrationDialog.ShouldWeShowRegistrationDialog())
			{
				using (var dlg = new RegistrationDialog(false))
				{
					if (_projectContext != null && _projectContext.ProjectWindow != null)
						dlg.ShowDialog(_projectContext.ProjectWindow);
					else
					{
						dlg.ShowDialog();
					}
				}
			}
		}


#if PerProjectMutex

					//NB: initially, you could have multiple blooms, if they were different projects.
			//however, then we switched to the embedded http image server, which can't share
			//a port. So we could fix that (get different ports), but for now, I'm just going
			//to lock it down to a single bloom

		private static bool GrabTokenForThisProject(string pathToProject)
		{
			//ok, here's how this complex method works...
			//First, we try to get the mutex quickly and quitely.
			//If that fails, we put up a dialog and wait a number of seconds,
			//while we wait for the mutex to come free.


			string mutexId = "bloom";
//			string mutexId = pathToProject;
//			mutexId = mutexId.Replace(Path.DirectorySeparatorChar, '-');
//			mutexId = mutexId.Replace(Path.VolumeSeparatorChar, '-');
			bool mutexAcquired = false;
			try
			{
				_oneInstancePerProjectMutex = Mutex.OpenExisting(mutexId);
				mutexAcquired = _oneInstancePerProjectMutex.WaitOne(TimeSpan.FromMilliseconds(1 * 1000), false);
			}
			catch (WaitHandleCannotBeOpenedException e)//doesn't exist, we're the first.
			{
				_oneInstancePerProjectMutex = new Mutex(true, mutexId, out mutexAcquired);
				mutexAcquired = true;
			}
			catch (AbandonedMutexException e)
			{
				//that's ok, we'll get it below
			}

			using (var dlg = new SimpleMessageDialog("Waiting for other Bloom to finish..."))
			{
				dlg.TopMost = true;
				dlg.Show();
				try
				{
					_oneInstancePerProjectMutex = Mutex.OpenExisting(mutexId);
					mutexAcquired = _oneInstancePerProjectMutex.WaitOne(TimeSpan.FromMilliseconds(10 * 1000), false);
				}
				catch (AbandonedMutexException e)
				{
					_oneInstancePerProjectMutex = new Mutex(true, mutexId, out mutexAcquired);
					mutexAcquired = true;
				}
				catch (Exception e)
				{
					ErrorReport.NotifyUserOfProblem(e,
						"There was a problem starting Bloom which might require that you restart your computer.");
				}
			}

			if (!mutexAcquired) // cannot acquire?
			{
				_oneInstancePerProjectMutex = null;
				ErrorReport.NotifyUserOfProblem("Another copy of Bloom is already open with " + pathToProject + ". If you cannot find that Bloom, restart your computer.");
				return false;
			}
			return true;
		}

		public static void ReleaseMutexForThisProject()
		{
			if (_oneInstancePerProjectMutex != null)
			{
				_oneInstancePerProjectMutex.ReleaseMutex();
				_oneInstancePerProjectMutex = null;
			}
		}
#endif

		private static bool GrabMutexForBloom()
		{
			//ok, here's how this complex method works...
			//First, we try to get the mutex quickly and quitely.
			//If that fails, we put up a dialog and wait a number of seconds,
			//while we wait for the mutex to come free.

			var mutexAcquired = TryToGrabMutexForBloom();

			using (var dlg = new SimpleMessageDialog("Waiting for other Bloom to finish..."))
			{
				dlg.TopMost = true;
				dlg.Show();
				try
				{
					_onlyOneBloomMutex = Mutex.OpenExisting(_mutexId);
					mutexAcquired = _onlyOneBloomMutex.WaitOne(TimeSpan.FromMilliseconds(10 * 1000), false);
				}
				catch (AbandonedMutexException e)
				{
					_onlyOneBloomMutex = new Mutex(true, _mutexId, out mutexAcquired);
					mutexAcquired = true;
				}
				catch (Exception e)
				{
					ErrorReport.NotifyUserOfProblem(e,
						"There was a problem starting Bloom which might require that you restart your computer.");
				}
			}

			if (!mutexAcquired) // cannot acquire?
			{
				_onlyOneBloomMutex = null;
				ErrorReport.NotifyUserOfProblem("Another copy of Bloom is already running. If you cannot find that Bloom, restart your computer.");
				return false;
			}
			return true;
		}

		private static bool TryToGrabMutexForBloom()
		{
			bool mutexAcquired = false;
			_onlyOneBloomMutex = new Mutex(true, _mutexId, out mutexAcquired);
			if (!mutexAcquired)
			{
				// Already existed...see if we can get it.
				mutexAcquired = _onlyOneBloomMutex.WaitOne(TimeSpan.FromMilliseconds(1 * 1000), false);
			}

			return mutexAcquired;
		}

		public static void ReleaseMutexForBloom()
		{
			if (_onlyOneBloomMutex != null)
			{
				_onlyOneBloomMutex.ReleaseMutex();
				_onlyOneBloomMutex = null;
			}
		}

		/// ------------------------------------------------------------------------------------
		private static void StartUpShellBasedOnMostRecentUsedIfPossible()
		{
			if (Settings.Default.MruProjects.Latest == null  ||
				!OpenProjectWindow(Settings.Default.MruProjects.Latest))
			{
				//since the message pump hasn't started yet, show the UI for choosing when it is //review june 2013... is it still not going, with the current splash screen?
				Application.Idle += ChooseAnotherProject;
			}
		}

		/// ------------------------------------------------------------------------------------
		private static bool OpenProjectWindow(string projectPath)
		{
			Debug.Assert(_projectContext == null);

			try
			{
				//NB: initially, you could have multiple blooms, if they were different projects.
				//however, then we switched to the embedded http image server, which can't share
				//a port. So we could fix that (get different ports), but for now, I'm just going
				//to lock it down to a single bloom
/*					if (!GrabTokenForThisProject(projectPath))
					{
						return false;
					}
				*/
				_projectContext = _applicationContainer.CreateProjectContext(projectPath);
				_projectContext.ProjectWindow.Closed += HandleProjectWindowClosed;
				_projectContext.ProjectWindow.Activated += HandleProjectWindowActivated;

				_projectContext.ProjectWindow.Show();

				if(_splashForm!=null)
					_splashForm.StayAboveThisWindow(_projectContext.ProjectWindow);

				return true;
			}
			catch (Exception e)
			{
				HandleErrorOpeningProjectWindow(e, projectPath);
			}

			return false;
		}

		private static void HandleProjectWindowActivated(object sender, EventArgs e)
		{
			_projectContext.ProjectWindow.Activated -= HandleProjectWindowActivated;

			// Sometimes after closing the splash screen the project window
			// looses focus, so do this.
			_projectContext.ProjectWindow.Activate();
		}



		/// ------------------------------------------------------------------------------------
		private static void HandleErrorOpeningProjectWindow(Exception error, string projectPath)
		{
			if (_projectContext != null)
			{
				if (_projectContext.ProjectWindow != null)
				{
					_projectContext.ProjectWindow.Closed -= HandleProjectWindowClosed;
					_projectContext.ProjectWindow.Close();
				}

				_projectContext.Dispose();
				_projectContext = null;
			}

			Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
				new Palaso.Reporting.ShowAlwaysPolicy(), error,
				"{0} had a problem loading the {1} project. Please report this problem to the developers by clicking 'Details' below.",
				Application.ProductName, Path.GetFileNameWithoutExtension(projectPath));
		}

		/// ------------------------------------------------------------------------------------
		static void ChooseAnotherProject(object sender, EventArgs e)
		{
			Application.Idle -= ChooseAnotherProject;

			while (true)
			{
				//If it looks like the 1st time, put up the create collection with the welcome.
				//The user can cancel that if they want to go looking for a collection on disk.
				if(Settings.Default.MruProjects.Latest == null)
				{
					var path = NewCollectionWizard.CreateNewCollection();
					if (!string.IsNullOrEmpty(path) && File.Exists(path))
					{
						OpenCollection(path);
						return;
					}
				}

				using (var dlg = _applicationContainer.OpenAndCreateCollectionDialog())
				{
					if (dlg.ShowDialog() != DialogResult.OK)
					{
						Application.Exit();
						return;
					}

					if (OpenCollection(dlg.SelectedPath)) return;
				}
			}
		}

		private static bool OpenCollection(string path)
		{
			if (OpenProjectWindow(path))
			{
				Settings.Default.MruProjects.AddNewPath(path);
				Settings.Default.Save();
				return true;
			}
			return false;
		}

		/// ------------------------------------------------------------------------------------
		static void HandleProjectWindowClosed(object sender, EventArgs e)
		{
			try
			{
				_projectContext.SendReceiver.CheckPointWithDialog("Storing History Of Your Work");
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,"There was a problem backing up your work to the SendReceive repository on this computer.");
			}

			_projectContext.Dispose();
			_projectContext = null;

			if (((Shell)sender).UserWantsToOpenADifferentProject)
			{
				Application.Idle += ChooseAnotherProject;
			}
			else if (((Shell)sender).UserWantsToOpeReopenProject)
			{
				Application.Idle +=new EventHandler(ReopenProject);
			}
			else
			{
				Application.Exit();
			}
		}


		private static void ReopenProject(object sender, EventArgs e)
		{
			Application.Idle -= ReopenProject;
			OpenCollection(Settings.Default.MruProjects.Latest);
		}

		public static void SetUpLocalization()
		{
			var installedStringFileFolder = FileLocator.GetDirectoryDistributedWithApplication("localization");

			try
			{
				_applicationContainer.LocalizationManager = LocalizationManager.Create(Settings.Default.UserInterfaceLanguage,
										   "Bloom", "Bloom", Application.ProductVersion,
										   installedStringFileFolder,
										   "SIL/Bloom",
										   Resources.Bloom, "issues@bloomlibrary.org", "Bloom");

				//We had a case where someone translated stuff into another language, and sent in their tmx. But their tmx had soaked up a bunch of string
				//from their various templates, which were not Bloom standard templates. So then someone else sitting down to localize bloom would be
				//faced with a bunch of string that made no sense to them, because they don't have those templates.
				//So for now, we only soak up new strings if it's a developer, and hope that the Commit process will be enough for them to realize "oh no, I
				//don't want to check that stuff in".

#if DEBUG
				_applicationContainer.LocalizationManager.CollectUpNewStringsDiscoveredDynamically = true;
#else
				_applicationContainer.LocalizationManager.CollectUpNewStringsDiscoveredDynamically = false;
#endif

				var uiLanguage =   LocalizationManager.UILanguageId;//just feeding this into subsequent creates prevents asking the user twice if the language of their os isn't one we have a tmx for
				var unusedGoesIntoStatic = LocalizationManager.Create(uiLanguage,
										   "Palaso", "Palaso", /*review: this is just bloom's version*/Application.ProductVersion,
										   installedStringFileFolder,
											"SIL/Bloom",
											Resources.Bloom, "issues@bloomlibrary.org", "Palaso.UI");

				Settings.Default.UserInterfaceLanguage = LocalizationManager.UILanguageId;
			}
			catch (Exception error)
			{
				//handle http://jira.palaso.org/issues/browse/BL-213
				if(Process.GetProcesses().Count(p=>p.ProcessName.ToLower().Contains("bloom"))>1)
				{
					ErrorReport.NotifyUserOfProblem("Whoops. There is another copy of Bloom already running while Bloom was trying to set up L10NSharp.");
					Environment.FailFast("Bloom couldn't set up localization");
				}

				if (error.Message.Contains("Bloom.en.tmx"))
				{
					ErrorReport.NotifyUserOfProblem(error,
						"Sorry. Bloom is trying to set up your machine to use this new version, but something went wrong getting at the file it needs. If you restart your computer, all will be well.");

					Environment.FailFast("Bloom couldn't set up localization");
				}

				//otherwise, we don't know what caused it.
				throw;
			}
		}



		/// ------------------------------------------------------------------------------------
		private static void SetUpErrorHandling()
		{
			Palaso.Reporting.ErrorReport.EmailAddress = "issues@bloomlibrary.org";
			Palaso.Reporting.ErrorReport.AddStandardProperties();
			Palaso.Reporting.ExceptionHandler.Init();

			ExceptionHandler.AddDelegate((w,e) => DesktopAnalytics.Analytics.ReportException(e.Exception));
		}


		public static void OldVersionCheck()
		{
			return;




			var asm = Assembly.GetExecutingAssembly();
			var file = asm.CodeBase.Replace("file:", string.Empty);
			file = file.TrimStart('/');
			var fi = new FileInfo(file);
			if(DateTime.UtcNow.Subtract(fi.LastWriteTimeUtc).Days > 90)// nb: "create time" is stuck at may 2011, for some reason. Arrrggghhhh
				{
					try
					{
						if (Dns.GetHostAddresses("ftp.sil.org.pg").Length > 0)
						{
							if(DialogResult.Yes == MessageBox.Show("This beta version of Bloom is now over 90 days old. Click 'Yes' to have Bloom open the folder on the Ukarumpa FTP site where you can get a new one.","OLD BETA",MessageBoxButtons.YesNo))
							{
								Process.Start("ftp://ftp.sil.org.pg/Software/LCORE/LangTran/Groups/LangTran_win_Literacy/");
								Process.GetCurrentProcess().Kill();
							}
							return;
						}
					}
					catch (Exception)
					{
					}

					try
					{
						if (Dns.GetHostAddresses("bloomlibrary.org").Length > 0)
						{
							if (DialogResult.Yes == MessageBox.Show("This beta version of Bloom is now over 90 days old. Click 'Yes' to have Bloom open the web page where you can get a new one.", "OLD BETA", MessageBoxButtons.YesNo))
							{
								Process.Start("http://bloomlibrary.org/download");
								Process.GetCurrentProcess().Kill();
							}
							return;
						}
					}
					catch (Exception)
					{
					}

					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
						"This beta version of Bloom is now over 90 days old. If possible, please get a new version at bloomlibrary.org.");
			}

		}
	}


}
