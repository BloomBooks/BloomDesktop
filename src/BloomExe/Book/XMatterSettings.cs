using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Book
{
    /// <summary>
    /// This class represents the xmatter.json file that can be included in an xmatter folder
    /// </summary>
    public class XMatterSettings
    {
        public class Settings
        {
            [JsonProperty("appearance")]
            public ExpandoObject Appearance;

            [JsonProperty(
                "legacyThemeCanBeUsed",
                DefaultValueHandling = DefaultValueHandling.Populate
            )]
            [DefaultValue(true)]
            public bool LegacyThemeCanBeUsed;

            [JsonProperty(
                "harvesterMayConvertToDefaultTheme",
                DefaultValueHandling = DefaultValueHandling.Populate
            )]
            [DefaultValue(false)]
            public bool HarvesterMayConvertToDefaultTheme;
        }

        public static Settings GetSettingsOrNull(string settingsPath)
        {
            if (!RobustFile.Exists(settingsPath))
                return null;
            var settingsJson = RobustFile.ReadAllText(settingsPath);
            return JsonConvert.DeserializeObject<Settings>(settingsJson);
        }
    }
}
