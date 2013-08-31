using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection.BloomPack;
using Bloom.CollectionCreating;
using Bloom.Properties;
using Bloom.Registration;
using DesktopAnalytics;
using L10NSharp;
using Palaso.IO;
using Palaso.Reporting;
using System.Linq;

namespace Bloom
{
	static class Program
	{

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
#endif

		[STAThread]
		[HandleProcessCorruptedStateExceptions]
		static void Main(string[] args)
		{
			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);

				//bring in settings from any previous version
				if (Settings.Default.NeedUpgrade)
				{
					//see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
					Settings.Default.Upgrade();
					Settings.Default.NeedUpgrade = false;
					Settings.Default.Save();
					StartUpWithFirstOrNewVersionBehavior = true;
				}

#if DEBUG
				using (new Analytics("sje2fq26wnnk8c2kzflf", RegistrationDialog.GetAnalyticsUserInfo(), true))

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


#if !DEBUG //the exception you get when there is no other BLOOM is a pain when running debugger with break-on-exceptions
				if (!GrabMutexForBloom())
					return;
#endif

					OldVersionCheck();



					SetUpErrorHandling();

					_applicationContainer = new ApplicationContainer();

					SetUpLocalization();
					Logger.Init();



					if (args.Length == 1 && args[0].ToLower().EndsWith(".bloomcollection"))
					{
						Settings.Default.MruProjects.AddNewPath(args[0]);
					}
					_earliestWeShouldCloseTheSplashScreen = DateTime.Now.AddSeconds(3);

					Settings.Default.Registration.IncrementLaunchCount();
					Settings.Default.Save();

					Browser.SetUpXulRunner();

					Application.Idle += Startup;



					L10NSharp.LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);

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
			}
			finally
			{
				ReleaseMutexForBloom();
			}
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
				CloseSplashScreen();
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

		private static void CloseSplashScreen()
		{
			if (_splashForm != null)
			{
				_splashForm.FadeAndClose(); //it's going to hang around while it fades,
				_splashForm = null; //but we are done with it
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

			string mutexId = "bloom";
			bool mutexAcquired = false;
			try
			{
				_onlyOneBloomMutex = Mutex.OpenExisting(mutexId);
				mutexAcquired = _onlyOneBloomMutex.WaitOne(TimeSpan.FromMilliseconds(1 * 1000), false);
			}
			catch (WaitHandleCannotBeOpenedException e)//doesn't exist, we're the first.
			{
				_onlyOneBloomMutex = new Mutex(true, mutexId, out mutexAcquired);
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
					_onlyOneBloomMutex = Mutex.OpenExisting(mutexId);
					mutexAcquired = _onlyOneBloomMutex.WaitOne(TimeSpan.FromMilliseconds(10 * 1000), false);
				}
				catch (AbandonedMutexException e)
				{
					_onlyOneBloomMutex = new Mutex(true, mutexId, out mutexAcquired);
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

			CheckAndWarnAboutVirtualStore();

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

		//The windows "VirtualStore" is teh source of some really hard to figure out behavior:
		//The symptom is, getting different results in the installed version, *unless you change the name of the Bloom folder in Program Files*.
		//Then look at C:\Users\User\AppData\Local\VirtualStore\Program Files (x86)\Bloom and you'll find some old files.
		private static void CheckAndWarnAboutVirtualStore()
		{
			var programFilesName = Path.GetFileName(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
			var virtualStore = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
								 "VirtualStore");
			var ourVirtualStore = Path.Combine(virtualStore, programFilesName);
			ourVirtualStore = Path.Combine(ourVirtualStore, "Bloom");

			if (Directory.Exists(ourVirtualStore))
			{
#if DEBUG
				Debug.Fail("You have a shadow copy of some Bloom files at " + ourVirtualStore + " that has crept in via running the installed version. Find what caused it and stamp it out!");
#endif
				try
				{
					Directory.Delete(ourVirtualStore, true);
				}
				catch (Exception error)
				{
					ErrorReport.NotifyUserOfProblem("Bloom could not remove the Virtual Store shadow of Bloom at " + ourVirtualStore +
													". This can cause some stylesheets to fall out of date.");
					Analytics.ReportException(error);
				}
			}
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
			else if (((Shell)sender).QuitForVersionUpdate)
			{
				Application.Exit();
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
										   Path.Combine(ProjectContext.GetBloomAppDataFolder(), "Localizations"), Resources.Bloom, "issues@bloom.palaso.org", "Bloom");

				var uiLanguage =   LocalizationManager.UILanguageId;//just feeding this into subsequent creates prevents asking the user twice if the language of their os isn't one we have a tmx for
				var unusedGoesIntoStatic = LocalizationManager.Create(uiLanguage,
										   "Palaso", "Palaso", /*review: this is just bloom's version*/Application.ProductVersion,
										   installedStringFileFolder,
										   Path.Combine(ProjectContext.GetBloomAppDataFolder(), "Localizations"), Resources.Bloom, "issues@bloom.palaso.org", "Palaso.UI");

  /*                var l10nSystem = L10NSystem.BeginInit(preferredLanguage, installedStringFileFolder, targetStringFileFolder, icon, "issues@bloom.palaso.org");
					l10nSystem.AddLocalizationPackage(NameSpace="Bloom", ID="Bloom", DisplayName="Bloom", Version=Application.ProductVersion);
					l10nSystem.AddLocalizationPackage(NameSpace = "Palaso", ID = "Palaso", DisplayName = "Palaso", Version = Application.ProductVersion);
				or better
						[Localizable(NameSpace="Bloom", ID="Bloom", DisplayName="Bloom", Version=Application.ProductVersion)]
				l10nSystem.EndInit();
	*/

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
			Palaso.Reporting.ErrorReport.EmailAddress = "issues@bloom.palaso.org";
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
						if (Dns.GetHostAddresses("bloom.palaso.org").Length > 0)
						{
							if (DialogResult.Yes == MessageBox.Show("This beta version of Bloom is now over 90 days old. Click 'Yes' to have Bloom open the web page where you can get a new one.", "OLD BETA", MessageBoxButtons.YesNo))
							{
								Process.Start("http://bloom.palaso.org/download");
								Process.GetCurrentProcess().Kill();
							}
							return;
						}
					}
					catch (Exception)
					{
					}

					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
						"This beta version of Bloom is now over 90 days old. If possible, please get a new version at bloom.palaso.org.");
			}

		}
	}


}
