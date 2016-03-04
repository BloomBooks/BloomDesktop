using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Configuration;
using Bloom.Collection;
using Bloom.Collection.BloomPack;
using Bloom.Properties;
using Bloom.Registration;
using Bloom.ToPalaso;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using Gecko;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Registration;
using SIL.Windows.Forms.Reporting;
using SIL.Windows.Forms.UniqueToken;
using System.Linq;
using Bloom.Edit;
using Bloom.MiscUI;
using SIL.Windows.Forms.HtmlBrowser;
using SIL.WritingSystems;

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
		public static bool ApplicationExiting;
		public static bool StartUpWithFirstOrNewVersionBehavior;

		private static GeckoWebBrowser _debugServerStarter;

#if PerProjectMutex
		private static Mutex _oneInstancePerProjectMutex;
#else
		private static DateTime _earliestWeShouldCloseTheSplashScreen;
		private static SplashScreen _splashForm;
		private static bool _alreadyHadSplashOnce;
		private static BookDownloadSupport _bookDownloadSupport;
#endif
		internal static string PathToBookDownloadedAtStartup { get; set; }

		private static bool _supressRegistrationDialog = false;

		[STAThread]
		[HandleProcessCorruptedStateExceptions]
		static void Main(string[] args1)
		{
			Logger.Init();
			CheckForCorruptUserConfig();

			//Debug.Fail("Attach Now");
			bool skipReleaseToken = false;
			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

				XWebBrowser.DefaultBrowserType = XWebBrowser.BrowserType.GeckoFx;

				var args = args1;

				if (SIL.PlatformUtilities.Platform.IsWindows)
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
					Settings.Default.MaximizeWindow = true; // this is needed to force this to be written to the file, where a user can find it to modify it by hand (our video maker)
					Settings.Default.Save();
					
					StartUpWithFirstOrNewVersionBehavior = true;
				}

				if (IsInstallerLaunch(args))
				{
					InstallerSupport.HandleSquirrelInstallEvent(args); // may exit program
				}

				// Needs to be AFTER HandleSquirrelInstallEvent, because that can happen when the program is launched by Update rather than
				// by the user.
				if (!Settings.Default.LicenseAccepted)
				{
					Browser.SetUpXulRunner();
					using (var dlg = new LicenseDialog())
						if (dlg.ShowDialog() != DialogResult.OK)
							return;
					Settings.Default.LicenseAccepted = true;
					Settings.Default.Save();
				}

#if !USING_CHORUS
				Settings.Default.ShowSendReceive = false; // in case someone turned it on before we disabled
#endif
#if DEBUG
				if (args.Length > 0)
				{
					// This allows us to debug things like  interpreting a URL.
					MessageBox.Show("Attach debugger now");
				}
#endif

				// Ensures that registration settings for all channels of Bloom are stored in a common place,
				// so the user is not asked to register each independently.
				RegistrationSettingsProvider.SetProductName("Bloom");

				Dictionary<string, string> propertiesThatGoWithEveryEvent = ErrorReport.GetStandardProperties();
				propertiesThatGoWithEveryEvent.Remove("MachineName");
				propertiesThatGoWithEveryEvent.Remove("UserName");
				propertiesThatGoWithEveryEvent.Remove("UserDomainName");
				propertiesThatGoWithEveryEvent.Add("channel", ApplicationUpdateSupport.ChannelName);

#if DEBUG
				using (new DesktopAnalytics.Analytics("sje2fq26wnnk8c2kzflf", RegistrationDialog.GetAnalyticsUserInfo(), propertiesThatGoWithEveryEvent, true))
#else
				string feedbackSetting = System.Environment.GetEnvironmentVariable("FEEDBACK");

				//default is to allow tracking
				var allowTracking = string.IsNullOrEmpty(feedbackSetting) || feedbackSetting.ToLowerInvariant() == "yes"
					|| feedbackSetting.ToLowerInvariant() == "true";

				using (new DesktopAnalytics.Analytics("c8ndqrrl7f0twbf2s6cv", RegistrationDialog.GetAnalyticsUserInfo(), propertiesThatGoWithEveryEvent, allowTracking))

#endif

				{
					// do not show the registration dialog if bloom was started for a special purpose
					if (args.Length > 0) _supressRegistrationDialog = true;

					if (args.Length == 1 && args[0].ToLowerInvariant().EndsWith(".bloompack"))
					{
						SetUpErrorHandling();
						using (_applicationContainer = new ApplicationContainer())
						{
							SetUpLocalization();
							
							var path = args[0];
							// This allows local links to bloom packs.
							if (path.ToLowerInvariant().StartsWith("bloom://"))
							{
								path = path.Substring("bloom://".Length);
								if (!File.Exists(path))
								{
									path = FileLocator.GetFileDistributedWithApplication(true, path);
									if (!File.Exists(path))
										return;
								}
							}
							using (var dlg = new BloomPackInstallDialog(path))
							{
								dlg.ShowDialog();
							}
							return;
						}
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
						using (_applicationContainer = new ApplicationContainer())
						{
							SetUpLocalization();
							InstallerSupport.MakeBloomRegistryEntries();
							Browser.SetUpXulRunner();
							Browser.XulRunnerShutdown += OnXulRunnerShutdown;
							LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
							var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
								_applicationContainer.BookThumbNailer, new BookDownloadStartingEvent())/*not hooked to anything*/;
							transfer.HandleBloomBookOrder(args[0]);
							PathToBookDownloadedAtStartup = transfer.LastBookDownloadedPath;

							// If another instance is running, this one has served its purpose and can exit right away.
							// Otherwise, carry on with starting up normally.
							if (UniqueToken.AcquireTokenQuietly(_mutexId))
								Run();
							else
							{
								skipReleaseToken = true; // we don't own it, so we better not try to release it

								// BL-2143: Don't show download complete message if download was not successful
								if (!string.IsNullOrEmpty(PathToBookDownloadedAtStartup))
								{
									var caption = LocalizationManager.GetString("Download.CompletedCaption", "Download complete");
									var message = LocalizationManager.GetString("Download.Completed",
										@"Your download ({0}) is complete. You can see it in the 'Books from BloomLibrary.org' section of your Collections.");
									message = string.Format(message, Path.GetFileName(PathToBookDownloadedAtStartup));
									MessageBox.Show(message, caption);
								}
							}
							return;
						}
					}

					if (!UniqueToken.AcquireToken(_mutexId, "Bloom"))
						return;

					OldVersionCheck();

					SetUpErrorHandling();

					using (_applicationContainer = new ApplicationContainer())
					{
						if (args.Length == 2 && args[0].ToLowerInvariant() == "--upload")
						{
							// A special path to upload chunks of stuff. This is not currently documented and is not very robust.
							// - User must log in before running this
							// - For best results each bloom book needs to be part of a collection in its parent folder
							// - little error checking (e.g., we don't apply the usual constraints that a book must have title and licence info)
							SetUpLocalization();
							Browser.SetUpXulRunner();
								Browser.XulRunnerShutdown += OnXulRunnerShutdown;
							var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
								_applicationContainer.BookThumbNailer, new BookDownloadStartingEvent()) /*not hooked to anything*/;
							transfer.UploadFolder(args[1], _applicationContainer);
							return;
						}

						InstallerSupport.MakeBloomRegistryEntries();

						SetUpLocalization();


						if (args.Length == 1 && !IsInstallerLaunch(args))
						{
							Debug.Assert(args[0].ToLowerInvariant().EndsWith(".bloomcollection")); // Anything else handled above.
							if (CollectionChoosing.OpenCreateCloneControl.ReportIfInvalidCollectionToEdit(args[0]))
								return;
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
								SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, error.Message);
								Environment.Exit(-1);
							}
							catch (Exception error)
							{
								SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,
									"Bloom could not finish renaming your collection folder. Restart your computer and try again.");
								Environment.Exit(-1);
							}

						}
						Browser.SetUpXulRunner();
						Browser.XulRunnerShutdown += OnXulRunnerShutdown;
#if DEBUG
						StartDebugServer();
#endif

						if(!BloomIntegrityDialog.CheckIntegrity())
						{
							Environment.Exit(-1);
						}

						LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);

						// BL-1258: sometimes the newly installed fonts are not available until after Bloom restarts
						if (FontInstaller.InstallFont("AndikaNewBasic")) 
							return;

						Run();
					}
				}
			}
			finally
			{
				// Check memory one final time for the benefit of developers.  The user won't see anything.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "Bloom finished and exiting", false);
				if (!skipReleaseToken)
					UniqueToken.ReleaseToken();
			}
		}

		public static void RestartBloom()
		{
			try
			{
				Process.Start(Application.ExecutablePath);

				//give some time for that process.start to finish staring the new instance, which will see
				//we have a mutex and wait for us to die.

				Thread.Sleep(2000);
				Environment.Exit(-1); //Force termination of the current process.
			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Bloom encounterd a problem while restarting.");
			}
		}

		private static bool IsInstallerLaunch(string[] args)
		{
			return args.Length > 0 &&  args[0].ToLowerInvariant().StartsWith("--squirrel");
		}

		// I think this does something like the Wix element
		// <ProgId Id='Bloom.BloomCollectionFile' Description='BloomPack file' >
		//   <Extension Id='BloomCollectionFile' ContentType='application/bloom'>
		//	   <!-- I know application/bloom looks weird, but it copies MSword docs -->
		//	   <Verb Id='open' Command='Open' TargetFile ='Bloom.exe' Argument='"%1"' />
		//   </Extension>
		// </ProgId>
		// (But I'm not completely sure all these come from that)

		// The folder where we tell squirrel to look for upgrades.
		// As of 2-20-15 this is  = @"https://s3.amazonaws.com/bloomlibrary.org/squirrel";
		// Controlled by the file at "http://bloomlibrary.org/channels/SquirrelUpgradeTable.txt".
		// This allows us to have different sets of deltas and upgrade targets for betas and stable releases,
		// or indeed to do something special for any particular version(s) of Bloom,
		// or even to switch to a different upgrade path after releasing a version.

		private static void OnXulRunnerShutdown(object sender, EventArgs e)
		{
			ApplicationExiting = true;
			Browser.XulRunnerShutdown -= OnXulRunnerShutdown;
			if (_debugServerStarter != null)
				_debugServerStarter.Dispose();
			_debugServerStarter = null;
		}

		private static void Run()
		{
			_earliestWeShouldCloseTheSplashScreen = DateTime.Now.AddSeconds(3);

			Settings.Default.Save();

			Application.Idle += Startup;

			Sldr.Initialize();
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
			Sldr.Cleanup();
			Logger.ShutDown();


			if (_projectContext != null)
				_projectContext.Dispose();
		}

		private static bool IsBloomBookOrder(string[] args)
		{
			return args.Length == 1 && !args[0].ToLowerInvariant().EndsWith(".bloomcollection") && !IsInstallerLaunch(args);
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
				// BL-3192. If there is some modal in front (e.g. dropbox or screen DPI warnings), just wait. We'll keep getting called with these
				// on idle warnings until it closes, then we can proceed.
				if (_splashForm.Visible && !_splashForm.CanFocus)
				{
					return;
				}
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
				if (RegistrationDialog.ShouldWeShowRegistrationDialog())
				{
					_splashForm.Hide();//the fading was getting stuck when we showed the registration.
				}
				_splashForm.FadeAndClose(); //it's going to hang around while it fades,
				_splashForm = null; //but we are done with it
			}

			if (RegistrationDialog.ShouldWeShowRegistrationDialog() && !_supressRegistrationDialog)
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


		/// ------------------------------------------------------------------------------------
		private static void StartUpShellBasedOnMostRecentUsedIfPossible()
		{
			var path = Settings.Default.MruProjects.Latest;

			if (!string.IsNullOrEmpty(path))
			{
				CollectionChoosing.OpenCreateCloneControl.CheckForBeingInDropboxFolder(path);
				while (CollectionChoosing.OpenCreateCloneControl.IsInvalidCollectionToEdit(path))
				{
					// Somehow...from a previous version?...we have an invalid file in our MRU list.
					Settings.Default.MruProjects.RemovePath(path);
					path = Settings.Default.MruProjects.Latest;
				}
			}

			if (path == null || !OpenProjectWindow(path))
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
#if DEBUG
				CheckLinuxFileAssociations();
#endif
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

			SIL.Reporting.ErrorReport.NotifyUserOfProblem(
				new SIL.Reporting.ShowAlwaysPolicy(), error,
				"{0} had a problem loading the {1} project. Please report this problem to the developers by clicking 'Details' below.",
				Application.ProductName, Path.GetFileNameWithoutExtension(projectPath));
		}

		/// ------------------------------------------------------------------------------------
		static void ChooseAnotherProject(object sender, EventArgs e)
		{
			Application.Idle -= ChooseAnotherProject;

			while (true)
			{
				// We decided to stop doing this (BL-1229) since the wizard can feel like part
				// of installation that might be irrevocable.
				////If it looks like the 1st time, put up the create collection with the welcome.
				////The user can cancel that if they want to go looking for a collection on disk.
				//if(Settings.Default.MruProjects.Latest == null)
				//{
				//	var path = NewCollectionWizard.CreateNewCollection();
				//	if (!string.IsNullOrEmpty(path) && File.Exists(path))
				//	{
				//		OpenCollection(path);
				//		return;
				//	}
				//}

				using (var dlg = _applicationContainer.OpenAndCreateCollectionDialog())
				{
					dlg.StartPosition = FormStartPosition.Manual; // try not to have it under the splash screen
					dlg.SetDesktopLocation(50,50);
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
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,"There was a problem backing up your work to the SendReceive repository on this computer.");
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
			else if (((Shell)sender).QuitForVersionUpdate)
			{
				Application.Exit();
			}
			else
			{
				Application.Exit();
			}
		}

		static nsILocalFile toNsFile(string file)
		{
			var nsfile = Xpcom.CreateInstance<nsILocalFile>("@mozilla.org/file/local;1");
			nsfile.InitWithPath(new nsAString(file));
			return nsfile;
		}

		static void registerChromeDir(string dir)
		{
			var chromeDir = toNsFile(dir);
			var chromeFile = chromeDir.Clone();
			chromeFile.Append(new nsAString("chrome.manifest"));
			Xpcom.ComponentRegistrar.AutoRegister(chromeFile);
			Xpcom.ComponentManager.AddBootstrappedManifestLocation(chromeDir);
		}

		/// <summary>
		/// This code (and the two methods above) were taken from https://bitbucket.org/duanyao/moz-devtools-patch
		/// with thanks to Duane Yao.
		/// It starts up a server that allows FireFox to be used to inspect and debug the content of geckofx windows.
		/// See the ReadMe in remoteDebugging for instructions.
		/// Note that this should NOT be done in production. There are security issues.
		/// </summary>
		static void StartDebugServer()
		{
			GeckoPreferences.User["devtools.debugger.remote-enabled"] = true;

			// It seems these files MUST be in a subdirectory of the application directory. At least, I haven't figured out
			// how it can be anywhere else. Therefore the build copies the necessary files there.
			// If you try to change it, be aware that the chrome.manifest file contains the name of the parent folder;
			// if you rename the folder and don't change the name there, you get navigation errors in the code below and
			// remote debugging doesn't work.
			var chromeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remoteDebugging");
			registerChromeDir(chromeDir);
			_debugServerStarter = new GeckoWebBrowser();
			_debugServerStarter.NavigationError += (s, e) => {
				Console.WriteLine(">>>StartDebugServer error: " + e.ErrorCode.ToString("X"));
				_debugServerStarter.Dispose();
				_debugServerStarter = null;
			};
			_debugServerStarter.DocumentCompleted += (s, e) => {
				Console.WriteLine(">>>StartDebugServer complete");
				_debugServerStarter.Dispose();
				_debugServerStarter = null;
			};
			_debugServerStarter.Navigate("chrome://remoteDebugging/content/moz-remote-debug.html");
		}

		private static void ReopenProject(object sender, EventArgs e)
		{
			Application.Idle -= ReopenProject;
			OpenCollection(Settings.Default.MruProjects.Latest);
		}

		public static void SetUpLocalization()
		{
			var installedStringFileFolder = FileLocator.GetDirectoryDistributedWithApplication(true,"localization");
			if (installedStringFileFolder == null)
			{
				// nb do NOT try to localize this...it's a shame, but the problem we're reporting is that the localization data is missing!
				var msg =
					@"Bloom seems to be missing some of the files it needs to run. Please uninstall Bloom, then install it again. If that's doesn't fix things, please contact us by clicking the ""Details"" button below, and we'd be glad to help.";
				ErrorReport.NotifyUserOfProblem(new ApplicationException("Missing localization directory"), msg);
				// If the user insists on continuing after that, start up using the built-in English.
				// We need an LM, and it needs some folder of tmx files, though it can be empty. So make a fake one.
				// Ideally we would dispose this at some point, but I don't know when we safely can. Normally this should never happen,
				// so I'm not very worried.
				var fakeLocalDir = new TemporaryFolder("Bloom fake localization").FolderPath;
				_applicationContainer.LocalizationManager = LocalizationManager.Create("en", "Bloom", "Bloom", Application.ProductVersion, fakeLocalDir, "SIL/Bloom",
										   Resources.Bloom, "issues@bloomlibrary.org",
											//the parameters that follow are namespace beginnings:
										   "Bloom");
				return;
			}

			try
			{
				_applicationContainer.LocalizationManager = LocalizationManager.Create(Settings.Default.UserInterfaceLanguage,
										   "Bloom", "Bloom", Application.ProductVersion,
										   installedStringFileFolder,
										   "SIL/Bloom",
										   Resources.Bloom, "issues@bloomlibrary.org",
										   //the parameters that follow are namespace beginnings:
										   "Bloom");

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
											Resources.Bloom, "issues@bloomlibrary.org", "SIL");

				Settings.Default.UserInterfaceLanguage = LocalizationManager.UILanguageId;
			}
			catch (Exception error)
			{
				//handle http://jira.palaso.org/issues/browse/BL-213
				if (GetRunningBloomProcessCount() > 1)
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
			ExceptionReportingDialog.PrivacyNotice = @"If you don't care who reads your bug report, you can skip this notice.

When you submit a crash report or other issue, the contents of your email go in our issue tracking system, ""YouTrack"", which is available via the web at https://silbloom.myjetbrains.com. This is the normal way to handle issues in an open-source project.

Our issue-tracking system is searchable by anyone. Search engines (like Google) should not search it, so someone searching with Google should not see your report, but we can't promise this for all search engines.

Anyone looking specifically at our issue tracking system can read what you sent us. So if you have something private to say, please send it to one of the developers privately with a note that you don't want the issue in our issue tracking system. If need be, we'll make some kind of sanitized place-holder for your issue so that we don't lose it.
";
			SIL.Reporting.ErrorReport.EmailAddress = "issues@bloomlibrary.org";
			SIL.Reporting.ErrorReport.AddStandardProperties();
			SIL.Reporting.ExceptionHandler.Init();

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

					SIL.Reporting.ErrorReport.NotifyUserOfProblem(
						"This beta version of Bloom is now over 90 days old. If possible, please get a new version at bloomlibrary.org.");
			}

		}

		/// <summary>
		/// Creates mime types and file associations on developer machine
		/// </summary>
		private static void CheckLinuxFileAssociations()
		{
			if (!SIL.PlatformUtilities.Platform.IsLinux)
				return;

			// on Linux, Environment.SpecialFolder.LocalApplicationData defaults to ~/.local/share
			var shareDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			var mimeDir = Path.Combine(shareDir, "mime", "packages");                   // check for mime-type files in ~/.local/share/mime/packages
			var imageDir = Path.Combine(shareDir, "icons", "hicolor", "48x48", "apps"); // check for mime-type icons in ~/.local/share/icons/hicolor/48x48/apps

			// make sure target directories exist
			if (!Directory.Exists(mimeDir))
				Directory.CreateDirectory(mimeDir);

			if (!Directory.Exists(imageDir))
				Directory.CreateDirectory(imageDir);

			// list of files to copy
			var updateNeeded = false;
			var filesToCheck = new System.Collections.Generic.Dictionary<string, string>
			{
				{"bloom-collection.sharedmimeinfo", Path.Combine(mimeDir, "application-bloom-collection.xml")},
				{"bloom-collection.png", Path.Combine(imageDir, "application-bloom-collection.png")}
			}; // Dictionary<sourceFileName, destinationFullFileName>

			// check each file now
			var sourceDir = FileLocator.DirectoryOfApplicationOrSolution;
			foreach(var entry in filesToCheck)
			{
				var destFile = entry.Value;
				if (!File.Exists(destFile))
				{
					var sourceFile = Path.Combine(sourceDir, "debian", entry.Key);
					if (File.Exists(sourceFile))
					{
						updateNeeded = true;
						File.Copy(sourceFile, destFile);
					}
				}
			}

			if (!updateNeeded) return;

			// if there were changes, notify the system
			var proc = new Process
			{
				StartInfo = {
					FileName = "update-desktop-database",
					Arguments = Path.Combine(shareDir, "applications"),
					UseShellExecute = false
				},
				EnableRaisingEvents = true // so we can run another process when this one finishes
			};

			// after the desktop database is updated, update the mime database
			proc.Exited += (sender, eventArgs) => {
				var proc2 = new Process
				{
					StartInfo = {
						FileName = "update-mime-database",
						Arguments = Path.Combine(shareDir, "mime"),
						UseShellExecute = false
					},
					EnableRaisingEvents = true // so we can run another process when this one finishes
				};

				// after the mime database is updated, set the file association
				proc2.Exited += (sender2, eventArgs2) => {
					var proc3 = new Process
					{
						StartInfo = {
							FileName = "xdg-mime",
							Arguments = "default bloom.desktop application/bloom-collection",
							UseShellExecute = false
						}
					};

					Debug.Print("Setting file association");
					proc3.Start();
				};

				Debug.Print("Executing update-mime-database");
				proc2.Start();
			};

			Debug.Print("Executing update-desktop-database");
			proc.Start();
		}

		/// <summary>
		/// Getting the count of running Bloom instances takes extra steps on Linux.
		/// </summary>
		/// <returns>The number of running Bloom instances</returns>
		public static int GetRunningBloomProcessCount()
		{
			var bloomProcessCount = Process.GetProcesses().Count(p => p.ProcessName.ToLowerInvariant().Contains("bloom"));

			// This is your count on Windows.
			if (SIL.PlatformUtilities.Platform.IsWindows)
				return bloomProcessCount;

			// On Linux, the process name is usually "mono-sgen" or something similar, but not all processes
			// with this name are instances of Bloom.
			var processes = Process.GetProcesses().Where(p => p.ProcessName.ToLowerInvariant().StartsWith("mono"));

			// DO NOT change this foreach loop into a LINQ expression. It takes longer to complete if you do.
			foreach (var p in processes)
			{
				bloomProcessCount += p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName == "Bloom.exe") ? 1 : 0;
			}

			return bloomProcessCount;
		}

		public static BloomFileLocator OptimizedFileLocator
		{
			get { return _projectContext.OptimizedFileLocator; }
		}

		private static void CheckForCorruptUserConfig()
		{
			//First check the user.config we get through using the palaso stuff.  This is the one in a folder with a name like Bloom/3.5.0.0
			var palasoSettings = new SIL.Settings.CrossPlatformSettingsProvider();
			palasoSettings.Initialize(null,null);
			var error = palasoSettings.CheckForErrorsInSettingsFile();
			if (error != null)
			{
				//Note: this is probably too early to do anything more complicated that writing to a log...
				//Enhance: we might be able to do a MessageBox.Show(), but it would be better to save this error 
				//and inform the user later when the UI can interact with them.
				Logger.WriteEvent("error reading palaso user config: "+error.Message);
				Logger.WriteEvent("Should self-heal");
			}

			//Now check the plain .net user.config we also use (sigh). This is the one in a folder with a name like Bloom.exe_Url_avygitvf1lws5lpjrmoh5j0ggsx4tkj0

			//roughly from http://stackoverflow.com/questions/9572243/what-causes-user-config-to-empty-and-how-do-i-restore-without-restarting
			try
			{
				ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
			}
			catch (ConfigurationErrorsException ex)
			{
				Logger.WriteEvent("Cannot open user config file "+ex.Filename);
				Logger.WriteEvent(ex.Message);

				if (File.Exists(ex.Filename))
				{
					Logger.WriteEvent("Config file content:\n{0}", File.ReadAllText(ex.Filename));
					Logger.WriteEvent("Deleting "+ ex.Filename);
					File.Delete(ex.Filename);
					Properties.Settings.Default.Upgrade();
					// Properties.Settings.Default.Reload();
					// you could optionally restart the app instead
				}
				else
				{
					Logger.WriteEvent("Config file {0} does not exist", ex.Filename);
				}
			}
		}
	}
}
