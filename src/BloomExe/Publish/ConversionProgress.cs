using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Gecko;
using Palaso.IO;
using Palaso.Reporting;

namespace GeckofxHtmlToPdf
{
	/// <summary>
	/// This class is used when the exe is called from the command line. It is invisibe
	/// if the --quiet parameter is used, otherwise it gives a little progress dialog.
	/// </summary>
	public partial class ConversionProgress : Form
	{
		private readonly ConversionOrder _conversionOrder;

		public ConversionProgress(ConversionOrder conversionOrder)
		{
			_conversionOrder = conversionOrder;
			InitializeComponent();
			_progressBar.Maximum = 100;
			if (conversionOrder.NoUIMode)
			{
				this.WindowState = FormWindowState.Minimized;
				this.ShowInTaskbar = false;
			}

            var xulRunnerPath = GetXulRunnerPath();
			_pdfMaker.Initialize(xulRunnerPath);
		}

        // Todo: common code in Browser.cs
	    string GetXulRunnerPath()
	    {
            string xulRunnerPath = Environment.GetEnvironmentVariable("XULRUNNER");
            if (!Directory.Exists(xulRunnerPath))
            {
                xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, "xulrunner");
                if (!Directory.Exists(xulRunnerPath))
                {
                    //if this is a programmer, go look in the lib directory
                    xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
                        Path.Combine("lib", "xulrunner"));

                    //on my build machine, I really like to have the dir labelled with the version.
                    //but it's a hassle to update all the other parts (installer, build machine) with this number,
                    //so we only use it if we don't find the unnumbered alternative.
                    if (!Directory.Exists(xulRunnerPath))
                    {
                        xulRunnerPath = Path.Combine(FileLocator.DirectoryOfApplicationOrSolution,
                            Path.Combine("lib", "xulrunner" + XulRunnerVersion));
                    }

                    if (!Directory.Exists(xulRunnerPath))
                    {
                        throw new ConfigurationException(
                            "Can't find the directory where xulrunner (version {0}) is installed",
                            XulRunnerVersion);
                    }
                }
            }
	        return xulRunnerPath;
	    }

        private static int XulRunnerVersion
        {
            get
            {
                var geckofx = Assembly.GetAssembly(typeof(GeckoWebBrowser));
                if (geckofx == null)
                    return 0;

                var versionAttribute = geckofx.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)
                    .FirstOrDefault() as AssemblyFileVersionAttribute;
                return versionAttribute == null ? 0 : new Version(versionAttribute.Version).Major;
            }
        }

		private void ConversionProgress_Load(object sender, EventArgs e)
		{
			_pdfMaker.Start(_conversionOrder);
		}

	    public void Cancel()
	    {
	        _pdfMaker.Cancel();
	        Close();
	    }

	    public event EventHandler<EventArgs> Finished;

		void OnPdfMaker_Finished(object sender, EventArgs e)
		{
			_statusLabel.Text = "Finished";
			
			//on windows 7 (at least) you won't see 100% if you close before the system has had a chance to "animate" the increase. 
			//On very short documents, you won't see it get past around 20%. Now good. So, the
			//trick here is to go *down* to 99, that going downwards makes it skip the animation delay.
			_progressBar.Value = 100;
			_progressBar.Value = 99;		
			Close();
		    if (Finished != null)
		        Finished(this, new EventArgs());
		}

		private void OnPdfMaker_StatusChanged(object sender, PdfMakingStatus pdfMakingStatus)
		{
			_statusLabel.Text = pdfMakingStatus.statusLabel;
			_progressBar.Value = pdfMakingStatus.percentage;
		}

		#region FindingXulRunner

		/// <summary>
		/// Find a file which, on a development machine, lives in [solution]/DistFiles/[subPath],
		/// and when installed, lives in 
		/// [applicationFolder]/[subPath1]/[subPathN]
		/// </summary>
		/// <example>GetFileDistributedWithApplication("info", "releaseNotes.htm");</example>
		public static string GetDirectoryDistributedWithApplication(bool optional, params string[] partsOfTheSubPath)
		{
			var path = DirectoryOfApplicationOrSolution;
			foreach (var part in partsOfTheSubPath)
			{
				path = System.IO.Path.Combine(path, part);
			}
			if (Directory.Exists(path))
				return path;

			//try distfiles
			path = DirectoryOfApplicationOrSolution;
			path = Path.Combine(path, "distFiles");
			foreach (var part in partsOfTheSubPath)
			{
				path = System.IO.Path.Combine(path, part);
			}
			if (Directory.Exists(path))
				return path;

			//try src (e.g. Bloom keeps its javascript under source directory (and in distfiles only when installed)
			path = DirectoryOfApplicationOrSolution;
			path = Path.Combine(path, "src");
			foreach (var part in partsOfTheSubPath)
			{
				path = System.IO.Path.Combine(path, part);
			}

			if (optional && !Directory.Exists(path))
				return null;

			if (!Directory.Exists(path))
				throw new ApplicationException("Could not locate " + path);
			return path;
		}

		/// <summary>
		/// Gives the directory of either the project folder (if running from visual studio), or
		/// the installation folder.  Helpful for finding templates and things; by using this,
		/// you don't have to copy those files into the build directory during development.
		/// It assumes your build directory has "output" as part of its path.
		/// </summary>
		/// <returns></returns>
		public static string DirectoryOfApplicationOrSolution
		{
			get
			{
				string path = DirectoryOfTheApplicationExecutable;
				char sep = Path.DirectorySeparatorChar;
				int i = path.ToLower().LastIndexOf(sep + "output" + sep);

				if (i > -1)
				{
					path = path.Substring(0, i + 1);
				}
				return path;
			}
		}

		public static string DirectoryOfTheApplicationExecutable
		{
			get
			{
				string path;
				bool unitTesting = Assembly.GetEntryAssembly() == null;
				if (unitTesting)
				{
					path = new Uri(Assembly.GetExecutingAssembly().CodeBase).AbsolutePath;
					path = Uri.UnescapeDataString(path);
				}
				else
				{
					var assembly = Assembly.GetEntryAssembly();
					path = assembly.Location;
				}
				return Directory.GetParent(path).FullName;
			}
		}
		#endregion
	}
}
