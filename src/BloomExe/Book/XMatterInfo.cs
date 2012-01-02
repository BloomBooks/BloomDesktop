using System.IO;

namespace Bloom.Book
{
	public class XMatterInfo
	{
		private readonly string _pathToFolder;

		public XMatterInfo(string pathToFolder)
		{
			_pathToFolder = pathToFolder;
		}

		public override string ToString()
		{
			return Key;
		}
		/// <summary>
		/// E.g. in "Factory-XMatter", the key is "Factory".
		/// </summary>
		public string Key
		{
			get {
				var x = Path.GetFileName(_pathToFolder);
				var end = x.ToLowerInvariant().IndexOf("-xmatter");
				return x.Substring(0, end);
			}
		}
	}
}