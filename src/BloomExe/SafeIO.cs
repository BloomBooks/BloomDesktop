using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Xml.Linq;
using TidyManaged;

namespace Bloom
{
	public class SafeIO
	{
		public static void DeleteDirectory(string path)
		{
			RetryUtil.Retry(() => Directory.Delete(path));
		}

		public static void DeleteDirectory(string path, bool recursive)
		{
			RetryUtil.Retry(() => Directory.Delete(path, recursive));
		}

		public static Document DocumentFromFile(string filePath)
		{
			return RetryUtil.Retry(() => Document.FromFile(filePath));
		}

		public static void SaveImage(Image processedBitmap, string fileName, ImageFormat format)
		{
			RetryUtil.Retry(() => processedBitmap.Save(fileName, format));
		}

		public static void SaveXElement(XElement xElement, string fileName)
		{
			RetryUtil.Retry(() => xElement.Save(fileName));
		}
	}

	public class SafeFile
	{
		public static void Copy(string sourceFileName, string destFileName)
		{
			RetryUtil.Retry(() => File.Copy(sourceFileName, destFileName));
		}

		public static void Copy(string sourceFileName, string destFileName, bool overwrite)
		{
			RetryUtil.Retry(() => File.Copy(sourceFileName, destFileName, overwrite));
		}

		public static FileStream Create(string path)
		{
			// Nothing different from File for now
			return File.Create(path);
		}

		public static StreamWriter CreateText(string path)
		{
			// Nothing different from File for now
			return File.CreateText(path);
		}

		public static void Delete(string path)
		{
			RetryUtil.Retry(() => File.Delete(path));
		}

		public static bool Exists(string path)
		{
			// Nothing different from File for now
			return File.Exists(path);
		}

		public static FileAttributes GetAttributes(string path)
		{
			return RetryUtil.Retry(() => File.GetAttributes(path));
		}

		public static DateTime GetLastWriteTime(string path)
		{
			// Nothing different from File for now
			return File.GetLastAccessTime(path);
		}

		public static DateTime GetLastWriteTimeUtc(string path)
		{
			// Nothing different from File for now
			return File.GetLastAccessTimeUtc(path);
		}

		public static void Move(string sourceFileName, string destFileName)
		{
			RetryUtil.Retry(() => File.Move(sourceFileName, destFileName));
		}

		public static FileStream OpenRead(string path)
		{
			return RetryUtil.Retry(() => File.OpenRead(path));
		}

		public static StreamReader OpenText(string path)
		{
			return RetryUtil.Retry(() => File.OpenText(path));
		}

		public static byte[] ReadAllBytes(string path)
		{
			return RetryUtil.Retry(() => File.ReadAllBytes(path));
		}

		public static string[] ReadAllLines(string path)
		{
			return RetryUtil.Retry(() => File.ReadAllLines(path));
		}

		public static string[] ReadAllLines(string path, Encoding encoding)
		{
			return RetryUtil.Retry(() => File.ReadAllLines(path, encoding));
		}

		public static string ReadAllText(string path)
		{
			return RetryUtil.Retry(() => File.ReadAllText(path));
		}

		public static string ReadAllText(string path, Encoding encoding)
		{
			return RetryUtil.Retry(() => File.ReadAllText(path, encoding));
		}
		public static void SetAttributes(string path, FileAttributes fileAttributes)
		{
			RetryUtil.Retry(() => File.SetAttributes(path, fileAttributes));
		}

		public static void WriteAllBytes(string path, byte[] bytes)
		{
			RetryUtil.Retry(() => File.WriteAllBytes(path, bytes));
		}

		public static void WriteAllText(string path, string contents)
		{
			RetryUtil.Retry(() => File.WriteAllText(path, contents));
		}

		public static void WriteAllText(string path, string contents, Encoding encoding)
		{
			RetryUtil.Retry(() => File.WriteAllText(path, contents, encoding));
		}
	}
}
