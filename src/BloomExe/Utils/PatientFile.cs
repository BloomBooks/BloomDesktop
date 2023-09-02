using System;
using System.IO;
using System.Text;
using SIL.Code;


namespace Bloom.Utils
{
	public static class PatientFile
	{
		private const int FileStreamBufferSize = 4096;

		internal static int BufferSize = 4096;
		public static FileStream CreateFileStream(string path, FileMode mode)
		{
			FileStream fs = null;
			Patient.Retry(() => fs = new FileStream(path, mode), memo: "CreateFileStream " + path);
			return fs;
		}
		public static FileStream CreateFileStream(string path, FileMode mode, FileAccess access)
		{
			FileStream fs = null;
			Patient.Retry(() => fs = new FileStream(path, mode, access), memo: "CreateFileStream " + path);
			return fs;
		}
		public static FileStream CreateFileStream(string path, FileMode mode, FileAccess access, FileShare share)
		{
			FileStream fs = null;
			Patient.Retry(() => fs = new FileStream(path, mode, access, share), memo: "CreateFileStream " + path);
			return fs;
		}

		public static void Copy(string sourceFileName, string destFileName, bool overwrite = false)
		{
			Patient.Retry(delegate
			{
				byte[] buffer = new byte[BufferSize];
				using (FileStream fileStream = new FileStream(sourceFileName, FileMode.Open, FileAccess.Read))

				using (FileStream fileStream2 = new FileStream(destFileName, (!overwrite) ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
				{

					int count;
					while ((count = fileStream.Read(buffer, 0, BufferSize)) > 0)
					{
						fileStream2.Write(buffer, 0, count);
					}

					fileStream2.Flush(flushToDisk: true);
				}

			}, memo: "copy to" + destFileName);
		}

		public static FileStream Create(string path)
		{
			return new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.WriteThrough);
		}

		public static StreamWriter CreateText(string path)
		{
			if (path == null)
			{
				throw new ArgumentNullException("path");
			}

			if (path.Length == 0)
			{
				throw new ArgumentException("Argument_EmptyPath");
			}

			return new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough | FileOptions.SequentialScan));
		}

		public static void Delete(string path)
		{
			Patient.Retry(delegate
			{
				File.Delete(path);
			}, memo: "delete " + path);
		}

		public static bool Exists(string path)
		{
			return File.Exists(path);
		}

		public static FileAttributes GetAttributes(string path)
		{
			return Patient.Retry(() => File.GetAttributes(path));
		}

		public static DateTime GetLastWriteTime(string path)
		{
			return File.GetLastWriteTime(path);
		}

		public static DateTime GetLastWriteTimeUtc(string path)
		{
			return File.GetLastWriteTimeUtc(path);
		}

		public static void Move(string sourceFileName, string destFileName)
		{
			Patient.Retry(delegate
			{
				File.Move(sourceFileName, destFileName);
			});
		}

		public static void Move(string sourceFileName, string destFileName, bool overWrite)
		{
			if (overWrite && Exists(destFileName))
			{
				Delete(destFileName);
			}

			Move(sourceFileName, destFileName);
		}

		public static FileStream OpenRead(string path)
		{
			return Patient.Retry(() => File.OpenRead(path));
		}

		public static StreamReader OpenText(string path)
		{
			return Patient.Retry(() => File.OpenText(path));
		}

		public static byte[] ReadAllBytes(string path)
		{
			return Patient.Retry(() => File.ReadAllBytes(path));
		}

		public static string[] ReadAllLines(string path)
		{
			return Patient.Retry(() => File.ReadAllLines(path));
		}

		public static string[] ReadAllLines(string path, Encoding encoding)
		{
			return Patient.Retry(() => File.ReadAllLines(path, encoding));
		}

		public static string ReadAllText(string path)
		{
			return Patient.Retry(() => File.ReadAllText(path));
		}

		public static string ReadAllText(string path, Encoding encoding)
		{
			return Patient.Retry(() => File.ReadAllText(path, encoding));
		}

		public static void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName)
		{
			Patient.Retry(delegate
			{
				try
				{
					File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
				}
				catch (UnauthorizedAccessException ex)
				{
					try
					{
						ReplaceByCopyDelete(sourceFileName, destinationFileName, destinationBackupFileName);
					}
					catch
					{
						throw ex;
					}
				}
			}, memo: "Replace " + destinationFileName);
		}

		public static void ReplaceByCopyDelete(string sourcePath, string destinationPath, string backupPath)
		{
			if (!string.IsNullOrEmpty(backupPath) && PatientFile.Exists(destinationPath))
			{
				PatientFile.Copy(destinationPath, backupPath, overwrite: true);
			}

			PatientFile.Copy(sourcePath, destinationPath, overwrite: true);
			PatientFile.Delete(sourcePath);
		}

		public static void SetAttributes(string path, FileAttributes fileAttributes)
		{

			File.SetAttributes(path, fileAttributes);

		}

		public static void WriteAllBytes(string path, byte[] bytes)
		{
			Patient.Retry(() =>
			{
				using (FileStream fileStream = File.Create(path, 4096, FileOptions.WriteThrough))
				{
					fileStream.Write(bytes, 0, bytes.Length);
					fileStream.Close();
				}
			}, memo: "WriteBytes " + path);
		}

		public static void WriteAllText(string path, string contents)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(contents);
			WriteAllBytes(path, bytes);
		}

		public static void WriteAllText(string path, string contents, Encoding encoding)
		{
			if (contents == null)
			{
				throw new ArgumentNullException("contents", "contents must not be null");
			}

			if (encoding == null)
			{
				throw new ArgumentNullException("encoding", "encoding must not be null");
			}

			Patient.Retry(delegate
			{
				using (FileStream fileStream = File.Create(path, 4096, FileOptions.WriteThrough))
				{
					byte[] preamble = encoding.GetPreamble();
					fileStream.Write(preamble, 0, preamble.Length);
					byte[] bytes = encoding.GetBytes(contents);
					fileStream.Write(bytes, 0, bytes.Length);
					fileStream.Close();
				}
			}, memo: "WriteAllText " + path);
		}
	}
}
