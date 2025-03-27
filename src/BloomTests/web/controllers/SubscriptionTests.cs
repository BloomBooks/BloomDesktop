using System;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using Bloom.SubscriptionAndFeatures;

namespace BloomTests.Collection
{
    [TestFixture]
    public class SubscriptionTests
    {
        [Test]
        public void Constructor_StoresCodeCorrectly()
        {
            var subscription = new Subscription("Test-Code");
            Assert.AreEqual("Test-Code", subscription.Code);
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("Sample-361769-1209", "Sample")]
        [TestCase("Foo", "Foo")]
        [TestCase("Foo-Bar", "Foo-Bar")]
        [TestCase("Foo-Bar-Blah", "Foo-Bar-Blah")]
        [TestCase("Test-Expired-Code-005658-9576", "Test-Expired-Code")]
        [TestCase("การทดสอบ-LC-005908-3073", "การทดสอบ-LC")]
        [TestCase("Fake[Western]-006273-6382", "Fake[Western]")]
        public void Descriptor_ReturnsCorrectValue(string code, string expectedDescriptor)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedDescriptor, subscription.Descriptor);
        }

        [TestCase(null, "Default")]
        [TestCase("", "Default")]
        [TestCase("Sample-361769-1709", "Sample")]
        [TestCase("Foo-Bar-Blah", "Default")] // missing parts, invalid, thus branding is "Default"
        [TestCase("Fake-LC-006273-1463", "Local-Community")] // this code will eventually expire, after which it should be replaced
        [TestCase("Test-Expired-005691-4935", "Default")] //  expired, thus "Default"
        [TestCase("Test-Invalid-111-1111", "Default")] //  invalid, thus "Default"
        [TestCase("Foobar-***-***", "Default")] //  invalid, thus "Default". To use a redacted code, you have to use a factory method
        [TestCase("Fake[Western]-006273-6382", "Fake[Western]")]
        public void BrandingKey_ReturnsCorrectValue(string code, string expectedBranding)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedBranding, subscription.BrandingKey);
        }

        [TestCase("", "", false, "")]
        [TestCase(null, null, false, null)]
        [TestCase(null, "Local Community", false, "Legacy-LC-005839-2533")] // migrate legacy branding only to a modern subscription code
        [TestCase(null, "Local-Community", false, "Legacy-LC-005839-2533")] // migrate legacy branding only to a modern subscription code
        [TestCase(
            "Test-Expiring-Soon-005779-1460",
            "Local-Community",
            false,
            "Test-Expiring-Soon-005779-1460"
        )] // ignore branding if we have a code
        [TestCase("foobar-***-***", "Default", false, "foobar-***-***")] // if the subscription and the branding are both provided and different, use the subscription
        [TestCase("foobar-***-***", "Default", true, "foobar-***-***")] // if the subscription and the branding are both provided and different, use the subscription
        [TestCase("Local-Community-***-***", "Default", true, "Legacy-LC-005839-2533")] // migrate legacy subscription to a modern subscription code for editing from Blorg
        public void FromSettingsXml_ReturnsCorrectValue(
            string code,
            string branding,
            bool editingABlorgBook,
            string expectedCode
        )
        {
            var subscription = Subscription.FromCollectionSettingsInfo(
                code,
                branding,
                editingABlorgBook
            );
            Assert.AreEqual(expectedCode, subscription.Code);
        }

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("incomplete", true)]
        [TestCase("UnitTest-E-006046-3301", false)] // when this test starts failing, give it a newer code
        [TestCase("Test-Expired-Code-005658-9576", true)] // Using the test code that should be valid
        public void IsExpired_ReturnsCorrectValue(string code, bool expectedResult)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedResult, subscription.IsExpired());
        }

        [TestCase(null, SubscriptionTier.Basic)]
        [TestCase("", SubscriptionTier.Basic)]
        [TestCase("Legacy-LC-005809-2533", SubscriptionTier.LocalCommunity)]
        [TestCase("Fake-006273-0501", SubscriptionTier.Enterprise)]
        [TestCase("Fake-LC-006273-1463", SubscriptionTier.LocalCommunity)]
        [TestCase("Test-Expired-005691-4935", SubscriptionTier.Basic)] // if expired, it's basic
        public void Tier_ReturnsCorrectEnum(string code, SubscriptionTier expectedTier)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedTier, subscription.Tier);
        }

        [TestCase("", "none")]
        [TestCase(null, "none")]
        [TestCase("NameOnly", "incomplete")]
        [TestCase("Name-Only", "incomplete")]
        [TestCase("Project-123456", "incomplete")]
        [TestCase("Acme3506487", "incomplete")] // no dashes
        [TestCase("Acme-3506487", "incomplete")] // too few dashes
        [TestCase("Acme-003506-", "incomplete")] // No digits in part 3 is OK, even though part 3 won't parse
        [TestCase("Project-Name-12345", "incomplete")]
        [TestCase("Project-Name-123456", "incomplete")]
        [TestCase("Project-Name-123456-", "incomplete")]
        [TestCase("Project-Name-123456-1", "incomplete")]
        [TestCase("Project-Name-123456-123", "incomplete")]
        [TestCase("Acme-3506-487-nonsense", "invalid")] // clearly wrong here
        [TestCase("Acme-silly-1234", "incomplete")] // not a number in part 2... but could be start of Acme-silly-123456-7890
        [TestCase("Acme-003506-487", "incomplete")] // Too few digits in part 3
        [TestCase("Somevery long fake thing-361769-19523", "invalid")] // Too many digits in part 3
        [TestCase("Project-12345-11", "invalid")] // Too few digits in date part
        [TestCase("Acme-3506-487", "invalid")] // Last two parts are numbers, but first is too short
        [TestCase("Acme-003506-0488", "invalid")] // wrong checksum
        [TestCase("Acme-7484-silly", "invalid")] // not a number in part 3...debatable, COULD be start of Acme-7484-silly-123456-7890
        [TestCase("Acme-7484-si", "invalid")] // short not a number in part 3...debatable, COULD be start of Acme-7484-si-123456-7890
        [TestCase("Quite-Phony-3098-4247", "invalid")] // Too few digits in part 2
        [TestCase("Tttest-Expired-Code-005658-9576", "invalid")]
        [TestCase("UnitTest-E-006046-3301", "ok")] // Using the test code that should be valid
        [TestCase("345-Test-002460-2109", "ok")]
        [TestCase("Test-789-006112-1755", "ok")]
        public void GetIntegrityLabel_ReturnsCorrectValue(string code, string expectedLabel)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedLabel, subscription.GetIntegrityLabel());
        }

        [TestCase(null, null, false)]
        [TestCase(null, "", false)]
        [TestCase("", null, false)]
        [TestCase("a", "a", false)]
        [TestCase("a", "A", true)]
        public void IsDifferent_DetectsChanges(string oldCode, string newCode, bool expectedResult)
        {
            var subscription = new Subscription(oldCode);
            Assert.AreEqual(expectedResult, subscription.IsDifferent(newCode));
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("Test-Expired-005691-4935", false)]
        [TestCase("Legacy-LC-005839-2533", true)]
        [TestCase("Fake-006273-0501", true)]
        [TestCase("Fake-LC-006273-1463", true)]
        public void HaveActiveSubscription_ReturnsCorrectValue(string code, bool hasSubscription)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(hasSubscription, subscription.HaveActiveSubscription);
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("Fake-Thing-LC-006273-5397", "Fake Thing")] // dashes are replaced by spaces
        [TestCase("Fake-006273-0501", "")] // this is a full-on enterprise subscription, so no personalization
        public void Personalization_ReturnsCorrectValue(string code, string personalization)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(subscription.Personalization, personalization);
        }

        [TestCase("", "0001-01-01")]
        [TestCase("Legacy-LC-005839-2533", "2025-07-01")] //Subscription.kDefaultExpirationDate
        [TestCase("Test-Expired-Code-005658-9576", "2025-01-01")]
        [TestCase("i-am-invalid", "0001-01-01")]
        public void GetExpirationDate_CalculatesCorrectly(string code, string expectedYYYYmmDD)
        {
            var subscription = new Subscription(code);
            var expirationDate = subscription.ExpirationDate;
            Assert.AreEqual(expectedYYYYmmDD, expirationDate.ToString("yyyy-MM-dd"));
        }

        [TestCase("Acme-003506-0487", 2019, 2, 10)]
        [TestCase("Quite-Phony-003098-4247", 2017, 12, 29)]
        [TestCase("SOME-FAKE-361769-3038", 2025, 7, 1)] // this number must correspond to kExpiryDateForDeprecatedBrandings
        [TestCase("Somevery long fake thing-361769-9523", 2025, 7, 1)] // this number must correspond to kExpiryDateForDeprecatedBrandings
        [TestCase("Local-Community", 2025, 7, 1)] // this number must correspond to kExpiryDateForDeprecatedBrandings
        public void GetExpirationDate_Valid_ReturnsCorrectDate(
            string input,
            int year,
            int month,
            int day
        )
        {
            void RunTestWithCulture(string input, int year, int month, int day, CultureInfo culture)
            {
                Thread.CurrentThread.CurrentCulture = culture;

                var subscription = new Subscription(input);
                var result = subscription.ExpirationDate;

                Assert.That(result.Year, Is.EqualTo(year));
                Assert.That(result.Month, Is.EqualTo(month));
                Assert.That(result.Day, Is.EqualTo(day));
            }

            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                RunTestWithCulture(input, year, month, day, originalCulture);
                RunTestWithCulture(input, year, month, day, new CultureInfo("th-TH"));
                RunTestWithCulture(input, year, month, day, new CultureInfo("en-US"));
                RunTestWithCulture(input, year, month, day, CultureInfo.InvariantCulture);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        [TestCase("")] // empty
        [TestCase(null)]
        [TestCase("Acme3506487")] // no dashes
        [TestCase("Acme-3506487")] // too few dashes
        [TestCase("Acme-3506-487-nonsense")] // extra at end
        [TestCase("Acme-3506-488")] // wrong checksum
        [TestCase("Acme-silly-1234")] // not a number in part 2
        [TestCase("Acme-7484-silly")] // not a number in part 3
        [TestCase("Quite-Phony-3098-4247")] // Too few digits in part 2
        [TestCase("Acme-003506-487")] // Too few digits in part 3
        [TestCase("Somevery long fake thing-361769-19523")] // Too many digits in part 3
        public void GetExpirationDate_InValid_ReturnsMinDate(string input)
        {
            var subscription = new Subscription(input);
            var result = subscription.ExpirationDate;

            Assert.That(result, Is.EqualTo(DateTime.MinValue));
        }
    }
}
