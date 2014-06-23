using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom
{
    public static class ExtensionMethods
    {
        public static int ToInt(this bool value)
        {
            if (value) return 1;
            return 0;
        }

		public static void UpdateSetting(this List<string> list, string settingName, bool shouldBeSet)
		{
			var isSet = list.Contains(settingName);

			if (isSet && !shouldBeSet)
			{
				list.Remove(settingName);
			}
			else if (!isSet && shouldBeSet)
			{
				list.Add(settingName);
			}
		}
    }
}
