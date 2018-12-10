using System.Drawing;


namespace Bloom
{
	class Palette
	{
		public static Color LightTextAgainstDarkBackground = Color.WhiteSmoke;
		public static Color BloomPanelBackground = Color.FromArgb(64, 64, 64);
		public static Color SILInternationalBlue = Color.FromArgb(0,101,163);//NB: the "official" RGB is 0,91,163, but all samples are close to this one I'm using.
		public static Color DisabledTextAgainstDarkBackColor = Color.Gray;

		public static Color Blue = Color.FromArgb(17,129,146);
		public static Color BloomRed = Color.FromArgb(0xff, 0xD6, 0x56, 0x49);
		public static Color BloomYellow = Color.FromArgb(254, 191, 0);
	}
}
