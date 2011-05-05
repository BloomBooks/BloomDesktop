using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Bloom.Properties;
using Palaso.IO;
using Palaso.Reporting;

namespace Bloom
{
	static class Program
	{

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		static extern bool SetDllDirectory(string lpPathName);

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

			string xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, "xulrunner");
			if (!Directory.Exists(xulRunnerPath))
			{
#if DEBUG
				//if this is a programmer, go look in the lib directory
				xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
											 Path.Combine("lib", "xulrunner"));
#endif
			}
			//Review: and early tester found that wrong xpcom was being loaded. The following solution is from http://www.geckofx.org/viewtopic.php?id=74&action=new
			SetDllDirectory(xulRunnerPath);

			Skybound.Gecko.Xpcom.Initialize(xulRunnerPath);

#if !DEBUG
			SetUpErrorHandling();
#endif
//            var args = Environment.GetCommandLineArgs();
//            _commandLineRequestedFirstTimeAfterInstallationBehavior = args.FirstOrDefault(x => x.ToLower().StartsWith("-i"));



			//			var args = Environment.GetCommandLineArgs();
			//			var firstTimeArg = args.FirstOrDefault(x => x.ToLower().StartsWith("-i"));
			//			if (firstTimeArg != null)
			//			{
			//				using (var dlg = new FirstTimeRunDialog("put filename here"))
			//					dlg.ShowDialog();
			//			}

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
			Palaso.Reporting.ErrorReport.AddProperty("EmailAddress", "issues@bloom.palaso.org");
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
			if(DateTime.UtcNow.Subtract(fi.CreationTimeUtc).Days > 15)
				//if (DateTime.UtcNow.Subtract(fi.CreationTimeUtc).Seconds > 100)
				{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(
					"Sorry, this experimental version of Bloom is now over 15 days old.  Please get a new version at bloom.palaso.org");
					Process.GetCurrentProcess().Kill();
			}

		}
	}


}
