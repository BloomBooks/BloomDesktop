using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bloom.Utils;
using NUnit.Framework;

namespace BloomTests.Utils
{
    /// <summary>
    /// Switches this test process's input method (never the whole session's) between a plain
    /// keyboard layout and an installed TSF input processor, and checks what the detector says.
    /// </summary>
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class InputProcessorDetectorTests
    {
        [Test]
        public void IsInputProcessorActive_FollowsTheActiveInputMethod()
        {
            var profileMgr = InputProcessorDetector.CreateProfileManager();
            Assert.That(profileMgr, Is.Not.Null, "TSF should provide a profile manager on Windows");
            var category = InputProcessorDetector.TfcatTipKeyboard;
            var original = profileMgr.GetActiveProfile(ref category);
            var profiles = GetKeyboardProfiles(profileMgr);
            var layout = profiles.FirstOrDefault(p =>
                p.ProfileType == TfProfileType.KeyboardLayout
            );
            var inputProcessor = profiles.FirstOrDefault(p =>
                p.ProfileType == TfProfileType.InputProcessor
            );
            Assert.That(
                layout.ProfileType,
                Is.EqualTo(TfProfileType.KeyboardLayout),
                "test setup: every Windows machine has a keyboard layout"
            );
            if (inputProcessor.ProfileType != TfProfileType.InputProcessor)
                Assert.Ignore(
                    "No TSF input processor (Keyman, an IME) is installed on this machine."
                );

            try
            {
                Activate(profileMgr, layout);
                Assert.That(InputProcessorDetector.IsInputProcessorActive(), Is.False);

                Activate(profileMgr, inputProcessor);
                Assert.That(InputProcessorDetector.IsInputProcessorActive(), Is.True);
            }
            finally
            {
                Activate(profileMgr, original);
            }
            Assert.That(
                profileMgr.GetActiveProfile(ref category).GuidProfile,
                Is.EqualTo(original.GuidProfile),
                "the test should leave the original input method active"
            );
        }

        private static List<TfInputProcessorProfile> GetKeyboardProfiles(
            ITfInputProcessorProfileMgr profileMgr
        )
        {
            var result = new List<TfInputProcessorProfile>();
            var enumerator = profileMgr.EnumProfiles(0); // 0 = every language
            var buffer = new TfInputProcessorProfile[1];
            while (enumerator.Next(1, buffer) == 1)
            {
                if (
                    buffer[0].ProfileType == TfProfileType.KeyboardLayout
                    || buffer[0].CatId == InputProcessorDetector.TfcatTipKeyboard
                )
                    result.Add(buffer[0]);
            }
            return result;
        }

        private static void Activate(
            ITfInputProcessorProfileMgr profileMgr,
            TfInputProcessorProfile profile
        )
        {
            var clsid = profile.ClsId;
            var guidProfile = profile.GuidProfile;
            profileMgr.ActivateProfile(
                profile.ProfileType,
                profile.LangId,
                ref clsid,
                ref guidProfile,
                profile.Hkl,
                TfIppMf.ForProcess
            );
        }
    }
}
