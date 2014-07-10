using Bloom.web;

namespace Bloom
{
	public static class Extensions
	{
		public static string ToLocalhost(this string fileName)
		{
			if (fileName.StartsWith(ServerBase.PathEndingInSlash)) return fileName;
			return ServerBase.PathEndingInSlash + fileName.Replace(":", "%3A").Replace("\\", "/");
		}

		public static string FromLocalhost(this string uri)
		{
			if (uri.StartsWith(ServerBase.PathEndingInSlash))
				uri = uri.Substring(ServerBase.PathEndingInSlash.Length).Replace("%3A", ":");
			return uri;
		}
	}
}
