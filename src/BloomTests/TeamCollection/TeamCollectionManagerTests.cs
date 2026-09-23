using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    public class TeamCollectionManagerTests
    {
        /// <summary>
        /// When we disconnect part way through a session, CurrentCollection becomes null and the
        /// DisconnectedTeamCollection standing in for it carries a message log of its own -- the
        /// one holding the "you are now disconnected" messages. Since an ordinary append gives up
        /// quickly on a busy log file and leaves the message to be written later, shutting down
        /// has to flush that log too, or those messages are lost. See BL-16729.
        /// </summary>
        [Test]
        public void Dispose_DisconnectedMidSession_FlushesTheDisconnectedCollectionsLog()
        {
            using (var collectionFolder = new TemporaryFolder("FlushDisconnectedLog_Collection"))
            using (var sharedFolder = new TemporaryFolder("FlushDisconnectedLog_Shared"))
            {
                var settingsPath = Path.Combine(
                    collectionFolder.FolderPath,
                    Path.GetFileName(collectionFolder.FolderPath) + ".bloomCollection"
                );
                RobustFile.WriteAllText(settingsPath, "<Collection version=\"0.2\"/>");
                FolderTeamCollection.CreateTeamCollectionLinkFile(
                    collectionFolder.FolderPath,
                    sharedFolder.FolderPath
                );
                var tcManager = new TeamCollectionManager(
                    settingsPath,
                    null,
                    new BookStatusChangeEvent(),
                    null,
                    null,
                    null
                );
                Assert.That(
                    tcManager.CurrentCollection,
                    Is.Not.Null,
                    "setup problem: it should have connected, or there is nothing to disconnect"
                );

                var logPath = TeamCollectionManager.GetTcLogPathFromLcPath(
                    collectionFolder.FolderPath
                );
                // Disconnect while nothing can write to the log file, so the disconnect messages
                // have to wait in the message log instead of reaching the file.
                using (
                    new FileStream(logPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None)
                )
                {
                    tcManager.MakeDisconnected(
                        new TeamCollectionMessage(
                            MessageAndMilestoneType.Error,
                            "TeamCollection.NoNetwork",
                            "No network is available on this computer."
                        ),
                        "test repo"
                    );
                }
                Assert.That(
                    tcManager.CurrentCollection,
                    Is.Null,
                    "setup problem: it should now be disconnected"
                );
                Assert.That(
                    tcManager.CurrentCollectionEvenIfDisconnected.MessageLog.Messages.Any(m =>
                        m.L10NId == "TeamCollection.OperatingDisconnected"
                    ),
                    Is.True,
                    "setup problem: the disconnect messages should be in memory"
                );
                Assert.That(
                    RobustFile.ReadAllText(logPath),
                    Does.Not.Contain("OperatingDisconnected"),
                    "setup problem: the busy file should not have received them yet"
                );

                // sut
                tcManager.Dispose();

                Assert.That(
                    RobustFile.ReadAllText(logPath),
                    Does.Contain("OperatingDisconnected"),
                    "disposing the manager should have flushed the disconnected collection's log"
                );
            }
        }

        /// <summary>
        /// A collection whose name ends with a period gets a folder without it, because Windows drops
        /// trailing periods when it creates a folder, so its settings file name does not match its
        /// folder name. OkToEditCollectionSettings is asked during the startup sync of a Team
        /// Collection, before anyone has given us a CollectionSettings, and it used to look only for a
        /// settings file named after the folder: it threw, which aborted the sync, so no books were
        /// copied in. See BL-16679.
        /// </summary>
        [Test]
        public void OkToEditCollectionSettings_SettingsFileNameDoesNotMatchFolderName_Succeeds()
        {
            using (
                var collectionFolder = new TemporaryFolder("TeamCollectionManagerTests_Collection")
            )
            using (var sharedFolder = new TemporaryFolder("TeamCollectionManagerTests_Shared"))
            {
                // The doubled period is what a collection named "Some Collection." really looks like
                // on disk: the folder lost the period, the settings file kept it.
                var settingsPath = Path.Combine(
                    collectionFolder.FolderPath,
                    Path.GetFileName(collectionFolder.FolderPath) + "..bloomCollection"
                );
                RobustFile.WriteAllText(settingsPath, "<Collection version=\"0.2\"/>");
                // Sanity check the premise: nothing has the name our code used to assume.
                Assert.That(
                    RobustFile.Exists(
                        Path.Combine(
                            collectionFolder.FolderPath,
                            Path.GetFileName(collectionFolder.FolderPath) + ".bloomCollection"
                        )
                    ),
                    Is.False,
                    "the whole point is that the folder-derived name does not exist"
                );
                // Makes this look like a Team Collection, so that the restriction applies at all.
                FolderTeamCollection.CreateTeamCollectionLinkFile(
                    collectionFolder.FolderPath,
                    sharedFolder.FolderPath
                );

                var tcManager = new TeamCollectionManager(
                    settingsPath,
                    null,
                    new BookStatusChangeEvent(),
                    null,
                    null,
                    null
                );
                Assert.That(
                    tcManager.CurrentCollectionEvenIfDisconnected,
                    Is.Not.Null,
                    "should have recognized this as a Team Collection, or the test proves nothing"
                );
                Assert.That(
                    tcManager.Settings,
                    Is.Null,
                    "the case we are testing is the one where nobody has given us settings yet"
                );

                // sut: this used to throw, because it looked for a settings file named after the folder.
                Assert.That(tcManager.OkToEditCollectionSettings, Is.True);
            }
        }
    }
}
