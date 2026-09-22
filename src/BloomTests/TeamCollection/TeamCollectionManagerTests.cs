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
