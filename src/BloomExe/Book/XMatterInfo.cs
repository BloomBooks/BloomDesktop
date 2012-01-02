using System.IO;

namespace Bloom.Book
{
	public class XMatterInfo
	{
		public readonly string PathToFolder;

		public XMatterInfo(string pathToFolder)
		{
			PathToFolder = pathToFolder;
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
				var x = Path.GetFileName(PathToFolder);
				var end = x.ToLowerInvariant().IndexOf("-xmatter");
				return x.Substring(0, end);
			}
		}
	}
}