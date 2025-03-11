using System;
using NUnit.Framework;

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

        [TestCase(null, "Default")]
        [TestCase("", "Default")]
        [TestCase("Sample-361769-1209", "Sample")]
        [TestCase("Incomplete-Code", "Default")]
        [TestCase("Test-Expired-Code-005658-9576", "Test-Expired-Code")]
        [TestCase("การทดสอบ-LC-005908-3073", "Local-Community")]
        public void BrandingKey_ReturnsCorrectValue(string code, string expectedBranding)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedBranding, subscription.BrandingKey);
        }

        [TestCase("", "", "")]
        [TestCase(null, null, null)]
        [TestCase("", "Local Community", "Legacy-LC-005809-2533")] // migrate legacy branding only to a modern subscription code
        [TestCase("", "Local-Community", "Legacy-LC-005809-2533")] // migrate legacy branding only to a modern subscription code
        [TestCase(
            "Test-Expiring-Soon-005779-1460",
            "Local-Community",
            "Test-Expiring-Soon-005779-1460"
        )] // ignore branding if we have a code
        public void FromSettingsXml_ReturnsCorrectValue(
            string code,
            string brandingForMigration,
            string expectedCode
        )
        {
            var subscription = Subscription.FromSettingsXml(code, brandingForMigration);
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

        [TestCase(null, Subscription.SubscriptionTier.None)]
        [TestCase("", Subscription.SubscriptionTier.None)]
        [TestCase("Legacy-LC-005809-2533", Subscription.SubscriptionTier.Community)]
        [TestCase("SIL-LEAD-123456-7890", Subscription.SubscriptionTier.Enterprise)]
        [TestCase("การทดสอบ-LC-005908-3073", Subscription.SubscriptionTier.Community)]
        public void Tier_ReturnsCorrectEnum(string code, Subscription.SubscriptionTier expectedTier)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedTier, subscription.Tier);
        }

        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("Invalid", true)]
        [TestCase("Invalid-Code", true)]
        [TestCase("Project-Name-12345", true)]
        [TestCase("Project-Name-123456", true)]
        [TestCase("Project-Name-123456-", true)]
        [TestCase("Project-Name-123456-1", true)]
        [TestCase("Project-Name-123456-123", true)]
        [TestCase("Test-Expired-Code-005658-9576", false)]
        public void LooksIncomplete_DetectsIncompleteCode(string code, bool expectedResult)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedResult, subscription.LooksIncomplete());
        }

        [TestCase("", "none")]
        [TestCase(null, "none")]
        [TestCase("Tttest-Expired-Code-005658-9576", "invalid")]
        [TestCase("Project-123456", "incomplete")]
        [TestCase("UnitTest-E-006046-3301", "ok")] // Using the test code that should be valid
        public void GetIntegrityLabel_ReturnsCorrectValue(string code, string expectedLabel)
        {
            var subscription = new Subscription(code);
            Assert.AreEqual(expectedLabel, subscription.GetIntegrityLabel());
        }

        [TestCase(null, false)]
        [TestCase("", false)]
        [TestCase("Different", true)]
        public void IsDifferent_DetectsChanges(string newCode, bool expectedResult)
        {
            var subscription = new Subscription(null);
            Assert.AreEqual(expectedResult, subscription.IsDifferent(newCode));
        }

        [Test]
        public void HaveEnterpriseFeatures_DependsOnTier()
        {
            // Community tier should have enterprise features
            var communitySubscription = new Subscription("HuyaVillage-LC-123456-7890");
            Assert.IsTrue(communitySubscription.HaveActiveSubscription);

            // Enterprise tier should have enterprise features
            var enterpriseSubscription = new Subscription("SIL-LEAD-123456-7890");
            Assert.IsTrue(enterpriseSubscription.HaveActiveSubscription);

            // None tier should not have enterprise features
            var noneSubscription = new Subscription("");
            Assert.IsFalse(noneSubscription.HaveActiveSubscription);
        }

        [Test]
        public void Personalization_ExtractsCorrectly()
        {
            var subscription = new Subscription("Huya-Village-LC-123456-7890");
            Assert.AreEqual("Huya Village", subscription.Personalization);
        }

        [TestCase("", "0001-01-01")]
        [TestCase("Legacy-LC-005809-2533", "2025-06-01")] //Subscription.kDefaultExpirationDate
        [TestCase("Test-Expired-Code-005658-9576", "2025-01-01")]
        [TestCase("i-am-invalid", "0001-01-01")]
        public void GetExpirationDate_CalculatesCorrectly(string code, string expectedYYYYmmDD)
        {
            var subscription = new Subscription(code);
            var expirationDate = subscription.GetExpirationDate();
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
            var subscription = new Subscription(input);
            var result = subscription.GetExpirationDate();

            Assert.That(result.Year, Is.EqualTo(year));
            Assert.That(result.Month, Is.EqualTo(month));
            Assert.That(result.Day, Is.EqualTo(day));
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
            var result = subscription.GetExpirationDate();

            Assert.That(result, Is.EqualTo(DateTime.MinValue));
        }

        [TestCase("", true)]
        [TestCase(null, true)]
        [TestCase("Acme3506487", true)] // no dashes
        [TestCase("Acme-3506487", true)] // too few dashes
        [TestCase("Acme-3506-487-nonsense", false)] // clearly wrong here
        [TestCase("Acme-003506-0488", false)] // wrong checksum
        [TestCase("Acme-silly-1234", true)] // not a number in part 2... but could be start of Acme-silly-123456-7890
        [TestCase("Acme-7484-silly", false)] // not a number in part 3...debatable, COULD be start of Acme-7484-silly-123456-7890
        [TestCase("Acme-7484-si", false)] // short not a number in part 3...debatable, COULD be start of Acme-7484-si-123456-7890
        [TestCase("Quite-Phony-3098-4247", false)] // Too few digits in part 2
        [TestCase("Acme-003506-487", true)] // Too few digits in part 3
        [TestCase("Somevery long fake thing-361769-19523", false)] // Too many digits in part 3
        [TestCase("Acme-3506-487", false)] // Last two parts are numbers, but first is too short
        [TestCase("Acme-003506-", true)] // No digits in part 3 is OK, even though part 3 won't parse
        public void SubscriptionAppearsIncomplete(string input, bool incomplete)
        {
            var subscription = new Subscription(input);
            var result = subscription.LooksIncomplete();

            Assert.That(result, Is.EqualTo(incomplete));
        }
    }
}
