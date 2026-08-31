using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Tests of the application-level/project-level split in the endpoint registrations.
    /// </summary>
    /// <remarks>
    /// This is the mechanism collection switching depends on. Every ProjectContext registers the same
    /// set of project-level endpoint patterns, and RegisterEndpointHandler throws on a duplicate key --
    /// deliberately, since a real double registration is a programming error. So the handler table has
    /// to be back to its application-level contents before the next project is built. When a project
    /// failed part way through being built and did not manage that, every later collection in that run
    /// died with "An item with the same key has already been added. Key: audio/startrecord" until Bloom
    /// was restarted. See BL-16678.
    /// </remarks>
    [TestFixture]
    public class BloomApiHandlerTests
    {
        private BloomApiHandler _handler;

        // Two of the patterns a real project registers, and one the ApplicationContainer registers
        // before any project exists.
        private const string kProjectPattern = "audio/startRecord";
        private const string kOtherProjectPattern = "collections/list";
        private const string kApplicationPattern = "common/instanceInfo";

        [SetUp]
        public void Setup()
        {
            _handler = new BloomApiHandler(new BookSelection());
        }

        private void Register(string pattern)
        {
            _handler.RegisterEndpointHandler(pattern, request => { }, false);
        }

        /// <summary>
        /// The sanity check for the tests below: registering the same pattern twice really does throw,
        /// so a test that re-registers without failing is telling us something.
        /// </summary>
        [Test]
        public void RegisterEndpointHandler_SamePatternTwice_Throws()
        {
            Register(kProjectPattern);

            Assert.That(() => Register(kProjectPattern), Throws.ArgumentException);
        }

        /// <summary>
        /// The invariant a project's teardown has to restore. See the remarks on this fixture.
        /// </summary>
        [Test]
        public void ClearProjectLevelHandlers_ThenRegisterAgain_DoesNotThrow()
        {
            Register(kApplicationPattern);
            _handler.RecordApplicationLevelHandlers();
            Register(kProjectPattern);
            Register(kOtherProjectPattern);
            // Sanity check: without the clear, this is the failure the user saw.
            Assert.That(
                () => Register(kProjectPattern),
                Throws.ArgumentException,
                "the point of the test is that re-registering is what used to fail"
            );

            _handler.ClearProjectLevelHandlers();

            Assert.That(() => Register(kProjectPattern), Throws.Nothing);
            Assert.That(() => Register(kOtherProjectPattern), Throws.Nothing);
        }

        [Test]
        public void ClearProjectLevelHandlers_LeavesApplicationLevelHandlersRegistered()
        {
            Register(kApplicationPattern);
            _handler.RecordApplicationLevelHandlers();
            Register(kProjectPattern);

            _handler.ClearProjectLevelHandlers();

            // Still there, so re-registering it is still an error. The application-level handlers are
            // registered once by the ApplicationContainer and must survive every collection switch;
            // the collection chooser itself is served by them.
            Assert.That(() => Register(kApplicationPattern), Throws.ArgumentException);
        }

        [Test]
        public void HasProjectLevelHandlers_OnlyWhenAProjectHasRegisteredSomething()
        {
            Register(kApplicationPattern);
            _handler.RecordApplicationLevelHandlers();
            Assert.That(
                _handler.HasProjectLevelHandlers,
                Is.False,
                "application-level handlers should not count as project-level ones"
            );

            Register(kProjectPattern);
            Assert.That(_handler.HasProjectLevelHandlers, Is.True);

            _handler.ClearProjectLevelHandlers();
            Assert.That(_handler.HasProjectLevelHandlers, Is.False);
        }

        /// <summary>
        /// Bloom's startup registers application-level handlers before it knows anything about a
        /// collection, so nothing should be treated as project-level until that snapshot is taken.
        /// </summary>
        [Test]
        public void ClearProjectLevelHandlers_BeforeSnapshotWasTaken_ClearsEverything()
        {
            Register(kApplicationPattern);

            _handler.ClearProjectLevelHandlers();

            Assert.That(() => Register(kApplicationPattern), Throws.Nothing);
        }
    }
}
