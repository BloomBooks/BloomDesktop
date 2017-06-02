using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Bloom.ToPalaso;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using SIL.PlatformUtilities;
using System.Text;
using SIL.Reporting;

namespace Bloom.Publish
{
	/// <summary>
	/// The Geckofx/Mozilla code used to create PDF files can create truly huge files in the hundreds of megabytes
	/// if not bigger.  ghostscript can reduce the size of these files by 90-95%.
	/// The Geckofx/Mozilla code also produces RGB color by default (which is what cameras produce).  Some printers
	/// accept only CMYK color.  ghostscript can also convert from RGB to CMYK.
	/// This class wraps invoking the ghostscript program to do these tasks.
	/// </summary>
	/// <remarks>
	/// See http://issues.bloomlibrary.org/youtrack/issue/BL-3721 and http://issues.bloomlibrary.org/youtrack/issue/BL-4457.
	/// </remarks>
	public class FixPdfUsingGhostscript
	{
		public string _inputPdfPath;
		public string _outputPdfPath;

		private bool _shrink;
		private string _rgbProfile;
		private string _cmykProfile;

		private SIL.Windows.Forms.Progress.ProgressDialog _progress;

		public FixPdfUsingGhostscript(bool shrink, string rgbProfile, string cmykProfile)
		{
			_shrink = shrink;
			_rgbProfile = rgbProfile;
			_cmykProfile = cmykProfile;
		}

		public void CreateFixedPdfFile(string inputFile, string outputFile)
		{
			_inputPdfPath = inputFile;
			_outputPdfPath = outputFile;
			if (!_shrink && String.IsNullOrWhiteSpace(_rgbProfile) && String.IsNullOrWhiteSpace(_cmykProfile))
			{
				if (_inputPdfPath != _outputPdfPath)
					RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
				return;
			}

			using (_progress = new SIL.Windows.Forms.Progress.ProgressDialog())
			{
				if (_shrink)
				{
					_progress.Overview = L10NSharp.LocalizationManager.GetString(@"PublishTab.FixPDF.Shrink",
						"Compressing the PDF file.",
						@"Message displayed in a progress report dialog box.");
				}
				if (!String.IsNullOrWhiteSpace(_rgbProfile) && !String.IsNullOrWhiteSpace(_cmykProfile))
				{
					_progress.StatusText = L10NSharp.LocalizationManager.GetString(@"PublishTab.FixPDF.Color",
						"Converting color to CMYK for printing.",
						@"Message displayed in a progress report dialog box.");
				}
				_progress.BackgroundWorker = new BackgroundWorker();
				_progress.BackgroundWorker.DoWork += DoFixPdfWork;
				_progress.CanCancel = false;
				_progress.ShowDialog();
				if (_progress.ProgressStateResult != null && _progress.ProgressStateResult.ExceptionThatWasEncountered != null)
				{
					Console.WriteLine("Exception encountered fixing PDF - {0}", _progress.ProgressStateResult.ExceptionThatWasEncountered);
				}
			}
		}

		private void DoFixPdfWork(object sender, DoWorkEventArgs args)
		{
			var exePath = "/usr/bin/gs";
			if (SIL.PlatformUtilities.Platform.IsWindows)
				exePath = FindGhostcriptOnWindows();
			if (!String.IsNullOrEmpty(exePath) && File.Exists(exePath))
			{
				var tempPdfFile = GetTemporaryPdfFilename();
				var runner = new CommandLineRunner();
				var arguments = SetArguments(tempPdfFile);
				var fromDirectory = String.Empty;
				var progress = new NullProgress();	// I can't figure out how to use any IProgress based code, but we show progress okay as is.
				var res = runner.Start(exePath, arguments, Encoding.UTF8, fromDirectory, 3600, progress, ProcessGhostcriptReporting);
				if (res.DidTimeOut || !RobustFile.Exists(tempPdfFile))
				{
					// REVIEW: Should we log this failure?
					if (_inputPdfPath != _outputPdfPath)
						RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
					return;
				}
				// If the process actually made the file larger, ignore the result.
				var oldInfo = new FileInfo(_inputPdfPath);
				var newInfo = new FileInfo(tempPdfFile);
				if (newInfo.Length < oldInfo.Length)
					RobustFile.Copy(tempPdfFile, _outputPdfPath, true);
				else if (_inputPdfPath != _outputPdfPath)
					RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
				File.Delete(tempPdfFile);
			}
			else
			{
				// REVIEW: ask user to download and install ghostscript?  how often?  only once?  once per run of Bloom and saving PDF?
				Debug.WriteLine("ghostscript is not installed, so Bloom cannot process the PDF file.");
				if (_inputPdfPath != _outputPdfPath)
					RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
			}
		}

		private string GetTemporaryPdfFilename()
		{
			var newTempFile = Path.GetTempFileName();
			File.Delete(newTempFile);
			return newTempFile + ".pdf";
		}


		/// <summary>
		/// Try to find ghostscript on the Windows system, looking in the likeliest installation
		/// locations and trying to not depend on the exact version installed.
		/// </summary>
		/// <returns>path to the ghostscript program, or null if not found</returns>
		/// <remarks>
		/// Ghostscript 9.21 for 64-bit Windows 10 installs by default as
		/// C:\Program Files\gs\gs9.21\bin\gswin64c.exe or C:\Program Files\gs\gs9.21\bin\gswin64.exe.
		/// The former uses the current console window, the latter pops up its own command window.
		/// </remarks>
		private string FindGhostcriptOnWindows()
		{
			// TODO: if we decide to distribute GS with Bloom, enhance this to look first wherever we
			// install GS in the user area.
			var baseName = "gswin32";
			if (Environment.Is64BitOperatingSystem)
				baseName = "gswin64";
			// Look for the four most common installation locations.
			var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "gs");
			// GetFolderPath returns the wrong value on x64 system for x86 programs.
			// See https://stackoverflow.com/questions/23304823/environment-specialfolder-programfiles-returns-the-wrong-directory.
			baseDir = baseDir.Replace(" (x86)", "");
			if (!Directory.Exists(baseDir))
			{
				baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "gs");
				baseName = "gswin32";
			}
			if (!Directory.Exists(baseDir))
				return null;
			foreach (var versionDir in Directory.GetDirectories(baseDir))
			{
				var prog = Path.Combine(versionDir, "bin", baseName + "c.exe");
				if (File.Exists(prog))
					return prog;
				prog = Path.Combine(versionDir, "bin", baseName + ".exe");
				if (File.Exists(prog))
					return prog;
			}
			return null;
		}

		private string SetArguments(string tempFile)
		{
			var bldr = new StringBuilder();
			bldr.Append("-sDEVICE=pdfwrite -dBATCH -dNOPAUSE -dCompatibilityLevel=1.7");	// REVIEW: is this the right compatibility level?
			if (_shrink)
			{
				// See http://issues.bloomlibrary.org/youtrack/issue/BL-3721 for the need for this operation.
				// See also https://stackoverflow.com/questions/9497120/how-to-downsample-images-within-pdf-file and
				// https://superuser.com/questions/435410/where-are-ghostscript-options-switches-documented.  We are trying
				// for "press quality" (or better).
				bldr.Append(" -dDownsampleColorImages=true -dColorImageResolution=600 -dColorImageDownsampleThreshold=1.0");
				bldr.Append(" -dDownsampleGrayImages=true -dGrayImageResolution=600 -dGrayImageDownsampleThreshold=1.0");
				bldr.Append(" -dDownsampleMonoImages=true -dColorMonoResolution=1200 -dMonoImageDownsampleThreshold=1.0");
			}
			if (!String.IsNullOrEmpty(_rgbProfile) && !String.IsNullOrEmpty(_cmykProfile))
			{
				// TODO: handle _rgbProfile and _cmykProfile  (BL-4457)
			}
			bldr.AppendFormat(" -sOutputFile=\"{0}\" \"{1}\"", tempFile, _inputPdfPath);
			return bldr.ToString();
		}

		int _firstPage;
		int _numPages;

		// Magic strings to match for initializing progress reporting.
		const string ProcessingPages = "Processing pages ";
		const string ThroughWithSpaces = " through ";

		/// <summary>
		/// Use the stdout progress reporting to move the GUI progress report along.  This does assume that
		/// ghostscript doesn't have any localization since it's parsing English strings to extract information.
		/// </summary>
		private void ProcessGhostcriptReporting(string line)
		{
			Debug.WriteLine(String.Format("DEBUG gs report line = \"{0}\"", line));
			if (line.StartsWith(ProcessingPages) && line.Contains(ThroughWithSpaces))
			{
				_firstPage = 0;
				int lastPage = 0;
				_numPages = 0;
				// Get the first and last page numbers processed and the total number of pages.
				var idxNumber = ProcessingPages.Length;
				var idxMid = line.IndexOf(ThroughWithSpaces);
				if (idxMid > idxNumber && !Int32.TryParse(line.Substring(idxNumber, idxMid - idxNumber), out _firstPage))
					_firstPage = 0;
				idxNumber = idxMid + ThroughWithSpaces.Length;
				var idxPeriod = line.IndexOf(".", idxNumber);
				if (idxPeriod > idxNumber && !Int32.TryParse(line.Substring(idxNumber, idxPeriod - idxNumber), out lastPage))
					lastPage = 0;
				if (_firstPage > 0 && lastPage > 0)
					_numPages = lastPage - _firstPage + 1;
			}
			else if (line.StartsWith("Page "))
			{
				// Get the current page number and adjust the progress dialog appropriately.
				int pageNumber = 0;
				if (Int32.TryParse(line.Substring(5), out pageNumber) && _numPages > 0)
				{
					try
					{
						var percentage = (int)(100.0F * (float)(pageNumber - _firstPage) / (float)_numPages);
						if (_progress.InvokeRequired)
						{
							_progress.Invoke(new ReportFixProgress(ReportProgress), new object[] { percentage });
							return;
						}
						ReportProgress(percentage);
					}
					catch (ObjectDisposedException e)
					{
						// Don't worry about not being able to update while/after the progress dialog is closing/has closed.
					}
				}
			}
		}

		private delegate void ReportFixProgress(int percentage);

		void ReportProgress(int percentage)
		{
			if (_progress.IsDisposed)
				return;
			_progress.Progress = percentage;
		}
	}
}
