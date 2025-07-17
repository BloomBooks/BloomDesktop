using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using Bloom.Api;
using Bloom.ToPalaso;
using Bloom.web;
using Cairo;
using L10NSharp;
using SIL.CommandLineProcessing;
using SIL.IO;
using SIL.Progress;
using Path = System.IO.Path;
using TempFile = SIL.IO.TempFile;

namespace Bloom.Publish.PDF
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
    public class ProcessPdfWithGhostscript
    {
        public string _inputPdfPath;
        public string _outputPdfPath;

        private readonly BackgroundWorker _worker;

        public enum OutputType
        {
            DesktopPrinting, // shrink images to 600dpi in compressed format
            Printshop // shrink images to 300dpi, convert color to CMYK (-dPDFSETTINGS=/prepress + other options)
        };

        private readonly OutputType _type;
        private readonly string _colorProfile; // for CMYK conversion
        private DoWorkEventArgs _doWorkEventArgs;

        public ProcessPdfWithGhostscript(
            OutputType type,
            string colorProfile,
            BackgroundWorker worker,
            DoWorkEventArgs doWorkEventArgs
        )
        {
            _type = type;
            _colorProfile = colorProfile;
            _worker = worker;
            _doWorkEventArgs = doWorkEventArgs;
        }

        /// <summary>
        /// Process the input PDF file by compressing images and/or by converting color to CMYK.  The operations
        /// to perform are established by the constructor.
        /// </summary>
        public void ProcessPdfFile(string inputFile, string outputFile)
        {
            _inputPdfPath = inputFile;
            _outputPdfPath = outputFile;
            var exePath = "/usr/bin/gs";
            if (SIL.PlatformUtilities.Platform.IsWindows)
                exePath = FindGhostcriptOnWindows();

            var compressing = LocalizationManager.GetString(
                "PublishTab.PdfMaker.Compress",
                "Compressing PDF"
            );
            _socketProgress = new WebSocketProgress(BloomWebSocketServer.Instance, "progress");
            _socketProgress.MessageWithoutLocalizing(compressing);

            if (!String.IsNullOrWhiteSpace(exePath) && RobustFile.Exists(exePath))
            {
                if (_worker != null)
                    _worker.ReportProgress(0, GetSpecificStatus());
                using (var tempPdfFile = TempFile.WithExtension(".pdf"))
                {
                    _runner = new SIL.CommandLineProcessing.CommandLineRunner();
                    var arguments = GetArguments(tempPdfFile.Path, null);
                    var fromDirectory = String.Empty;
                    var progress = new NullProgress(); // I can't figure out how to use any IProgress based code, but we show progress okay as is.
                    var res = _runner.StartWithInvariantCulture(
                        exePath,
                        arguments,
                        Encoding.UTF8,
                        fromDirectory,
                        3600,
                        progress,
                        ProcessGhostcriptReporting
                    );
                    if (res.ExitCode != 0)
                    {
                        // On Linux, ghostscript doesn't deal well with some Unicode filenames.  Try renaming the input
                        // file temporarily to something innocuous to see if this makes the ghostscript process succeed.
                        // See https://issues.bloomlibrary.org/youtrack/issue/BL-7177.
                        using (var tempInputFile = TempFile.WithExtension(".pdf"))
                        {
                            RobustFile.Delete(tempInputFile.Path); // Move won't replace even empty files.
                            RobustFile.Move(_inputPdfPath, tempInputFile.Path);
                            arguments = GetArguments(tempPdfFile.Path, tempInputFile.Path);
                            res = _runner.StartWithInvariantCulture(
                                exePath,
                                arguments,
                                Encoding.UTF8,
                                fromDirectory,
                                3600,
                                progress,
                                ProcessGhostcriptReporting
                            );
                            RobustFile.Move(tempInputFile.Path, _inputPdfPath);
                        }
                    }
                    if (res.ExitCode != 0 || res.DidTimeOut || !RobustFile.Exists(tempPdfFile.Path))
                    {
                        if (_inputPdfPath != _outputPdfPath)
                            RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
                        return;
                    }
                    // If the process made the file larger and didn't change the color scheme, ignore the result.
                    var oldInfo = new FileInfo(_inputPdfPath);
                    var newInfo = new FileInfo(tempPdfFile.Path);
                    if (newInfo.Length < oldInfo.Length || _type == OutputType.Printshop)
                        RobustFile.Copy(tempPdfFile.Path, _outputPdfPath, true);
                    else if (_inputPdfPath != _outputPdfPath)
                        RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
                }
            }
            else
            {
                // This shouldn't happen.  Linux Bloom package depends on the ghostscript package, and we'll include
                // ghostscript files in our installer to ensure it's available on Windows.  But we have this code here
                // as a failsafe fallback reminding the developers to ensure this installation work happens.
                Debug.WriteLine(
                    "ghostscript is not installed, so Bloom cannot process the PDF file."
                );
                if (_inputPdfPath != _outputPdfPath)
                    RobustFile.Copy(_inputPdfPath, _outputPdfPath, true);
            }
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
            // Look first for the barebones version distributed with Bloom 4.0 (and later presumably).
            // Don't give up if you can't find it.
            var basedir = FileLocationUtilities.DirectoryOfApplicationOrSolution;
            var dir = Path.Combine(basedir, "ghostscript");
            if (!Directory.Exists(dir))
                dir = Path.Combine(basedir, "DistFiles", "ghostscript");
            if (Directory.Exists(dir))
            {
                var filename = Path.Combine(dir, "gswin32c.exe");
                if (RobustFile.Exists(filename))
                    return filename;
            }
            var baseName = "gswin32";
            if (Environment.Is64BitOperatingSystem)
                baseName = "gswin64";
            // Look for the four most common installation locations.
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "gs"
            );
            // GetFolderPath returns the wrong value on x64 system for x86 programs.
            // See https://stackoverflow.com/questions/23304823/environment-specialfolder-programfiles-returns-the-wrong-directory.
            baseDir = baseDir.Replace(" (x86)", "");
            if (!Directory.Exists(baseDir))
            {
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "gs"
                );
                baseName = "gswin32";
            }
            if (!Directory.Exists(baseDir))
                return null;
            // gs9.18 works on Linux.  gs9.16 fails on Windows.  See BL-5295.
            // We know gs9.21 works on Windows.
            const float kMinVersion = 9.21F;
            foreach (var versionDir in Directory.GetDirectories(baseDir))
            {
                var gsversion = Path.GetFileName(versionDir);
                if (gsversion != null && gsversion.StartsWith("gs") && gsversion.Length > 2)
                {
                    gsversion = gsversion.Substring(2);
                    float version;
                    if (
                        float.TryParse(
                            gsversion,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out version
                        ) || float.TryParse(gsversion, out version)
                    ) // In case it does get stored on the system in a culture-specific way.
                    {
                        if (version < kMinVersion)
                            continue;
                        var prog = Path.Combine(versionDir, "bin", baseName + "c.exe");
                        if (RobustFile.Exists(prog))
                            return prog;
                        prog = Path.Combine(versionDir, "bin", baseName + ".exe");
                        if (RobustFile.Exists(prog))
                            return prog;
                    }
                }
            }
            return null;
        }

        private string GetArguments(string tempFile, string inputFile)
        {
            var bldr = new StringBuilder();
            // CompatibilityLevel=1.4 - Acrobat 5.0
            // CompatibilityLevel=1.5 - Acrobat 6.0
            // CompatibilityLevel=1.6 - Acrobat 7.0
            // CompatibilityLevel=1.7 - Acrobat 8.0/9.0 (~2008)
            bldr.Append("-sDEVICE=pdfwrite -dBATCH -dNOPAUSE -dCompatibilityLevel=1.7"); // REVIEW: is this the right compatibility level?
            switch (_type)
            {
                case OutputType.DesktopPrinting:
                    // See http://issues.bloomlibrary.org/youtrack/issue/BL-3721 for the need for this operation.
                    // See also https://stackoverflow.com/questions/9497120/how-to-downsample-images-within-pdf-file and
                    // https://superuser.com/questions/435410/where-are-ghostscript-options-switches-documented.  We are trying
                    // for "press quality" (or better), but leaving the color as RGB.
                    bldr.Append(" -dColorImageResolution=600");
                    bldr.Append(" -dGrayImageResolution=600");
                    bldr.Append(" -dMonoImageResolution=1200");
                    break;
                case OutputType.Printshop:
                    Debug.Assert(!string.IsNullOrEmpty(_colorProfile), "color profile must be set for printshop");
                    // This reduces images to 300dpi, converting the color to CMYK.
                    bldr.Append(" -dPDFSETTINGS=/prepress");
                    bldr.Append(" -sColorConversionStrategy=CMYK");
                    bldr.Append(" -sColorConversionStrategyForImages=CMYK");
                    bldr.Append(" -sProcessColorModel=DeviceCMYK");
                    bldr.Append(" -dOverrideICC=true");
                    var rgbProfile = FileLocationUtilities.GetFileDistributedWithApplication(
                        "ColorProfiles/RGB/AdobeRGB1998.icc"
                    );
                    var distFolder = PdfMaker.GetDistributedColorProfilesFolder();
                    var cmykProfile = Path.Combine(distFolder, _colorProfile + ".icc");
                    if (!RobustFile.Exists(cmykProfile))
                    {
                        var userFolder = PdfMaker.GetUserColorProfilesFolder();
                        cmykProfile = Path.Combine(userFolder, _colorProfile + ".icc");
                        if (!RobustFile.Exists(cmykProfile))
                        {
                            SIL.Reporting.Logger.WriteEvent(
                                "Could not find CMYK color profile {0} in {1} or {2}, using default USWebCoatedSWOP.icc",
                                _colorProfile,
                                distFolder,
                                userFolder
                            );
                            // fall back to the default CMYK profile.
                            cmykProfile = Path.Combine(PdfMaker.GetDistributedColorProfilesFolder(), "USWebCoatedSWOP.icc");
                        }
                    }
                    bldr.AppendFormat(" -sDefaultRGBProfile=\"{0}\"", rgbProfile);
                    bldr.AppendFormat(" -sDefaultCMYKProfile=\"{0}\"", cmykProfile);
                    bldr.AppendFormat(" -sOutputICCProfile=\"{0}\"", cmykProfile);
                    break;
            }
            bldr.Append(" -dDownsampleColorImages=true -dColorImageDownsampleThreshold=1.0");
            bldr.Append(" -dDownsampleGrayImages=true -dGrayImageDownsampleThreshold=1.0");
            bldr.Append(" -dDownsampleMonoImages=true -dMonoImageDownsampleThreshold=1.0");
            if (SIL.PlatformUtilities.Platform.IsWindows)
            {
                // Ghostscript uses JPEG compression by default on all images when compressing a PDF file.
                // The value in imageCompressDict provides the highest quality image using JPEG compression: best picture, least compression.
                // See https://files.lfpp.csa.canon.com/media/Assets/PDFs/TSS/external/DPS400/Distillerpdfguide_v1_m56577569830529783.pdf#G5.1030935.
                // The default setting here can result in visibly mottled areas of what should be solid colors.
                // (See https://issues.bloomlibrary.org/youtrack/issue/BL-8928.)  Increasing the quality to the maximum
                // does not totally eliminate this mottling effect, but makes it much less noticeable.
                //
                // These arguments cause gs 9.50 on Linux to fail.  The gs 9.21 we're using on Windows doesn't fail.
                // Quoting from https://www.ghostscript.com/doc/current/VectorDevices.htm#note_13: (also in /doc/9.26/VectorDevices.htm)
                // "The arrays AlwaysEmbed and NeverEmbed and image parameter dictionaries ColorACSImageDict, ColorACSImageDict, ColorImageDict,
                // GrayACSImageDict, GrayImageDict, MonoImageDict cannot be specified on the command line. To specify these, you must use PostScript,
                // either by including it in the PostScript source or by passing the -c command-line parameter to ghostscript as described in
                // Limitations below."
                // e.g., -c "<< /GrayACSImageDict <</QFactor 0.5 /HSamples [1 1 1 1] /VSamples [1 1 1 1]>> >> setdistillerparams"
                // Note that the -c parameter must follow all other parameters, including the input file.
                var imageCompressDict =
                    "/QFactor 0.1 /Blend 1 /HSamples [1 1 1 1] /VSamples [1 1 1 1]";
                bldr.AppendFormat(" -sColorACSImageDict=\"{0}\"", imageCompressDict);
                bldr.AppendFormat(" -sColorImageDict=\"{0}\"", imageCompressDict);
                bldr.AppendFormat(" -sGrayACSImageDict=\"{0}\"", imageCompressDict);
                bldr.AppendFormat(" -sGrayImageDict=\"{0}\"", imageCompressDict);
            }

            if (String.IsNullOrEmpty(inputFile))
                inputFile = _inputPdfPath;
            bldr.AppendFormat(
                $" -sOutputFile=\"{tempFile}\" \"{DoubleBracesInInputPath(inputFile)}\""
            );
            if (SIL.PlatformUtilities.Platform.IsLinux)
            {
                // See detailed explanation of this above in the IsWindows block.
                var imageCompressDict =
                    "<< /QFactor 0.1 /Blend 1 /HSamples [1 1 1 1] /VSamples [1 1 1 1] >>";
                bldr.Append(" -c \"<<");
                bldr.AppendFormat(" /ColorACSImageDict {0}", imageCompressDict);
                bldr.AppendFormat(" /ColorImageDict {0}", imageCompressDict);
                bldr.AppendFormat(" /GrayACSImageDict {0}", imageCompressDict);
                bldr.AppendFormat(" /GrayImageDict {0}", imageCompressDict);
                bldr.Append(" >> setdistillerparams\"");
            }
            return bldr.ToString();
        }

        private string DoubleBracesInInputPath(string inputPath)
        {
            // prevents BL-5940
            return inputPath.Replace("{", "{{").Replace("}", "}}");
        }

        private string GetSpecificStatus()
        {
            switch (_type)
            {
                case OutputType.DesktopPrinting:
                    return L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.PdfMaker.Compress",
                        "Compressing PDF",
                        @"Message displayed in a progress report dialog box"
                    );
                case OutputType.Printshop:
                    return L10NSharp.LocalizationManager.GetString(
                        @"PublishTab.PdfMaker.CompressConvertColor",
                        "Compressing PDF & Converting Color to CMYK",
                        @"Message displayed in a progress report dialog box"
                    );
            }
            return String.Empty;
        }

        int _firstPage;
        int _numPages;
        private SIL.CommandLineProcessing.CommandLineRunner _runner;
        private WebSocketProgress _socketProgress;

        // Magic strings to match for progress reporting.  ghostscript itself doesn't seem to be localized
        // (maybe because it's a command line program?)
        const string kProcessingPages = "Processing pages ";
        const string kThroughWithSpaces = " through ";
        const string kPage = "Page ";

        // What share of the total progress time should be allocated to compression
        // as we move the progress bar?
        // Experimentally, treating it as 75% of the total time to make the PDF
        // (with the other 25% being spent creating the initial PDF from the HTML)
        // seems to give a roughly uniform rate of progress, but we may want to
        // tweak it after experimenting on more typical user computers.
        public const int kPdfCompressionShare = 75;

        /// <summary>
        /// Use the stdout progress reporting to move the GUI progress report along.  This does assume that
        /// ghostscript doesn't have any localization since it's parsing English strings to extract information.
        /// </summary>
        private void ProcessGhostcriptReporting(string line)
        {
            if (_worker == null)
                return; // nothing here will have any effect anyway
            if (_worker.CancellationPending)
            {
                if (_doWorkEventArgs != null)
                    _doWorkEventArgs.Cancel = true;
                try
                {
                    _runner.Abort(1);
                }
                catch (InvalidOperationException)
                {
                    // Typically means the process already stopped.
                    // Possibly a race condition between aborting the process and getting another
                    // line of output from it.
                }

                return;
            }
            //Debug.WriteLine(String.Format("DEBUG gs report line = \"{0}\"", line));
            if (line.StartsWith(kProcessingPages) && line.Contains(kThroughWithSpaces))
            {
                _firstPage = 0;
                int lastPage = 0;
                _numPages = 0;
                // Get the first and last page numbers processed and the total number of pages.
                var idxNumber = kProcessingPages.Length;
                var idxMid = line.IndexOf(kThroughWithSpaces);
                if (
                    idxMid > idxNumber
                    && !Int32.TryParse(
                        line.Substring(idxNumber, idxMid - idxNumber),
                        out _firstPage
                    )
                )
                    _firstPage = 0;
                idxNumber = idxMid + kThroughWithSpaces.Length;
                var idxPeriod = line.IndexOf(".", idxNumber);
                if (
                    idxPeriod > idxNumber
                    && !Int32.TryParse(
                        line.Substring(idxNumber, idxPeriod - idxNumber),
                        out lastPage
                    )
                )
                    lastPage = 0;
                if (_firstPage > 0 && lastPage > 0)
                    _numPages = lastPage - _firstPage + 1;
            }
            else if (line.StartsWith(kPage) && _numPages > 0)
            {
                // Get the current page number and adjust the progress dialog appropriately.
                int pageNumber = 0;
                if (Int32.TryParse(line.Substring(kPage.Length), out pageNumber) && _numPages > 0)
                {
                    try
                    {
                        var percentage = (int)(
                            100.0F * (float)(pageNumber - _firstPage) / (float)_numPages
                        );
                        // For old views...goes to C# progress window (remove when they go away)
                        _worker.ReportProgress(percentage);
                        // For new view...goes to Javascript code
                        _socketProgress.SendPercent(
                            percentage * kPdfCompressionShare / 100 + (100 - kPdfCompressionShare)
                        );
                    }
                    catch (ObjectDisposedException e)
                    {
                        Bloom.Utils.MiscUtils.SuppressUnusedExceptionVarWarning(e);

                        // Don't worry about not being able to update while/after the progress dialog is closing/has closed.
                    }
                }
            }
        }
    }
}
