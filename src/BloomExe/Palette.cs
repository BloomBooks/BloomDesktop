using System.Drawing;

namespace Bloom
{
	class Palette
	{
		public static Color LightTextAgainstDarkBackground = Color.WhiteSmoke;
		public static Color SILInternationalBlue =  Color.FromArgb(0,101,163);//NB: the "official" RGB is 0,91,163, but all samples are close to this one I'm using.
		public static Color DarkTextAgainstBackgroundColor = Color.Black;
		public static Color DisabledTextAgainstDarkBackColor = Color.Gray;
		public static string kBloomBlueHex = "#1D94A4";
		public static Color BloomRed = GetColor("#FFD65649");
		public static Color BloomBlue = GetColor(kBloomBlueHex);
		public static Color BloomYellow = GetColor("#FEBF00");	// RGB: 254, 191, 0
		public static Color BloomPurple = GetColor("#96668f");	// RGB: 150, 102, 43
		public static Color GeneralBackground = GetColor("#2E2E2E");
		public static Color SidePanelBackgroundColor = GeneralBackground;
		public static Color SelectedTabBackground = SidePanelBackgroundColor;
		public static Color UnselectedTabBackground = GetColor("#575757");
		public static Color BookListSplitterColor = GetColor("#1a1a1a");

		private static Color GetColor(string hexColor)
		{
			// Note that Mono doesn't have System.Windows.Media.
			return ColorTranslator.FromHtml(hexColor);
		}
	}
}
