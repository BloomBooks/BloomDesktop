using System;

namespace Bloom.Utils
{
    /// <summary>
    /// Attach the filepath to file-specific exceptions, e.g. errors accessing that file
    /// </summary>
    public class FileException : Exception
    {
        public string FilePath;

        // When WinForms catches exceptions thrown from invoked methods, it only gets the innermost exception
        // So we can't put the original exception in innerException or this FilePath will get lost in such cases
        // See https://stackoverflow.com/questions/15668334/preserving-exceptions-from-dynamically-invoked-methods
        // https://stackoverflow.com/questions/347502/why-does-the-inner-exception-reach-the-threadexception-handler-and-not-the-actua
        public Exception OriginalException;

        public FileException(string filePath, Exception originalException)
        {
            FilePath = filePath;
            OriginalException = originalException;
        }

        public static string getFilePathIfPresent(Exception exception)
        {
            if (exception is FileException)
            {
                return ((FileException)exception).FilePath;
            }
            return null;
        }

        public static Exception UnwrapIfFileException(Exception exception)
        {
            if (exception is FileException)
            {
                return ((FileException)exception).OriginalException;
            }
            return exception;
        }
    }
}
