using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Api;
using SIL.IO;
using System.Diagnostics;

namespace Bloom
{
    public class HelpLauncher
    {
        public static void Show(Control parent)
        {
            // Help.ShowHelp() obeys the current environment, which includes LD_LIBRARY_PATH on Linux.
            // This can prevent Bloom Help from displaying web sites if firefox is not the default browser.
            // (External links do exist when viewing help, even though most links are internal to the help
            // file and not subject to this display glitch.)  So we must ensure that LD_LIBRARY_PATH is
            // cleared (its normal state) before invoking Help.ShowHelp(), then restored for the sake of
            // the rest of the program.  See https://silbloom.myjetbrains.com/youtrack/issue/BL-5993.
            var libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (!String.IsNullOrEmpty(libpath))
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);

            Help.ShowHelp(
                parent,
                FileLocationUtilities.GetFileDistributedWithApplication("Bloom.chm")
            );

            if (!String.IsNullOrEmpty(libpath))
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
        }

        public static void Show(Control parent, string topic)
        {
            Show(parent, "Bloom.chm", topic);
        }

        public static void Show(Control parent, string helpFileName, string topic)
        {
            // See the comments above related to BL-5993.
            var libpath = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
            if (!String.IsNullOrEmpty(libpath))
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", null);

            ShowHelpWithTopic(
                parent,
                FileLocationUtilities.GetFileDistributedWithApplication(helpFileName),
                topic
            );

            if (!String.IsNullOrEmpty(libpath))
                Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", libpath);
        }

        public static void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            apiHandler.RegisterEndpointHandler(
                "help",
                (request) =>
                {
                    var topic = request.RequiredParam("topic");
                    Show(Application.OpenForms.Cast<Form>().Last(), topic);
                    //if this is called from a simple html anchor, we don't want the browser to do anything
                    request.ExternalLinkSucceeded();
                },
                true
            ); // opening a form, definitely UI thread
        }

        private static void ShowHelpWithTopic(Control parent, string helpFile, string helpTopic)
        {
            // The Mono runtime pretends to handle MONO_HELP_VIEWER, defaulting to "chmsee", but formats the
            // argument according to chmsee's specification which doesn't work for other viewers.
            // On Windows, this variable shouldn't be set.
            string helpViewer = Environment.GetEnvironmentVariable("MONO_HELP_VIEWER");
            if (
                SIL.PlatformUtilities.Platform.IsWindows
                || String.IsNullOrEmpty(helpViewer)
                || helpViewer == "chmsee"
                || helpViewer == "/usr/bin/chmsee"
            )
            {
                Help.ShowHelp(parent, helpFile, helpTopic);
                return;
            }
            if (helpFile == null)
                throw new ArgumentNullException();
            if (helpFile == String.Empty)
                throw new ArgumentException();
            string arguments = String.Empty;
            if (helpViewer == "kchmviewer" || helpViewer == "/usr/bin/kchmviewer")
            {
                arguments = String.Format("-showPage \"{0}\" \"{1}\"", helpTopic, helpFile);
            }
            else if (helpViewer == "xchm" || helpViewer == "/usr/bin/xchm")
            {
                // According to the xchm developer, something like this should work:
                // xchm file:jdk150.chm#xchm:/jdk150/api/java/applet/package-summary.html
                arguments = String.Format("\"file:{0}#xchm:{1}\"", helpFile, helpTopic);
            }
            else
            {
                // We don't know anything about any other viewers, but assume the help file is okay.
                arguments = String.Format("\"{0}\"", helpFile);
            }
            try
            {
                // The new process should use the current culture, so we don't need to worry about that.
                // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
                using (Process process = new Process())
                {
                    process.StartInfo.FileName = helpViewer;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.UseShellExecute = false;
                    process.Start();
                }
            }
            catch (Exception e)
            { // (copied from mono code in Help.cs)
                // Don't crash if the help viewer couldn't be launched. There
                // won't be an exception thrown if the help viewer can't find
                // the help file; it's up to the help viewer to display such an error.
                string message = String.Format(
                    "The help viewer could not load. Maybe you don't have {0} installed or haven't set MONO_HELP_VIEWER. The specific error message was: {1}",
                    helpViewer,
                    e.Message
                );
                Console.Error.WriteLine(message);
                MessageBox.Show(message);
            }
        }
    }
}
