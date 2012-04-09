using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Bloom.Properties;
using Palaso.Reporting;

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


		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			TimeBomb();

			//bring in settings from any previous version
			if (Settings.Default.NeedUpgrade)
			{
				//see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
				Settings.Default.Upgrade();
				Settings.Default.NeedUpgrade = false;
				Settings.Default.Save();
				StartUpWithFirstOrNewVersionBehavior = true;
			}

			SetUpErrorHandling();
			Logger.Init();
			Splasher.Show();
			SetUpReporting();
			Settings.Default.Save();

			_applicationContainer = new ApplicationContainer();

			Browser.SetUpXulRunner();

#if !DEBUG
			SetUpErrorHandling();
#endif

			StartUpShellBasedOnMostRecentUsedIfPossible();
			Application.Idle += new EventHandler(Application_Idle);
			Application.Run();

			Settings.Default.Save();

			Logger.ShutDown();

			if (_projectContext != null)
				_projectContext.Dispose();
		}



		private static void Application_Idle(object sender, EventArgs e)
		{
			Application.Idle -= new EventHandler(Application_Idle);
			Splasher.Close();
		}

		/// ------------------------------------------------------------------------------------
		private static void StartUpShellBasedOnMostRecentUsedIfPossible()
		{
			if (Settings.Default.MruProjects.Latest == null  ||
				!OpenProjectWindow(Settings.Default.MruProjects.Latest))
			{
				//since the message pump hasn't started yet, show the UI for choosing when it is
				Application.Idle += ChooseAnotherProject;
			}
		}

		/// ------------------------------------------------------------------------------------
		private static bool OpenProjectWindow(string projectPath)
		{
			Debug.Assert(_projectContext == null);

			try
			{
				_projectContext = _applicationContainer.CreateProjectContext(projectPath);
				_projectContext.ProjectWindow.Closed += HandleProjectWindowClosed;
				_projectContext.ProjectWindow.Activated += HandleProjectWindowActivated;

				_projectContext.ProjectWindow.Show();

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
				using (var dlg = _applicationContainer.CreateWelcomeDialog())
				{
					if (dlg.ShowDialog() != DialogResult.OK)
					{
						Application.Exit();
						return;
					}

					if (OpenProjectWindow(dlg.SelectedPath))
					{
						Settings.Default.MruProjects.AddNewPath(dlg.SelectedPath);
						Settings.Default.Save();
						return;
					}
				}
			}
		}

		/// ------------------------------------------------------------------------------------
		static void HandleProjectWindowClosed(object sender, EventArgs e)
		{
			_projectContext.Dispose();
			_projectContext = null;

			if (((Shell)sender).UserWantsToOpenADifferentProject)
			{
				Application.Idle += ChooseAnotherProject;
			}
			else
			{
				Application.Exit();
			}
		}

		/// ------------------------------------------------------------------------------------
		private static void SetUpErrorHandling()
		{
			Palaso.Reporting.ErrorReport.EmailAddress = "issues@bloom.palaso.org";
			Palaso.Reporting.ErrorReport.AddStandardProperties();
			Palaso.Reporting.ExceptionHandler.Init();
		}


		private static void SetUpReporting()
		{
			if (Settings.Default.Reporting == null)
			{
				Settings.Default.Reporting = new ReportingSettings();
				Settings.Default.Save();
			}
			UsageReporter.Init(Settings.Default.Reporting, "bloom.palaso.org", "UA-22170471-2",
#if DEBUG
				true
#else
				false
#endif
				);
		}

		public static void TimeBomb()
		{
			var asm = Assembly.GetExecutingAssembly();
			var file = asm.CodeBase.Replace("file:", string.Empty);
			file = file.TrimStart('/');
			var fi = new FileInfo(file);
			if(DateTime.UtcNow.Subtract(fi.LastWriteTimeUtc).Days > 30)// nb: "create time" is stuck at may 2011, for some reason. Arrrggghhhh
				//if (DateTime.UtcNow.Subtract(fi.CreationTimeUtc).Seconds > 100)
				{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					"Sorry, this developmental version of Bloom is now over 30 days old.  Please get a new version at bloom.palaso.org");
					Process.GetCurrentProcess().Kill();
			}

		}
	}


}
