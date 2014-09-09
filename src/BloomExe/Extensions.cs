using System.Text;
using Bloom.web;

namespace Bloom
{
    public static class Extensions
    {
        public static string ToLocalhost(this string fileName)
        {
			// don't do this if it is done already
			if (fileName.StartsWith(ServerBase.PathEndingInSlash)) return fileName;

			// BL-117, PH: With the newer xulrunner, javascript code with parenthesis in the URL is not working correctly.
			fileName = fileName.Replace("(", "%28").Replace(")", "%29");

            return ServerBase.PathEndingInSlash + fileName.Replace(":", "%3A").Replace("\\", "/");
        }

        public static string FromLocalhost(this string uri)
        {
            if (uri.StartsWith(ServerBase.PathEndingInSlash))
                uri = uri.Substring(ServerBase.PathEndingInSlash.Length).Replace("%3A", ":");
            return uri;
        }

		public static int ToInt(this bool value)
		{
			if (value) return 1;
			return 0;
		}

		public static void AppendLineFormat(this StringBuilder sb, string format, params object[] args)
		{
			sb.AppendLine(string.Format(format, args));
		}
    }
}
