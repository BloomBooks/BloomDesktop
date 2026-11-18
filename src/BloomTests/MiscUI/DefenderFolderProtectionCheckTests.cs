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
