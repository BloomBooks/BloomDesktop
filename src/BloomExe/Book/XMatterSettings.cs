using System;
using System.Collections.Generic;
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
        }

        public static Settings GetSettingsOrNull(string settingsPath)
        {
            if (!RobustFile.Exists(settingsPath))
                return null;
            var settingsJson = System.IO.File.ReadAllText(settingsPath);
            return JsonConvert.DeserializeObject<Settings>(settingsJson);
        }
    }
}
