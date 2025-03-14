using System;
using System.Linq;

public class Subscription
{
    // These options must match the strings used in requiresBloomEnterprise.tsx
    public enum SubscriptionTier
    {
        None,
        Community,
        Enterprise
    }

    public static string kExpiryDateForDeprecatedCodes = "2025-07-01"; // per Cate. Careful! Make sure to use leading zeros in month and day.

    // The "descriptor" is the part of the subscription code before the numbers start. It can tell us the branding, the tier, flavors, and individual subscriber account.
    public string Descriptor { get; private set; }
    public DateTime ExpirationDate { get; private set; }
    public bool EditingBlorgBook { get; private set; } = false;
    public readonly string Code;

    public SubscriptionTier Tier { get; private set; }

    public Subscription(string code)
    {
        Code = code;
        Descriptor = CalculateDescriptor();
        Tier = CalculateTier();
        ExpirationDate = CalculateExpirationDate();
    }

    // Factor Method
    // We would really like to say look it's only the code, from that we can get everything else.
    // However there are two cases where that is not enough:
    // 1. Legacy collections will have no code, but only a branding of "Local-Community".
    // 2. We are editing a book that was uploaded to BloomLibrary.org, and we want to use the same branding as when it was uploaded but the code
    // was intentionally blanked so as not to publish it.
    public static Subscription FromCollectionSettingsInfo(
        string code,
        string descriptor,
        bool editingABlorgBook = false
    )
    {
        if (
            string.IsNullOrWhiteSpace(code)
            && (descriptor == "Local-Community" || descriptor == "Local Community")
        )
        {
            // migrating to actual code
            code = "Legacy-LC-005809-2533"; // expires on 1 July 2025
        }

        // When on BloomLibrary.org you click "Download for Edit", we want to let you use the same tier and
        // branding as when it was uploaded, even if it is expired.
        if (editingABlorgBook)
        {
            var sub = new Subscription(code);
            sub.EditingBlorgBook = editingABlorgBook;

            sub.Descriptor = descriptor;
            sub.ExpirationDate = DateTime.Now.AddDays(1);

            sub.Tier =
                descriptor == "Local-Community" || descriptor == "Local Community"
                    ? SubscriptionTier.Community
                    : SubscriptionTier.Enterprise; // see https://issues.bloomlibrary.org/youtrack/issue/BL-14419

            return sub;
        }

        return new Subscription(code);
    }

    public string BrandingProjectKey
    {
        get
        {
            if (Descriptor.Contains("-LC"))
                return "Local-Community";
            if (string.IsNullOrWhiteSpace(Descriptor))
                return "Default";
            return Descriptor; // a normal Enterprise code, perhaps with a region or flavor
        }
    }

    // enhance: extract and normalize the date part
    public bool GetChecksumCorrect()
    {
        if (Code == null)
            return false;

        string descriptor;
        int datePart;
        int combinedChecksum;
        ParseCode(out descriptor, out datePart, out combinedChecksum);

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

    public bool LooksIncomplete()
    {
        if (Code == null)
            return true;
        var parts = Code.Split('-');
        if (parts.Length < 3)
            return true; // less than the required three components
        int last = parts.Length - 1;
        int dummy;
        if (!Int32.TryParse(parts[last - 1], out dummy))
            return true; // If they haven't started typing numbers, assume they're still in the name part, which could include a hyphen
        // If they've typed one number, we expect another. (Might not be true...ethnos-360-guatemala is incomplete...)
        // So, we already know the second-last part is a number, only short numbers or empty last part qualify as incomplete now.
        // Moreover, for the whole thing to be incomplete in this case, the completed number must be the right length; otherwise,
        // we consider it definitely wrong.
        if (
            parts[last - 1].Length == 6
            && parts[last].Length < 4
            && (parts[last].Length == 0 || Int32.TryParse(parts[last], out dummy))
        )
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
        if (LooksIncomplete())
        {
            return "incomplete";
        }
        if (!GetChecksumCorrect())
        {
            return "invalid";
        }

        return "ok";
    }

    public string Personalization
    {
        // everything before the -LC- is the personalization code, e.g. "Foobar-Village". Replace dashes with spaces.
        get { return Code.Substring(0, Code.IndexOf("-LC-")).Replace("-", " "); }
    }

    // Enhance: soon we will devide up these so that they don't have exactly the same set of features
    public bool HaveActiveSubscription =>
        Tier == Subscription.SubscriptionTier.Enterprise
        || Tier == Subscription.SubscriptionTier.Community;

    // Since normally all info comes from the code, this allows us to ignore the code and just set what we need.
    // If a unit test breaks because of an expired subscription, consider fixing it by using this method or one like it.
    public static Subscription ForUnitTestWithOverrideTierOrDescriptor(
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
    public static Subscription ForUnitTestWithOverrideDescriptor(string descriptor)
    {
        var subscription = new Subscription("");

        // Directly set the descriptor and expiration date for testing
        subscription.Descriptor = descriptor;
        subscription.ExpirationDate = DateTime.Now.AddDays(1);

        return subscription;
    }

    // From the subscription code extract everything up to the second-last hyphen.
    private string CalculateDescriptor()
    {
        ParseCode(out var descriptor, out var datePart, out var combinedChecksum);
        return descriptor;
    }

    private void ParseCode(out string descriptor, out int datePortion, out int checksum)
    {
        // valid codes are
        // (empty) = "", 0, 0
        // foobar = "foobar", 0, 0
        // foo-bar  = "foo-bar", 0, 0
        // foo-blah-bar = "foo-blah-bar", 0, 0
        // foo-bar-123456-7890 = "foo-bar", 123456, 7890
        // foo-bar-*****-****  = "foo-bar", 0, 0
        datePortion = 0;
        checksum = 0;
        descriptor = "";

        if (string.IsNullOrEmpty(Code))
            return;
        // see if the last part has "***-***" like a redacted date and checksum. If so, the descriptor is what precedes it.
        if (Code.EndsWith("-***-***"))
        {
            descriptor = Code.Replace("-***-***", "");
            return;
        }
        var parts = Code.Split('-').ToList();
        // if the last two parts are digits, remove them and parse them as numbers.
        if (parts.Count < 3)
        {
            datePortion = 0;
            checksum = 0;
            descriptor = Code;
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
            return DateTime.Parse(kExpiryDateForDeprecatedCodes);

        ParseCode(out var descriptor, out var datePart, out var combinedChecksum);

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
            return DateTime.Parse(kExpiryDateForDeprecatedCodes);
        return date;
    }

    private SubscriptionTier CalculateTier()
    {
        var descriptor = CalculateDescriptor();
        if (string.IsNullOrWhiteSpace(descriptor) || descriptor == "Default")
            return SubscriptionTier.None;
        else if (
            descriptor == "Local-Community"
            || descriptor == "Local Community" /* pre 4.4 */
            || descriptor.EndsWith("-LC")
        )
            return SubscriptionTier.Community;
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
        return Descriptor + "-***-***";
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
}
