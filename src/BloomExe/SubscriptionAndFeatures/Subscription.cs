using System;
using System.Globalization;
using System.Linq;

namespace Bloom.SubscriptionAndFeatures
{ // NB: this must match c# enum SubscriptionTier in FeaturesStatus.ts
    // Tiers are ordered, so if you have a higher tier, you can use the features of the lower tiers.
    public enum SubscriptionTier
    {
        Basic = 0,
        Pro = 1,
        LocalCommunity = 2,
        Enterprise = 3,
    }

    public class Subscription
    {
        public static string kExpiryDateForDeprecatedCodes = "2025-07-01"; // per Cate. Careful! Make sure to use leading zeros in month and day.

        // The "descriptor" is the part of the subscription code before the numbers start. It can tell us the branding,
        // the tier, flavors, and individual subscriber account.
        // If the subscription is invalid or expired, and nothing overrode this, the descriptor will be empty.
        public string Descriptor { get; private set; }

        public DateTime ExpirationDate { get; private set; }

        // The editingBlorgBook flag is used to indicate that we are editing a book that was uploaded to BloomLibrary.org.
        // In that case, we want to use the same branding as when it was uploaded, even if the code is expired.
        public bool EditingBlorgBook { get; private set; } = false;

        // The subscription code is a string like "PNG-RISE-123456-1234"
        public readonly string Code;

        public SubscriptionTier Tier { get; private set; }

        public Subscription(string code)
        {
            Code = code;
            Descriptor = CalculateDescriptor();
            ExpirationDate = CalculateExpirationDate();
            Tier = CalculateTier();
        }

        /// <summary>
        /// Factory Method
        /// We would really like to say look it's only the code, from that we can get everything else.
        /// However there are two cases where that is not enough:
        /// 1. Local legacy collections have no code, but only a branding of "Local-Community".
        /// 2. We are editing a book that was uploaded to BloomLibrary.org, and we want to use the same branding
        ///    as when it was uploaded, but the code was intentionally blanked so as not to publish it.
        /// </summary>
        /// <param name="code">content of &lt;SubscriptionCode&gt; from file, or null if empty</param>
        /// <param name="brandingProjectName">content of &lt;BrandingProjectName&gt; from file, or "Default" if empty</param>
        /// <param name="editingABlorgBook">true iff we're editing a book downloaded specifically for edit/update</param>
        /// <returns>a new Subscription object based on the inputs</returns>
        /// <remarks>
        /// When downloading books for editing/updating, Bloom Library provides a code of "Local-Community-***-***"
        /// for the branding of "Local-Community" and does not provide a branding.  (so it goes to "Default")
        /// </remarks>
        public static Subscription FromCollectionSettingsInfo(
            string code,
            string brandingProjectName,
            bool editingABlorgBook = false
        )
        {
            // Collections created before Bloom 4.4 use the branding name "Local Community" instead of "Local-Community"
            if (brandingProjectName == "Local Community")
                brandingProjectName = "Local-Community";
            string descriptor = null;
            // If the collection has <SubscriptionCode>foobar-***-***</SubscriptionCode>, but
            // <BrandingProjectName>Default</BrandingProjectName>, then we have a conflict (which is why we're
            // phasing out the use of <BrandingProjectName>). In that case, go with what is given in the code
            // by setting the descriptor.
            if (
                editingABlorgBook
                && brandingProjectName == "Default"
                && code != null
                && code.Contains(kRedactedCodeSuffix)
            )
            {
            if (code == "Local-Community-***-***")
            {
                // Migrate the downloaded book to the new code and descriptor.
                code = "Legacy-LC-005839-2533"; // expires on 1 July 2025
                descriptor = "Legacy-LC";
            }
            else
                {
                    // set the descriptor by parsing the code
                    ParseCode(code, out descriptor, out _, out _);
                }
            }
        if (string.IsNullOrWhiteSpace(code) && (brandingProjectName == "Local-Community"))
        {
            // Migrate the local collection to the new code and descriptor.
            code = "Legacy-LC-005839-2533"; // expires on 1 July 2025
            descriptor = "Legacy-LC";
        }

            // When on BloomLibrary.org you click "Download for Edit", we want to let you use the same tier and
            // branding as when it was uploaded, even if it is expired.
            if (editingABlorgBook)
            {
                Subscription sub;
                // The Subscription.Descriptor value is calculated by code in the constructor, so we don't
                // need to set it separately.
                if (string.IsNullOrEmpty(code))
                {
                    sub = new Subscription(brandingProjectName + kRedactedCodeSuffix);
                }
                else
                    sub = new Subscription(code);

                sub.EditingBlorgBook = editingABlorgBook;

                var tomorrow = DateTime.Now.AddDays(1);
                if (sub.ExpirationDate < tomorrow)
                    sub.ExpirationDate = tomorrow;

                if (code == null && brandingProjectName == "Default")
                    sub.Tier = SubscriptionTier.Basic;
                // After migration, the code for "Local-Community" is "Legacy-LC-5809-2533", but the branding project
                // name is still written out as "Local-Community".  The second part of the test is probably not needed,
                // but we all believe in both belt and suspenders, right?
                else if (
                    (descriptor != null && descriptor.EndsWith("-LC"))
                    || brandingProjectName == "Local-Community"
                )
                    sub.Tier = SubscriptionTier.LocalCommunity;
                else
                    sub.Tier = SubscriptionTier.Enterprise; // see https://issues.bloomlibrary.org/youtrack/issue/BL-14419

                return sub;
            }

            return new Subscription(code);
        }

        // A BrandingKey must match a folder name under the src/content/branding folder,
        // unless that branding isn't yet supported by the current running Bloom.
        // The BrandingKey will sometimes be the same as the descriptor, e.g. "Acme-Literacy".
        // But a descriptor like "Acme-LC" will have a BrandingKey of "Local-Community".
        // An empty descriptor will have a BrandingKey of "Default".
        public string BrandingKey
        {
            get
            {
                if (IsExpired())
                    return "Default";
                if (Descriptor.Contains("-LC"))
                    return "Local-Community";
                if (string.IsNullOrWhiteSpace(Descriptor))
                    return "Default";
                return Descriptor; // a normal Enterprise code, perhaps with a region or flavor
            }
        }

        public bool IsChecksumCorrect()
        {
            if (Code == null)
                return false;

            string descriptor;
            int datePart;
            int combinedChecksum;
            ParseCode(Code, out descriptor, out datePart, out combinedChecksum);

            // Early exit for invalid code formats
            if (string.IsNullOrEmpty(descriptor) || datePart == 0 || combinedChecksum == 0)
                return false;

            int checkSum = CheckSum(CalculateDescriptor());
            if ((Math.Floor(Math.Sqrt(datePart)) + checkSum) % 10000 != combinedChecksum)
                return false;
            return true;
        }

        public bool IsExpired()
        {
            if (Code == null)
                return true;
            var date = ExpirationDate;
            if (date == DateTime.MinValue)
                return true; // invalid code
            return date < DateTime.Now;
        }

        private bool LooksIncomplete()
        {
            if (Code == null)
                return true;
            var parts = Code.Split('-');
            if (parts.Length < 3)
                return true; // less than the required three components
            var checksumPart = parts[parts.Length - 1];
            if (checksumPart.Length < 4)
                return true;
            var datePart = parts[parts.Length - 2];
            if (datePart.Length < 6)
                return true;

            return false;
        }

        // Must match the function associated with the code generation google sheet
        private int CheckSum(string code)
        {
            var sum = 0;
            code = code.ToUpperInvariant();
            for (var i = 0; i < code.Length; i++)
            {
                sum += code[i] * i;
            }
            return sum;
        }

        public string GetIntegrityLabel()
        {
            if (String.IsNullOrWhiteSpace(Code))
            {
                return "none";
            }
            var parts = Code.Split('-');
            if (parts.Length < 3)
                return "incomplete";

            // the date part is the next to last part, must be all numbers, and at least 6 digits
            var datePart = parts[parts.Length - 2];
            if (string.IsNullOrWhiteSpace(datePart))
                return "incomplete";
            if (!datePart.All(char.IsDigit))
                return "incomplete";
            if (datePart.Length < 6)
                return "invalid"; // we say invalid because we have a checksum part, but the date part was too short

            // the checksum is the final part, must be all numbers, and at least 4 digits
            var checksumPart = parts.Last();
            if (!checksumPart.All(char.IsDigit))
                return "invalid";
            if (checksumPart.Length < 4)
                return "incomplete";

            if (!IsChecksumCorrect())
                return "invalid";

            return "ok";
        }

        // Personalization is the part of the some descriptors that is used to output something unique to this subscription
        // without creating a custom branding.
        public string Personalization
        {
            // In Local Community subscription, the personalization code is everything preceding the "-LC-". We replace dashes with spaces.
            get
            {
                // if the descriptor contains "-LC", we want to return the part before that, with dashes replaced by spaces. Otherwise, empty string.
                if (string.IsNullOrWhiteSpace(Descriptor))
                    return "";
                var parts = Descriptor.Split('-');
                var lcIndex = Array.IndexOf(parts, "LC");
                if (lcIndex > 0)
                {
                    var personalization = string.Join(" ", parts.Take(lcIndex).ToArray());
                    return personalization;
                }
                return ""; // no "-LC" found, so return empty string.
            }
        }

        // Enhance: soon we will divide up these so that they don't have exactly the same set of features
        public bool HaveActiveSubscription =>
            Tier == SubscriptionTier.Enterprise || Tier == SubscriptionTier.LocalCommunity;

        // From the subscription code extract everything up to the second-last hyphen.
        // Pays no attention to the validity of the code, just returns the part before the numbers.
        private string CalculateDescriptor()
        {
            ParseCode(Code, out var descriptor, out _, out _);
            return descriptor;
        }

        private const string kRedactedCodeSuffix = "-***-***";

        private static void ParseCode(
            string code,
            out string descriptor,
            out int datePortion,
            out int checksum
        )
        {
            // valid codes are
            // (empty) = "", 0, 0
            // foobar = "foobar", 0, 0
            // foo-bar  = "foo-bar", 0, 0
            // foo-blah-bar = "foo-blah-bar", 0, 0
            // foo-bar-123456-7890 = "foo-bar", 123456, 7890
            // foo-bar-***-***  = "foo-bar", 0, 0
            datePortion = 0;
            checksum = 0;
            descriptor = "";

            if (string.IsNullOrEmpty(code))
                return;
            // see if the last part has "***-***" like a redacted date and checksum. If so, the descriptor is what precedes it.
            if (code.EndsWith(kRedactedCodeSuffix))
            {
                descriptor = code.Replace(kRedactedCodeSuffix, "");
                return;
            }
            var parts = code.Split('-').ToList();
            // if the last two parts are digits, remove them and parse them as numbers.
            if (parts.Count < 3)
            {
                datePortion = 0;
                checksum = 0;
                descriptor = code;
                return;
            }
            if (parts.Last().Length == 4 && parts[parts.Count - 2].Length == 6)
            {
                Int32.TryParse(parts.Last(), out checksum);
                Int32.TryParse(parts[parts.Count - 2], out datePortion);
                parts.RemoveAt(parts.Count - 1);
                parts.RemoveAt(parts.Count - 1);
            }
            descriptor = string.Join("-", parts.ToArray());
        }

        // Parse a string like PNG-RISE-361769-363798 or SIL-LEAD-361769-363644,
        // generated by a private google spreadsheet. The two last elements are numbers;
        // the first is an encoding of an expiry date, the second is a simple hash of
        // the project name (case-insensitive) and the expiry date, used to make it
        // a little less trivial to fake codes. We're not aiming for something that
        // would be difficult for someone willing to take the trouble to read this code.
        private DateTime CalculateExpirationDate()
        {
            if (Code == null)
            return DateTime.MinValue;

        if (Code == "Local-Community")
            return DateTime.ParseExact(
                kExpiryDateForDeprecatedCodes,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );

        ParseCode(Code, out var descriptor, out var datePart, out var combinedChecksum);

            // Early exit for invalid code formats
            if (string.IsNullOrEmpty(descriptor) || datePart == 0 || combinedChecksum == 0)
                return DateTime.MinValue;

            int checkSum = CheckSum(CalculateDescriptor());
            if ((Math.Floor(Math.Sqrt(datePart)) + checkSum) % 10000 != combinedChecksum)
                return DateTime.MinValue;

            int dateNum = datePart + 40000; // days since Dec 30 1899
            var date = new DateTime(1899, 12, 30) + TimeSpan.FromDays(dateNum);

        // At one time there were some subscriptions which never ended. Those have been retired.
        if (date.Year == 3000)
            return DateTime.ParseExact(
                kExpiryDateForDeprecatedCodes,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture
            );
        return date;
    }

    private SubscriptionTier CalculateTier()
    {
        if (GetIntegrityLabel() != "ok" || ExpirationDate < DateTime.Now)
                return SubscriptionTier.Basic;
            var descriptor = CalculateDescriptor();
            if (string.IsNullOrWhiteSpace(descriptor) || descriptor == "Default")
                return SubscriptionTier.Basic;
            else if (
                descriptor == "Local-Community"
                || descriptor == "Local Community" /* pre 4.4 */
                || descriptor.EndsWith("-LC")
            )
                return SubscriptionTier.LocalCommunity;
            else
                return SubscriptionTier.Enterprise;
        }

        public string GetRedactedCode()
        {
            if (string.IsNullOrEmpty(Code))
                return "";

            // In the future we may redact parts of the descriptor; the parts we need to retain will
            // be the tier and any actual branding name, region and "flavor"
            // needed to get the right files, styling, and defaults.
            return Descriptor + kRedactedCodeSuffix;
        }

        public static Subscription FromLegacyBranding(string branding)
        {
            if (string.IsNullOrEmpty(branding))
                return new Subscription("");

            // Local-Community is the only one that has a legacy branding name.
            if (branding == "Local-Community" || branding == "Local Community")
                return new Subscription("Local-Community");

            // All other legacy branding names are considered expired.
            return new Subscription("");
        }

        internal bool IsDifferent(string code)
        {
            if (string.IsNullOrEmpty(Code) && string.IsNullOrEmpty(code))
                return false;
            return Code != code;
        }

        internal static Subscription ForUnitTestWithOverrideTier(SubscriptionTier tier)
        {
            var subscription = new Subscription("");

            // Directly set the tier for testing
            subscription.Tier = tier;

            return subscription;
        }

        // Since normally all info comes from the code, this allows us to ignore the code and just set what we need.
        // If a unit test breaks because of an expired subscription, consider fixing it by using this method or one like it.
        internal static Subscription ForUnitTestWithOverrideTierOrDescriptor(
            SubscriptionTier tier,
            string descriptor
        )
        {
            var subscription = new Subscription("");

            // Directly set the internal fields for testing
            subscription.Descriptor = descriptor;
            subscription.Tier = tier;
            subscription.ExpirationDate = DateTime.Now.AddDays(1);

            return subscription;
        }

        // Since normally all info comes from the code, this allows us to ignore the code and just set what we need.
        // If a unit test breaks because of an expired subscription, consider fixing it by using this method or one like it.
        internal static Subscription ForUnitTestWithOverrideDescriptor(string descriptor)
        {
            var subscription = new Subscription("");

            // Directly set the descriptor and expiration date for testing
            subscription.Descriptor = descriptor;
            subscription.ExpirationDate = DateTime.Now.AddDays(1);

            return subscription;
        }
    }
}
