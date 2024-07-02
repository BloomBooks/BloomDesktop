using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIL.IO;

namespace BloomTemp
{
    public static class TempFileUtils
    {
        /// <summary>
        /// Return a temporary path (that does not yet exist) using standard methods that should yield safe
        /// filenames regardless of the current codepage.  The caller is responsible to delete the temporary
        /// file after creating it and using it.
        /// </summary>
        public static string GetTempFilepathWithExtension(string extension)
        {
            // The Dispose call caused by the using block ensures that the returned file path
            // is not for an existing file.
            using (var temp = TempFile.WithExtension(extension))
            {
                return temp.Path;
            }
        }

        /// <summary>
        /// Returns a TempFile with a random file basename in 8.3 format with the specified extension
        /// Unlike TempFile.WithExtension, this returns w143kxnu.{extension} instead of w143kxnu.idj.{extension}
        /// </summary>
        public static TempFile GetTempFileWithPrettyExtension(string extension)
        {
            var withRandomExtension = Path.GetRandomFileName(); // in 8.3 format, e.g. w143kxnu.idj
            var withSpecifiedExtension = Path.ChangeExtension(withRandomExtension, extension); // now w143kxnu.{extension}
            return TempFile.WithFilename(withSpecifiedExtension);
        }
    }

    // ENHANCE: Replace with TemporaryFolder implemented in Palaso. However, that means
    // refactoring some Palaso code and moving TemporaryFolder from Palaso.TestUtilities into
    // Palaso.IO
    public class TemporaryFolder : IDisposable
    {
        private string _path;

        public static TemporaryFolder TrackExisting(string path)
        {
            Debug.Assert(Directory.Exists(path));
            TemporaryFolder f = new TemporaryFolder();
            f._path = path;
            return f;
        }

        [Obsolete(
            "Go ahead and give it a name related to the test.  Makes it easier to track down problems."
        )]
        public TemporaryFolder()
            : this("unnamedTestFolder") { }

        public TemporaryFolder(string name)
        {
            Debug.Assert(!String.IsNullOrWhiteSpace(name));

            _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
            if (Directory.Exists(_path))
            {
                DeleteFolderThatMayBeInUseAndIfNotFailSilently(_path);
            }

            Directory.CreateDirectory(_path);
        }

        public TemporaryFolder(TemporaryFolder parent, string name)
        {
            _path = parent.Combine(name);
            if (Directory.Exists(_path))
            {
                DeleteFolderThatMayBeInUseAndIfNotFailSilently(_path);
            }

            Directory.CreateDirectory(_path);
        }

        public string FolderPath
        {
            get { return _path; }
        }

        public void Dispose()
        {
            DeleteFolderThatMayBeInUseAndIfNotFailSilently(_path);

            GC.SuppressFinalize(this);
        }

        [Obsolete(
            "It's better to wrap the use of this in a using() so that it is automatically cleaned up, even if a test fails."
        )]
        public void Delete()
        {
            DeleteFolderThatMayBeInUseAndIfNotFailSilently(_path);
        }

        public string GetPathForNewTempFile(bool doCreateTheFile)
        {
            string s = System.IO.Path.GetRandomFileName();
            s = System.IO.Path.Combine(_path, s);
            if (doCreateTheFile)
            {
                RobustFile.Create(s).Close();
            }

            return s;
        }

        public string GetPathForNewTempFile(bool doCreateTheFile, string extension)
        {
            extension = extension.TrimStart('.');
            var s = System.IO.Path.Combine(
                _path,
                System.IO.Path.GetRandomFileName() + "." + extension
            );

            if (doCreateTheFile)
            {
                RobustFile.Create(s).Close();
            }

            return s;
        }

        public TempFile GetNewTempFile(bool doCreateTheFile)
        {
            string s = System.IO.Path.GetRandomFileName();
            s = System.IO.Path.Combine(_path, s);
            if (doCreateTheFile)
            {
                RobustFile.Create(s).Close();
            }

            return TempFile.TrackExisting(s);
        }

        [Obsolete(
            "It's better to use the explict GetNewTempFile, which makes you say if you want the file to be created or not, and give you back a whole TempFile class, which is itself IDisposable."
        )]
        public string GetTemporaryFile()
        {
            return GetTemporaryFile(System.IO.Path.GetRandomFileName());
        }

        [Obsolete(
            "It's better to use the explict GetNewTempFile, which makes you say if you want the file to be created or not, and give you back a whole TempFile class, which is itself IDisposable."
        )]
        public string GetTemporaryFile(string name)
        {
            string s = System.IO.Path.Combine(_path, name);
            RobustFile.Create(s).Close();
            return s;
        }

        public string Combine(params string[] partsOfThePath)
        {
            string result = _path;
            foreach (var s in partsOfThePath)
            {
                result = System.IO.Path.Combine(result, s);
            }

            return result;
        }

        public static void DeleteFolderThatMayBeInUseAndIfNotFailSilently(string folder)
        {
            if (Directory.Exists(folder))
            {
                try
                {
                    RobustIO.DeleteDirectory(folder, true);
                }
                catch (Exception e)
                {
                    try
                    {
                        Debug.WriteLine(e.Message);
                        //maybe we can at least clear it out a bit
                        string[] files = Directory.GetFiles(
                            folder,
                            "*.*",
                            SearchOption.AllDirectories
                        );
                        foreach (string s in files)
                        {
                            try
                            {
                                RobustFile.Delete(s);
                            }
                            catch (Exception) { }
                        }

                        //sleep and try again (in case some other thread will  let go of them)
                        Thread.Sleep(1000);
                        RobustIO.DeleteDirectory(folder, true);
                    }
                    catch (Exception) { }
                }
            }
        }
    }
}
