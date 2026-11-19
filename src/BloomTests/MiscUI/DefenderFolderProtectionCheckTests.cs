using System;
using System.IO;
using Bloom.MiscUI;
using NUnit.Framework;

namespace BloomTests.MiscUI
{
    /// <summary>
    /// How DefenderFolderProtectionCheck decides which explanation to give when Bloom cannot write
    /// in a collection's folder. (BL-16728)
    /// </summary>
    [TestFixture]
    public class DefenderFolderProtectionCheckTests
    {
        /// <summary>
        /// Access denied with no antivirus suspected is an ordinary permission or read-only
        /// problem, even when something else, such as a cloud sync program, is a likely cause.
        /// </summary>
        [Test]
        public void IsPermissionProblem_AccessDeniedNoAntivirusSuspected_True()
        {
            Assert.That(
                DefenderFolderProtectionCheck.IsPermissionProblem(
                    new UnauthorizedAccessException("Access to the path is denied."),
                    false
                ),
                Is.True
            );
        }

        /// <summary>
        /// Avast's Ransomware Shield refuses with access denied too (BL-17056); when the diagnostics
        /// suspect antivirus, telling the user to ask for permission would send them the wrong way.
        /// </summary>
        [Test]
        public void IsPermissionProblem_AccessDeniedWithAntivirusSuspected_False()
        {
            Assert.That(
                DefenderFolderProtectionCheck.IsPermissionProblem(
                    new UnauthorizedAccessException("Access to the path is denied."),
                    true
                ),
                Is.False
            );
        }

        /// <summary>
        /// A permission problem names the folder (escaped, since it goes into HTML), says it may be
        /// a permission or read-only problem, and links to the file permissions help page.
        /// </summary>
        [Test]
        public void MakeMessageHtml_PermissionProblem_NamesFolderAndLinksToPermissionsHelp()
        {
            var folder = @"C:\Stories & Songs";
            var html = DefenderFolderProtectionCheck.MakeMessageHtml(folder, null, true, null);

            Assert.That(html, Does.Contain(@"C:\Stories &amp; Songs"));
            Assert.That(html, Does.Not.Contain("Songs & "), "the folder should be HTML-escaped");
            Assert.That(html, Does.Contain("You may not have permission"));
            Assert.That(html, Does.Not.Contain("Antivirus"));
            Assert.That(
                html,
                Does.Contain("href='https://docs.bloomlibrary.org/windows-file-permissions'")
            );
        }

        /// <summary>
        /// When antivirus is suspected and the collection opened but some operations were refused,
        /// the message says so, blames antivirus, links to the troubleshooting page and adds the
        /// likely cause the diagnostics found.
        /// </summary>
        [Test]
        public void MakeMessageHtml_PartlyBlockedByAntivirus_HasEveryPart()
        {
            var extra = "Bloom opened the collection, but books may fail to save.";
            var likelyCause = "Avast's Ransomware Shield may be blocking Bloom.";
            var html = DefenderFolderProtectionCheck.MakeMessageHtml(
                @"C:\Stories",
                extra,
                false,
                likelyCause
            );

            Assert.That(html, Does.Contain(@"C:\Stories"));
            Assert.That(html, Does.Contain(extra));
            Assert.That(html, Does.Contain("Antivirus software"));
            Assert.That(html, Does.Not.Contain("You may not have permission"));
            Assert.That(
                html,
                Does.Contain("href='https://docs.bloomlibrary.org/troubleshooting-file-access'")
            );
            Assert.That(html, Does.Contain("Avast&#39;s Ransomware Shield may be blocking Bloom."));
            Assert.That(
                html.IndexOf(extra, System.StringComparison.Ordinal),
                Is.LessThan(html.IndexOf("Antivirus software", System.StringComparison.Ordinal)),
                "the partly-blocked paragraph should come before the cause"
            );
        }

        /// <summary>
        /// Controlled Folder Access refuses a new file with FileNotFoundException; that is never
        /// a permission problem.
        /// </summary>
        [Test]
        public void IsPermissionProblem_FileNotFound_False()
        {
            Assert.That(
                DefenderFolderProtectionCheck.IsPermissionProblem(
                    new FileNotFoundException("Could not find file."),
                    false
                ),
                Is.False
            );
        }
    }
}
