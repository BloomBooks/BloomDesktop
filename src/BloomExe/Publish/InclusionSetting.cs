using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Bloom.Publish
{
    /// <summary>
    /// Represents the state of the checkbox for whether to publish a language or not.
    /// In addition to the binary Include / Exclude, also allows a "Default" value,
    /// which means that the checkbox has never been explicitly set by the user and should fallback to the default setting for that checkbox.
    /// </summary>
    /// <remarks>
    /// Note that for IncludeByDefault and ExcludeByDefault, if the inputs to the algorithm which determines whether it should be IncludeByDefault or ExcludeByDefault change,
    /// you may very well want to recompute the answer.
    /// This implies that in many cases, such as when first loading a deserialized value,
    /// encountering IncludeByDefault or ExcludeByDefault is equivalent to encountering just a generic Default (You should go and compute what it should default to)
    /// But after computing what it should default to, it may come in handy to store the result, which is why we have IncludeByDefault and ExcludeByDefault
    /// To simplify on one most simple enum we can use everywhere, we have distinct values for them
    /// </remarks>
    [JsonConverter(typeof(StringEnumConverter))] // StringEnumConverter serializes the name string value instead of the numeric value
    public enum InclusionSetting
    {
        Include,
        Exclude,
        IncludeByDefault,
        ExcludeByDefault,
    }

    public static class InclusionSettingExtensions
    {
        /// <summary>
        /// Returns true if the enum value is either Include or IncludeByDefault
        /// </summary>
        public static bool IsIncluded(this InclusionSetting checkboxVal)
        {
            switch (checkboxVal)
            {
                case InclusionSetting.Include:
                case InclusionSetting.IncludeByDefault:
                    return true;
                case InclusionSetting.Exclude:
                case InclusionSetting.ExcludeByDefault:
                    return false;
                default:
                    throw new NotImplementedException("Unknown case: " + checkboxVal.ToString());
            }
        }

        /// <summary>
        /// Returns true if the enum value is not provided By Default
        /// </summary>
        public static bool IsSpecified(this InclusionSetting checkboxVal)
        {
            switch (checkboxVal)
            {
                case InclusionSetting.Include:
                case InclusionSetting.Exclude:
                    return true;
                case InclusionSetting.IncludeByDefault:
                case InclusionSetting.ExcludeByDefault:
                    return false;
                default:
                    throw new NotImplementedException("Unknown case: " + checkboxVal.ToString());
            }
        }
    }
}
