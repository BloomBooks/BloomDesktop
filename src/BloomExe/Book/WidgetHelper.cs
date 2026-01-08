using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.ToPalaso;
using Bloom.Utils;
using BloomTemp;
using SIL.IO;

namespace Bloom.Book
{
    // "Widgets" are HTML Activities that the user creates outside of Bloom, as distinct from our built-in activities.
    public class WidgetHelper
    {
        private const int kMaximumNameLength = 50;

        public static string GetShortWidgetName(string widgetName)
        {
            if (widgetName.Length > kMaximumNameLength)
                return widgetName.Substring(0, kMaximumNameLength).Trim(); // keep the name from being too long (BL-15307)

            return widgetName;
        }

        public static UrlPathString AddWidgetFilesToBookFolder(
            string bookFolderPath,
            string fullWidgetPath
        )
        {
            // Note that we are guaranteed that the current widget paths are already truncated to
            // MaximumNameLength characters because of BookStorage.MigrateToLevel9TruncateWidgetPaths().

            var widgetName = GetShortWidgetName(Path.GetFileNameWithoutExtension(fullWidgetPath));
            var newWidgetFolderName = widgetName;
            var widgetDestinationPath = $"{bookFolderPath}/activities/{newWidgetFolderName}";
            var uniqueSuffix = 1;
            // If widget destination folder already exists, come up with modified name
            while (Directory.Exists(widgetDestinationPath))
            {
                newWidgetFolderName = $"{newWidgetFolderName}{uniqueSuffix}";
                widgetDestinationPath = $"{bookFolderPath}/activities/{newWidgetFolderName}";
                uniqueSuffix++;
            }

            ZipUtils.ExpandZip(fullWidgetPath, widgetDestinationPath);
            if (uniqueSuffix > 1)
            {
                // might be duplicate widget
                var existingWidgetPath = $"{bookFolderPath}/activities/{widgetName}";
                if (DirectoryUtils.SameContent(widgetDestinationPath, existingWidgetPath))
                {
                    newWidgetFolderName = widgetName;
                    SIL.IO.RobustIO.DeleteDirectoryAndContents(widgetDestinationPath);
                    widgetDestinationPath = existingWidgetPath;
                }
            }

            var rootFileName = "index.html";
            // Warning if unzipped folder does not contain index.html
            // Enhance: possibly (following wdgt format at https://support.apple.com/en-us/HT204433),
            // zip might contain Info.plist. Parsed as an XML file, this should have a root plist
            // element containing a dict element containing a sequence of key/string pairs, including
            // one like this:
            // <key>MainHTML</key>
            // <string>HelloWorld.html</string>
            // where the string element following the MainHTML key contains the name of the root
            // HTML file.
            if (!RobustFile.Exists(Path.Combine(widgetDestinationPath, rootFileName)))
            {
                rootFileName = "index.htm";
                if (!RobustFile.Exists(Path.Combine(widgetDestinationPath, rootFileName)))
                    // Review: worth localizing?
                    MessageBox.Show(
                        "Zip file contains no index.htm{l} file. It will not work as a Bloom book widget.",
                        "Warning",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
            }

            return UrlPathString.CreateFromUnencodedString(
                $"activities/{newWidgetFolderName}/{rootFileName}"
            );
        }

        /// <summary>
        /// Create a .wdgt file from a folder that contains everything needed for a proper widget.
        /// </summary>
        /// <param name="fullWidgetPath">full path of the widget's "index.html" file, which may not actually be named "index.html"</param>
        /// <remarks>
        /// This has been tested on output from only Active Presenter and Book Widgets.
        /// </remarks>
        public static string CreateWidgetFromHtmlFolder(
            string fullWidgetPath,
            bool ensureIndexHtmlFileName = true
        )
        {
            var filename = Path.GetFileName(fullWidgetPath);
            var fullFolderName = Path.GetDirectoryName(fullWidgetPath);
            // First attempt: get the widget name from the name of the directory
            // containing the .html file.
            var widgetName = Path.GetFileName(fullFolderName);
            var fromActivePresenter = false;
            var activePresenterFiles = new string[]
            {
                "practice.html",
                "tutorial.html",
                "test.html",
            };
            if (widgetName == "HTML5")
            {
                // Active Presenter export HTML5 files to folder structure <ProjectName>/HTML5/<files>
                // where the files include possibly practice.html, tutorial.html, and/or test.html.
                // The user presumably picked one of these three files.  Get the widget name from
                // the directory name from the level above the HTML5 subfolder.
                widgetName = Path.GetFileName(Path.GetDirectoryName(fullFolderName));
                fromActivePresenter = activePresenterFiles.Contains(filename);
            }
            if (String.IsNullOrWhiteSpace(widgetName))
            {
                // The .html file must be at the top level of the filesystem??
                widgetName = "MYWIDGET"; // I doubt this ever happens, but it saves a compiler warning.
            }
            else if (widgetName.EndsWith(".wdgt"))
            {
                // Book Widgets creates a folder named <ProjectName>.wdgt in which to store the
                // widget files.  Why a folder and not a zip file, only the programmers/analysts
                // know...  So strip the extension off the folder name to get the widget name.
                widgetName = Path.GetFileNameWithoutExtension(widgetName);
            }
            // Ampersands cause problems for BloomPubReader, so replace them.  (BL-10045)
            if (widgetName.Contains("&"))
                widgetName = widgetName.Replace("&", "_");
            // Trim widgetName to a reasonable length.  (BL-15307)
            widgetName = GetShortWidgetName(widgetName);
            var widgetPath = Path.Combine(Path.GetTempPath(), "Bloom", widgetName + ".wdgt");
            Directory.CreateDirectory(Path.GetDirectoryName(widgetPath));
            using (TemporaryFolder temp = new TemporaryFolder("CreatingWidgetForBloom"))
            {
                try
                {
                    // Copy the relevant files and folders to a temporary location.
                    DirectoryUtils.CopyFolder(fullFolderName, temp.FolderPath);
                }
                catch (Bloom.Utils.PathTooLongException ex)
                {
                    // Tell the user that the widget creation failed due to the long source path. (BL-15421)
                    LongPathAware.ReportLongPath(ex);
                    return "";
                }
                // Remove excess html files (if any), and ensure desired html file is named "index.html".
                foreach (var filePath in Directory.GetFiles(temp.FolderPath))
                {
                    var name = Path.GetFileName(filePath);
                    if (name == filename)
                    {
                        if (ensureIndexHtmlFileName && filename != "index.html")
                        {
                            var destPath = Path.Combine(temp.FolderPath, "index.html");
                            if (RobustFile.Exists(destPath))
                                RobustFile.Delete(destPath);
                            RobustFile.Move(filePath, destPath);
                        }
                    }
                    else if (fromActivePresenter && (activePresenterFiles.Contains(name)))
                    {
                        RobustFile.Delete(filePath);
                    }
                }
                // zip up the temporary folder contents into a widget file
                var zip = new BloomZipFile(widgetPath);
                foreach (var file in Directory.GetFiles(temp.FolderPath))
                {
                    zip.AddTopLevelFile(file);
                }
                foreach (var dir in Directory.GetDirectories(temp.FolderPath))
                    zip.AddDirectory(dir);
                zip.Save();
            }
            return widgetPath;
        }
    }
}
