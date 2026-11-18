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
        private const string kAvastCause =
            "Avast's Ransomware Shield may be blocking Bloom. You can allow Bloom in Avast under Protection > Ransomware Shield.";

        /// <summary>
        /// Access denied with no suspect found is an ordinary permission or read-only problem.
        /// </summary>
        [Test]
        public void IsPermissionProblem_AccessDeniedNoSuspect_True()
        {
            Assert.That(
                DefenderFolderProtectionCheck.IsPermissionProblem(
                    new UnauthorizedAccessException("Access to the path is denied."),
                    null
                ),
                Is.True
            );
        }

        /// <summary>
        /// Avast's Ransomware Shield refuses with access denied too (BL-17056); when the diagnostics
        /// suspect it, telling the user to ask for permission would send them the wrong way.
        /// </summary>
        [Test]
        public void IsPermissionProblem_AccessDeniedWithAntivirusSuspect_False()
        {
            Assert.That(
                DefenderFolderProtectionCheck.IsPermissionProblem(
                    new UnauthorizedAccessException("Access to the path is denied."),
                    kAvastCause
                ),
                Is.False
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
                    null
                ),
                Is.False
            );
        }
    }
}
