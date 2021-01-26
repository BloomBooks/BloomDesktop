using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
using System.Xml;
using Bloom.Book;
using Bloom.CLI;
using Bloom.TeamCollection;
using Bloom.MiscUI;
using Bloom.web;
using CommandLine;
using Sentry;
using Sentry.Protocol;
using SIL.Windows.Forms.HtmlBrowser;
using SIL.WritingSystems;
using SIL.Xml;

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
		private static int _uiThreadId;
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

#if !__MonoCS__
		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int pid);
#endif
		/// <summary>
		/// The UI language of the system when the program starts
		/// </summary>
		internal static CultureInfo UserInterfaceCulture = CultureInfo.CurrentUICulture;

		public static bool RunningOnUiThread => Thread.CurrentThread.ManagedThreadId == _uiThreadId;

		// Background threads creating thumbnails need to be canceled if the project window
		// closes down, which shuts down the BloomServer.
		// See https://silbloom.myjetbrains.com/youtrack/issue/BL-6214.
		public static CancellationTokenSource BloomThreadCancelService;

		public static SynchronizationContext MainContext { get; private set; }

		[STAThread]
		[HandleProcessCorruptedStateExceptions]
		static int Main(string[] args1)
		{
			_uiThreadId = Thread.CurrentThread.ManagedThreadId;
			Logger.Init();
			CheckForCorruptUserConfig();
			// We use crowdin for localizing, and they require a directory per language setup.
			LocalizationManager.UseLanguageCodeFolders = true;
			// We want only good localizations in Bloom.
			// REVIEW: should the setting be used only for alpha and beta?
			LocalizationManager.ReturnOnlyApprovedStrings = !Settings.Default.ShowUnapprovedLocalizations;

#if DEBUG
			//MessageBox.Show("Attach debugger now");
#endif
			// Bloom has several command line scenarios, without a coherent system for them.
			// The following is how we will do things from now on, and things can be moved
			// into this as time allows. See CommandLineOptions.cs.
			if (args1.Length > 0 && new[] {"--help", "hydrate", "upload", "download", "getfonts", "changeLayout", "createArtifacts"}.Contains(args1[0])) //restrict using the commandline parser to cases were it should work
			{
#if !__MonoCS__
				AttachConsole(-1);
#endif

				RunningInConsoleMode = true;

				var exitCode = CommandLine.Parser.Default.ParseArguments(args1,
					new[] {typeof (HydrateParameters), typeof(UploadParameters), typeof(DownloadBookOptions), typeof (GetUsedFontsParameters), typeof(ChangeLayoutParameters), typeof(CreateArtifactsParameters)})
					.MapResult(
						(HydrateParameters opts) => HydrateBookCommand.Handle(opts),
						(UploadParameters opts) =>
						{
							using (InitializeAnalytics())
							{
								return UploadCommand.Handle(opts);
							}
						},
						(DownloadBookOptions opts) => DownloadBookCommand.HandleSilentDownload(opts),
						(GetUsedFontsParameters opts) => GetUsedFontsCommand.Handle(opts),
						(ChangeLayoutParameters opts) => ChangeLayoutCommand.Handle(opts),
						(CreateArtifactsParameters opts) => CreateArtifactsCommand.Handle(opts),
						errors =>
						{
							var code = 0;
							foreach(var error in errors)
							{
								if(!(error is HelpVerbRequestedError))
								{
									Debug.WriteLine(error.ToString());
									Console.WriteLine(error.ToString());
									code = 1;
								}
							}
							return code;
						});
				return exitCode; // we're done
			}

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
					using (var dlg = new LicenseDialog("license.htm"))
						if (dlg.ShowDialog() != DialogResult.OK)
							return 1;
					Settings.Default.LicenseAccepted = true;
					Settings.Default.Save();
				}

#if !USING_CHORUS
				Settings.Default.ShowSendReceive = false; // in case someone turned it on before we disabled
#endif
#if DEBUG
				if (args.Length > 0)
				{
					if (IsLocalizationHarvestingLaunch(args))
						LocalizationManager.IgnoreExistingEnglishTranslationFiles = true;
					else
						// This allows us to debug things like  interpreting a URL.
						MessageBox.Show("Attach debugger now");
				}
				var harvest = Environment.GetEnvironmentVariable("HARVEST_FOR_LOCALIZATION");
				if (!String.IsNullOrWhiteSpace(harvest) && (harvest.ToLowerInvariant() == "on" || harvest.ToLowerInvariant() == "yes"))
					LocalizationManager.IgnoreExistingEnglishTranslationFiles = true;
#endif

				using (InitializeAnalytics())
				{
					// do not show the registration dialog if bloom was started for a special purpose
					if (args.Length > 0)
						_supressRegistrationDialog = true;

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
								if (!RobustFile.Exists(path))
								{
									path = FileLocationUtilities.GetFileDistributedWithApplication(true, path);
									if (!RobustFile.Exists(path))
										return 1;
								}
							}
							using (var dlg = new BloomPackInstallDialog(path))
							{
								dlg.ShowDialog();
								if (dlg.ExitWithoutRunningBloom)
									return 1;
							}
						}
						// Continue with normal startup now we unpacked the bloompack.
						// Various other things will misinterpret the .bloompack argument if we leave it in args.
						args = new string[] { };
					}
					if (FolderTeamRepo.IsJoinTeamCollectionFile(args))
					{
						var newCollection = FolderTeamRepo.JoinCollectionTeam(args[0]);
						args = new string[] { }; // continue to open, but without args.
						Settings.Default.MruProjects.AddNewPath(newCollection);
						// and now we need to get the lock as usual before going on to load the new collection.
						if (!UniqueToken.AcquireToken(_mutexId, "Bloom"))
							return 1;
					} else
					if (IsBloomBookOrder(args))
					{
						HandleDownload(args[0]);
						// If another instance is running, this one has served its purpose and can exit right away. Otherwise,
						// carry on with starting up normally.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-3822.
						if (!UniqueToken.AcquireTokenQuietly(_mutexId))
							return 0;
					}
					else
					{
						// Check whether another instance of Bloom is running.  That should happen only when downloading a book because
						// BloomLibrary starts a new Bloom process even when one is already running.  But that is taken care of in the
						// other branch of this if/else.  So quit if we find another instance of Bloom running at this point.
						// (A message will pop up to tell the user about this situation if it happens.)
						if (!UniqueToken.AcquireToken(_mutexId, "Bloom"))
							return 1;
					}
					OldVersionCheck();

					SetUpErrorHandling();

					using (_applicationContainer = new ApplicationContainer())
					{
						InstallerSupport.MakeBloomRegistryEntries(args);
						BookDownloadSupport.EnsureDownloadFolderExists();

						SetUpLocalization();


						if (args.Length == 1 && !IsInstallerLaunch(args) && !IsLocalizationHarvestingLaunch(args))
						{
							Debug.Assert(args[0].ToLowerInvariant().EndsWith(".bloomcollection")); // Anything else handled above.
							if (CollectionChoosing.OpenCreateCloneControl.ReportIfInvalidCollectionToEdit(args[0]))
								return 1;
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
						if (SIL.PlatformUtilities.Platform.IsWindows)
							StartDebugServer();
#endif

						if (!BloomIntegrityDialog.CheckIntegrity())
						{
							Environment.Exit(-1);
						}

						if (!DefenderFolderProtectionCheck.CanWriteToDirectory())
						{
							Environment.Exit(-1);
						}

						LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
						Browser.SetBrowserLanguage(Settings.Default.UserInterfaceLanguage);

						DialogAdapters.CommonDialogAdapter.ForceKeepAbove = true;
						DialogAdapters.CommonDialogAdapter.UseMicrosoftPositioning = true;

						// BL-1258: sometimes the newly installed fonts are not available until after Bloom restarts
						// We don't even want to try to install fonts if we are installed by an admin for all users;
						// it will have been installed already as part of the allUsers install.
						if (!InstallerSupport.SharedByAllUsers() && FontInstaller.InstallFont("AndikaNewBasic"))
							return 1;

						// This has served its purpose on Linux, and with Geckofx60 it interferes with CommandLineRunner.
						Environment.SetEnvironmentVariable("LD_PRELOAD", null);

						Run();
					}
				}
			}
			finally
			{
				// Check memory one final time for the benefit of developers.  The user won't see anything.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(true, "Bloom finished and exiting", false);
				UniqueToken.ReleaseToken();

				_sentry?.Dispose();
			}
			Settings.Default.FirstTimeRun = false;
			Settings.Default.Save();
			return 0;
		}

		/// <summary>
		/// Sets up different analytics channels depending on Debug or not.
		/// Also determines whether or not Registration should occur.
		/// </summary>
		/// <returns></returns>
		private static DesktopAnalytics.Analytics InitializeAnalytics()
		{
			// Ensures that registration settings for all channels of Bloom are stored in a common place,
			// so the user is not asked to register each independently.
			RegistrationSettingsProvider.SetProductName("Bloom");

			Dictionary<string, string> propertiesThatGoWithEveryEvent = ErrorReport.GetStandardProperties();
			propertiesThatGoWithEveryEvent.Remove("MachineName");
			propertiesThatGoWithEveryEvent.Remove("UserName");
			propertiesThatGoWithEveryEvent.Remove("UserDomainName");
			propertiesThatGoWithEveryEvent.Add("channel", ApplicationUpdateSupport.ChannelName);

#if DEBUG
			_supressRegistrationDialog = true;
			return new DesktopAnalytics.Analytics(
				"rw21mh2piu",
				RegistrationDialog.GetAnalyticsUserInfo(),
				propertiesThatGoWithEveryEvent,
				false); // change to true if you want to test sending
#else
			var feedbackSetting = System.Environment.GetEnvironmentVariable("FEEDBACK");

			//default is to allow tracking
			var allowTracking = string.IsNullOrEmpty(feedbackSetting) || feedbackSetting.ToLowerInvariant() == "yes"
				|| feedbackSetting.ToLowerInvariant() == "true" || feedbackSetting.ToLowerInvariant() == "on";
			_supressRegistrationDialog = _supressRegistrationDialog || !allowTracking;

			return new DesktopAnalytics.Analytics(
				"c8ndqrrl7f0twbf2s6cv",
				RegistrationDialog.GetAnalyticsUserInfo(),
				propertiesThatGoWithEveryEvent,
				allowTracking);
#endif
		}

#if DEBUG
		private static bool _harvestFinalized;
		private static object _harvestLock = new object();
#endif

		/// <summary>
		/// When exiting, finish any processing needed for harvesting strings for localization.
		/// </summary>
		/// <remarks>
		/// The lock and boolean check are to prevent this from being executed more than once
		/// as the program is shutting down.  Somehow this was being called twice before these
		/// checks were added.
		/// </remarks>
		public static void FinishLocalizationHarvesting()
		{
#if DEBUG
			lock(_harvestLock)
			{
				if (LocalizationManager.IgnoreExistingEnglishTranslationFiles && !_harvestFinalized)
				{
					var installedStringFileFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(true,"localization");
					LocalizationManager.MergeExistingEnglishTranslationFileIntoNew(installedStringFileFolder, "Bloom");
					LocalizationManager.MergeExistingEnglishTranslationFileIntoNew(installedStringFileFolder, "Palaso");
				}
				_harvestFinalized = true;
			}
#endif
		}

		/// <summary>
		/// This routine handles the old-style download requests which come as an order URL from BloomLibrary.
		/// Enhance: it's unfortunate that we have two command-line methods of downloading a book. However, they have
		/// rather different requirements:
		///   - this one displays a progress UI, the other doesn't.
		///   - this one must extract the URL from a bloom: url (which the library must produce with urlencoding),
		///			for the other, it's more convenient to pass an unencoded url
		///   - worse, this version typically goes on to fully launch Bloom; the other always shuts the program down
		///			when done. Thus, this version is much more tightly connected to the normal startup code.
		/// Note that we can't easily change the exact command line that this version deals with, because
		/// Bloom library generates that command line, and if we change what it generates, everyone running an
		/// older Bloom will be in trouble.
		/// Most of the core implementation of the download process is common.
		/// </summary>
		private static void HandleDownload(string order)
		{
			// We will start up just enough to download the book. This avoids the code that normally tries to keep only a single instance running.
			// There is probably a pathological case here where we are overwriting an existing template just as the main instance is trying to
			// do something with it. The time interval would be very short, because download uses a temp folder until it has the whole thing
			// and then copies (or more commonly moves) it over in one step, and making a book from a template involves a similarly short
			// step of copying the template to the new book. Hopefully users have (or will soon learn) enough sense not to
			// try to use a template while in the middle of downloading a new version.
			SetUpErrorHandling();
			using(_applicationContainer = new ApplicationContainer())
			{
				SetUpLocalization();
				//JT please review: is this needed? InstallerSupport.MakeBloomRegistryEntries(args);
				BookDownloadSupport.EnsureDownloadFolderExists();
				Browser.SetUpXulRunner();
				Browser.XulRunnerShutdown += OnXulRunnerShutdown;
				LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
				var transfer = new BookTransfer(new BloomParseClient(), ProjectContext.CreateBloomS3Client(),
					_applicationContainer.BookThumbNailer, new BookDownloadStartingEvent()) /*not hooked to anything*/;
				transfer.HandleBloomBookOrder(order);
				PathToBookDownloadedAtStartup = transfer.LastBookDownloadedPath;
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
		}

		public static void RestartBloom(bool hardExit, string args = null)
		{
			try
			{
				var program = Application.ExecutablePath;
				if (SIL.PlatformUtilities.Platform.IsLinux)
				{
					// This is needed until the day comes (if it ever does) when we can use the
					// system mono on Linux.
					program = "/opt/mono5-sil/bin/mono";
					if (args == null)
						args = "\"" + Application.ExecutablePath + "\"";
					else
						args = "\"" + Application.ExecutablePath + "\" " + args;
				}
				if (args == null)
					Process.Start(program);
				else
					Process.Start(program, args);

				//give some time for that process.start to finish starting the new instance, which will see
				//we have a mutex and wait for us to die.
				
				Thread.Sleep(2000);
				if (hardExit || _projectContext?.ProjectWindow == null)
				{
					Application.Exit(); 
				}
				else
				{
					_projectContext.ProjectWindow.Close();
				}
			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Bloom encountered a problem while restarting.");
			}
		}

		private static bool IsInstallerLaunch(string[] args)
		{
			return args.Length > 0 &&  args[0].ToLowerInvariant().StartsWith("--squirrel");
		}

		private static bool IsLocalizationHarvestingLaunch(string[] args)
		{
			return args.Length == 1 && args[0].StartsWith("--ha") &&  "--harvest-for-localization".StartsWith(args[0]);
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

		internal static void OnXulRunnerShutdown(object sender, EventArgs e)
		{
			ApplicationExiting = true;
			Browser.XulRunnerShutdown -= OnXulRunnerShutdown;
			if (_debugServerStarter != null)
				_debugServerStarter.Dispose();
			_debugServerStarter = null;
		}

		internal static void SetProjectContext(ProjectContext projectContext)
		{
			_projectContext = projectContext;
		}

		[HandleProcessCorruptedStateExceptions]
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
			catch (System.Reflection.TargetInvocationException bad)
			{
				if (bad.InnerException is System.AccessViolationException)
					Logger.ShowUserATextFileRelatedToCatastrophicError(bad.InnerException);
				else
					Logger.ShowUserATextFileRelatedToCatastrophicError(bad);
				System.Environment.FailFast("TargetInvocationException");
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
			MainContext = SynchronizationContext.Current;
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
			{
				_splashForm = SplashScreen.CreateAndShow();//warning: this does an ApplicationEvents()
				CloseFastSplashScreen();
			}
			else if (DateTime.Now > _earliestWeShouldCloseTheSplashScreen)
			{
				// BL-3192. If there is some modal in front (e.g. dropbox or screen DPI warnings), just wait. We'll keep getting called with these
				// on idle warnings until it closes, then we can proceed.
				if (_splashForm.Visible && !_splashForm.CanFocus)
				{
					return;
				}
				CloseSplashScreen();
				CheckRegistration();
				if (_projectContext != null && _projectContext.ProjectWindow != null)
				{
					var shell = _projectContext.ProjectWindow as Shell;
					if (shell != null)
					{
						shell.ReallyComeToFront();
					}
				}
			}
		}

		public static void CloseSplashScreen()
		{
			_alreadyHadSplashOnce = true;
			Application.Idle -= CareForSplashScreenAtIdleTime;

			if (_splashForm != null)
			{
				if (RegistrationDialog.ShouldWeShowRegistrationDialog())
				{
					_splashForm.Hide();//the fading was getting stuck when we showed the registration.
				}
				_splashForm.FadeAndClose(); //it's going to hang around while it fades,
				_splashForm = null; //but we are done with it
			}
		}

		private static void CheckRegistration()
		{
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
		//however, then we switched to the embedded http image server, which had a fixed
		//port. That has now been fixed, however in the meantime we switched to using a
		//differnt mutex approach (UniqueToken), so this code would need some updating.

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
				//CollectionChoosing.OpenCreateCloneControl.CheckForBeingInDropboxFolder(path);
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
				// Before we set up the project context, if the collection is shared, we need
				// to fetch any new information from the Team Collection. (This might include new
				// collection settings, so we need to sync before we load the settings file.)
				TeamRepo.MergeSharedData(projectPath);
				_projectContext = _applicationContainer.CreateProjectContext(projectPath);
				_projectContext.ProjectWindow.Closed += HandleProjectWindowClosed;
				_projectContext.ProjectWindow.Activated += HandleProjectWindowActivated;
#if DEBUG
				CheckLinuxFileAssociations();
#endif
				_projectContext.ProjectWindow.Show();

				if(_splashForm!=null)
					_splashForm.StayAboveThisWindow(_projectContext.ProjectWindow);

				if (BloomThreadCancelService != null)
					BloomThreadCancelService.Dispose();
				BloomThreadCancelService = new CancellationTokenSource();

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

			(_projectContext.ProjectWindow as Shell).CheckForInvalidBranding();
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

			if (_splashForm != null)
				CloseSplashScreen();

			while (true)
			{
				// We decided to stop doing this (BL-1229) since the wizard can feel like part
				// of installation that might be irrevocable.
				////If it looks like the 1st time, put up the create collection with the welcome.
				////The user can cancel that if they want to go looking for a collection on disk.
				//if(Settings.Default.MruProjects.Latest == null)
				//{
				//	var path = NewCollectionWizard.CreateNewCollection();
				//	if (!string.IsNullOrEmpty(path) && RobustFile.Exists(path))
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
			BloomThreadCancelService.Cancel();

			#if Chorus
			try
			{
				_projectContext.SendReceiver.CheckPointWithDialog("Storing History Of Your Work");
			}
			catch (Exception error)
			{
				SIL.Reporting.ErrorReport.NotifyUserOfProblem(error,"There was a problem backing up your work to the SendReceive repository on this computer.");
			}
			#endif

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

		static Gecko.nsILocalFileWin toNsFile(string file)
		{
			var nsfile = Xpcom.CreateInstance<nsILocalFileWin>("@mozilla.org/file/local;1");
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

		public static void SetUpLocalization(ApplicationContainer applicationContainerSource = null)
		{
			var applicationContainer = _applicationContainer;
			if (applicationContainerSource != null)
				applicationContainer = applicationContainerSource;
			var installedStringFileFolder = FileLocationUtilities.GetDirectoryDistributedWithApplication(true,"localization");
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
				applicationContainer.LocalizationManager = LocalizationManager.Create(
					TranslationMemory.XLiff, "en",
					"Bloom", "Bloom", Application.ProductVersion, fakeLocalDir, "SIL/Bloom",
					Resources.BloomIcon, "issues@bloomlibrary.org",
					//the parameters that follow are namespace beginnings:
					"Bloom");
				return;
			}

			try
			{
				// If the user has not set the interface language, try to use the system language if we can.
				// (See http://issues.bloomlibrary.org/youtrack/issue/BL-4393.)
				var desiredLanguage = GetDesiredUiLanguage(installedStringFileFolder);
				applicationContainer.LocalizationManager = LocalizationManager.Create(
					TranslationMemory.XLiff, desiredLanguage,
					"Bloom", "Bloom", Application.ProductVersion,
					installedStringFileFolder,
					"SIL/Bloom",
					Resources.BloomIcon, "issues@bloomlibrary.org",
					//the parameters that follow are namespace beginnings:
					"Bloom");

				//We had a case where someone translated stuff into another language, and sent in their tmx. But their tmx had soaked up a bunch of string
				//from their various templates, which were not Bloom standard templates. So then someone else sitting down to localize bloom would be
				//faced with a bunch of string that made no sense to them, because they don't have those templates.
				//So for now, we only soak up new strings if it's a developer, and hope that the Commit process will be enough for them to realize "oh no, I
				//don't want to check that stuff in".

#if DEBUG
				applicationContainer.LocalizationManager.CollectUpNewStringsDiscoveredDynamically = true;
#else
				applicationContainer.LocalizationManager.CollectUpNewStringsDiscoveredDynamically = false;
#endif

				var uiLanguage =   LocalizationManager.UILanguageId;//just feeding this into subsequent creates prevents asking the user twice if the language of their os isn't one we have a tmx for
				if (uiLanguage != desiredLanguage)
					Settings.Default.UserInterfaceLanguageSetExplicitly = true;

				LocalizationManager.Create(TranslationMemory.XLiff, uiLanguage,
										   "Palaso", "Palaso", /*review: this is just bloom's version*/Application.ProductVersion,
										   installedStringFileFolder,
											"SIL/Bloom",
											Resources.BloomIcon, "issues@bloomlibrary.org", "SIL");

				LocalizationManager.Create(TranslationMemory.XLiff, uiLanguage,
					"BloomMediumPriority", "BloomMediumPriority", Application.ProductVersion,
					installedStringFileFolder,
					"SIL/Bloom",
					Resources.BloomIcon, "issues@bloomlibrary.org", "Bloom");

				LocalizationManager.Create(TranslationMemory.XLiff, uiLanguage,
					"BloomLowPriority", "BloomLowPriority", Application.ProductVersion,
					installedStringFileFolder,
					"SIL/Bloom",
					Resources.BloomIcon, "issues@bloomlibrary.org", "Bloom");

				Settings.Default.UserInterfaceLanguage = LocalizationManager.UILanguageId;

				// Per BL-6449, these two languages should try Spanish before English if a localization is missing.
				// (If they ever get localized enough to show up in our list.)
				// The full names are Mam and K'iche'.
				if (LocalizationManager.UILanguageId == "mam" || LocalizationManager.UILanguageId == "quc")
				{
					LocalizationManager.FallbackLanguageIds = new[] { "es", "en" };
				}

				// If this is removed, change code in WorkspaceView.OnSettingsProtectionChanged
				LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog = false; // BL-5111
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

		/// <summary>
		/// Derive the desired UI language from the stored value, or from matching the OS value against
		/// the available localizations if nothing has been explicitly stored yet.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4393.
		/// </remarks>
		private static string GetDesiredUiLanguage(string installedStringFileFolder)
		{
			var desiredLanguage = Settings.Default.UserInterfaceLanguage;
			if (String.IsNullOrEmpty(desiredLanguage) || !Settings.Default.UserInterfaceLanguageSetExplicitly)
			{
				// Nothing has been explicitly selected by the user yet, so try to get a localization for the system language.
				// First try for an exact match, or failing that a localization with the same language.
				// (This is motivated by the Chinese localization which currently specifies zh-CN. LM might eventually
				// support multiple variants for a single language like zh-CN or zh-Hans, at which point both might be in the
				// list and we'd want the best match. In the meantime we want zh-Hans to find zh-CN. See BL-3691.)
				string localeMatchingLanguage = null;
				foreach (var subdir in Directory.EnumerateDirectories(installedStringFileFolder))
				{
					// The LocalizationManager has not been initialized yet when this method is called.
					// (Indeed, the output of this method is used to initialize the LocalizationManager!)
					// So we need to scan the disk ourselves to see what is available.  Unfortunately, the
					// subdirectory names in the given folder are not sufficient because some localizations
					// are more specific than the directory name.  For example, Spanish uses "es" for the
					// subdirectory name, but the actual language tags inside the files are "es-ES".
					// (Perhaps matching against the subdirectory name would be sufficient, but maybe
					// someday a case will arise where the country/script name is significant in some but
					// not all cases.)
					var dirTag = Path.GetFileName(subdir);
					if (IsSameActualLanguage(dirTag, UserInterfaceCulture.IetfLanguageTag))
					{
						var xliffPath = Path.Combine(subdir, "Bloom.xlf");
						if (File.Exists(xliffPath))
						{
							var doc = new XmlDocument();
							doc.Load(xliffPath);
							var fileNode = doc.DocumentElement.SelectSingleNodeHonoringDefaultNS("/xliff/file");
							if (fileNode != null)
							{
								var target = fileNode.GetOptionalStringAttribute("target-language", null);
								if (!string.IsNullOrEmpty(target))
								{
									if (target == UserInterfaceCulture.IetfLanguageTag)
										return target;
									// We don't have an exact match, but we do have a match that should work.  Remember
									// it, but hold out (fading) hope for an exact match and don't quit the loop.
									if (localeMatchingLanguage == null)
										localeMatchingLanguage = target;
								}
							}
						}
					}
				}
				// No exact match; did we find one that is at least the right language? If so use that.
				if (localeMatchingLanguage != null)
					return localeMatchingLanguage;
				// If exact matches fail, return the base language tag.  If that doesn't match in the LM.Create method,
				// then L10NSharp will prompt the user to select one of the available localizations.
				return UserInterfaceCulture.TwoLetterISOLanguageName;
			}
			return desiredLanguage;
		}

		/// <summary>
		/// Test whether two language codes refer to the same language, ignoring any country, script, or variant tagging.
		/// </summary>
		private static bool IsSameActualLanguage(string code1, string code2)
		{
			if (code1 == code2)
				return true;
			if (string.IsNullOrEmpty(code1) || string.IsNullOrEmpty(code2))
				return false;
			var codeTags1 = code1.Split('-');
			var codeTags2 = code2.Split('-');
			return (codeTags1[0] == codeTags2[0]);
		}

		private static bool _errorHandlingHasBeenSetUp;
		private static IDisposable _sentry;

		/// ------------------------------------------------------------------------------------
		internal static void SetUpErrorHandling()
		{
			if (_errorHandlingHasBeenSetUp)
				return;

			if (!ApplicationUpdateSupport.IsDev)
			{
				try
				{
					_sentry = SentrySdk.Init("https://bba22972ad6b4c2ab03a056f549cc23d@sentry.keyman.com/23");
					SentrySdk.ConfigureScope(scope =>
					{
						scope.SetExtra("channel", ApplicationUpdateSupport.ChannelName);
						scope.SetExtra("version", ErrorReport.VersionNumberString);
						scope.User = new User
						{
							Email = SIL.Windows.Forms.Registration.Registration.Default.Email,
							Username = SIL.Windows.Forms.Registration.Registration.Default.FirstName,
							//Other = {Organization:SIL.Windows.Forms.Registration.Registration.Default.Organization}
						};
						scope.User.Other.Add("Organization",
							SIL.Windows.Forms.Registration.Registration.Default.Organization);
					});
				}
				catch (Exception err)
				{
					Debug.Fail(err.Message);
				}
			}

			ErrorReport.SetErrorReporter(new NotifyUserOfProblemLogger());


			string issueTrackingUrl = UrlLookup.LookupUrl(UrlType.IssueTrackingSystem);
			ExceptionReportingDialog.PrivacyNotice = string.Format(@"If you don't care who reads your bug report, you can skip this notice.

When you submit a crash report or other issue, the contents of your email go in our issue tracking system, ""YouTrack"", which is available via the web at {0}. This is the normal way to handle issues in an open-source project.

Our issue-tracking system is searchable by anyone. Search engines (like Google) should not search it, so someone searching with Google should not see your report, but we can't promise this for all search engines.

Anyone looking specifically at our issue tracking system can read what you sent us. So if you have something private to say, please send it to one of the developers privately with a note that you don't want the issue in our issue tracking system. If need be, we'll make some kind of sanitized place-holder for your issue so that we don't lose it.
", issueTrackingUrl);
			SIL.Reporting.ErrorReport.EmailAddress = "issues@bloomlibrary.org";
			SIL.Reporting.ErrorReport.AddStandardProperties();
			// with squirrel, the file's dates only reflect when they were installed, so we override this version thing which
			// normally would include a bogus "Apparently Built On" date:
			ErrorReport.Properties["Version"] = ErrorReport.VersionNumberString + " " + ApplicationUpdateSupport.ChannelName;
			SIL.Reporting.ExceptionHandler.Init(new FatalExceptionHandler());

			ExceptionHandler.AddDelegate((w,e) => DesktopAnalytics.Analytics.ReportException(e.Exception));
			if (!ApplicationUpdateSupport.IsDev)
			{
				ExceptionHandler.AddDelegate((w, e) =>
				{
					try
					{
							SentrySdk.CaptureException(e.Exception);
					}
					catch (Exception err)
					{
						// Will only "do something" if we're testing reporting and have thus turned off checking for dev.
						// // Else we're swallowing.
						Debug.Fail(err.Message);
					}
				});
			}
			_errorHandlingHasBeenSetUp = true;
		}

		public static void OldVersionCheck()
		{
			return;
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
			var sourceDir = FileLocationUtilities.DirectoryOfApplicationOrSolution;
			foreach(var entry in filesToCheck)
			{
				var destFile = entry.Value;
				if (!RobustFile.Exists(destFile))
				{
					var sourceFile = Path.Combine(sourceDir, "debian", entry.Key);
					if (RobustFile.Exists(sourceFile))
					{
						updateNeeded = true;
						RobustFile.Copy(sourceFile, destFile);
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
			if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				// This is your count on Windows.
				return Process.GetProcesses().Count(p => IsBloomProcess(p));;
			}
			// On Linux, the process name is usually "mono-sgen" or something similar, but not all processes
			// with this name are instances of Bloom.
			var processes = Process.GetProcesses().Where(p => IsMonoProcess(p));
			// DO NOT change this foreach loop into a LINQ expression. It takes longer to complete if you do.
			var bloomProcessCount = 0;
			foreach (var p in processes)
			{
				try
				{
					if (p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName == "Bloom.exe"))
						++bloomProcessCount;
				}
				catch (System.InvalidOperationException)
				{
				}
			}
			return bloomProcessCount;
		}

		public static bool IsBloomProcess(Process process)
		{
			try
			{
				var name = process.ProcessName.ToLowerInvariant();
				// The second test prevents counting the Bloom.vshost.exe process which Visual Studio and similar tools
				// create to speed up launching the program in debug mode. It's only useful for developers.
				return name.Contains("bloom") && !name.Contains("vshost") && !name.Contains("bloomharvester");
			}
			catch (System.InvalidOperationException)
			{
				// process is already exited?
				return false;
			}
		}

		public static bool IsMonoProcess(Process process)
		{
			try
			{
				return process.ProcessName.ToLowerInvariant().StartsWith("mono");
			}
			catch (System.InvalidOperationException)
			{
				// process is already exited?
				return false;
			}
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

				if (RobustFile.Exists(ex.Filename))
				{
					Logger.WriteEvent("Config file content:\n{0}", RobustFile.ReadAllText(ex.Filename));
					Logger.WriteEvent("Deleting "+ ex.Filename);
					RobustFile.Delete(ex.Filename);
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

		public static bool RunningUnitTests {
			get
			{
				return Assembly.GetEntryAssembly() == null;
			}
		}

		// Set to true when Bloom is running one of the command line verbs, e.g. hydrate or createArtifacts
		public static bool RunningInConsoleMode { get; set; }

		// Should be set to true if this is being called by Harvester, false otherwise.
		public static bool RunningHarvesterMode { get; set; }

		/// <summary>
		/// If launched by a fast splash screen program, signal it to close.
		/// </summary>
		private static void CloseFastSplashScreen()
		{
			if (SIL.PlatformUtilities.Platform.IsLinux)
			{
				File.Delete("/tmp/BloomLaunching.now");	// (okay if file doesn't exist)
			}
			else if (SIL.PlatformUtilities.Platform.IsWindows)
			{
				// signal the native process (that launched us) to close the splash screen
				// (okay if there's nobody there to receive the signal)
				using (var closeSplashEvent = new EventWaitHandle(false,
					EventResetMode.ManualReset, "CloseSquirrelSplashScreenEvent"))
				{
					closeSplashEvent.Set();
				}
			}
		}
	}
}
