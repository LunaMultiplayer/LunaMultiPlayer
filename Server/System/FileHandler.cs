using LmpCommon;
using Server.Log;
using Server.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Server.System
{
    /// <summary>
    ///     This class provides thread safe funcionallity for file and folder work
    /// </summary>
    public class FileHandler
    {
        /// <summary>
        /// This object is used for accesing the lock semaphore dictionary as only 1 thread is allowed there
        /// </summary>
        private static readonly object SemaphoreLock = new object();

        /// <summary>
        /// This dictionary is for retrieving the correct lock based on the path of the file/folder
        /// </summary>
        private static readonly Dictionary<string, object> LockSemaphore =
            new Dictionary<string, object>();

        /// <summary>
        /// Thread safe method to append text
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="text">Text to insert</param>
        public static void AppendToFile(string path, string text)
        {
            lock (GetLockSemaphore(path))
            {
                try
                {
                    File.AppendAllText(path, text);
                }
                catch (Exception e)
                {
                    LunaLog.Error($"Error writing to file: {path}, Exception: {e}");
                }
            }
        }

        /// <summary>
        /// Thread safe file overwriting method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="data">Data to insert</param>
        /// <param name="numBytes">Number of bytes to write</param>
        public static void WriteToFile(string path, byte[] data, int numBytes)
        {
            lock (GetLockSemaphore(path))
            {
                if (ContentChecker.ContentsAreEqual(data, numBytes, path))
                    return;

                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(data, 0, numBytes);
                }
            }
        }

        /// <summary>
        /// Thread safe file overwriting method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="text">Text to insert</param>
        public static void WriteToFile(string path, string text)
        {
            var content = Encoding.UTF8.GetBytes(text);
            WriteToFile(path, content, content.Length);
        }

        /// <summary>
        /// Thread safe file creating method. It won't create the file if it already exists!
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="data">Data to insert</param>
        /// <param name="numBytes">Number of bytes to write</param>
        /// <returns>True if the file was created</returns>
        public static bool CreateFile(string path, byte[] data, int numBytes)
        {
            lock (GetLockSemaphore(path))
            {
                if (!FileExists(path))
                {
                    LunaLog.Normal($"Creating file {Path.GetFileName(path)}");

                    using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                    {
                        fs.Write(data, 0, numBytes);
                    }

                    return true;
                }

                return false;
            }
        }


        /// <summary>
        /// Thread safe file creating method. It won't create the file if it already exists!
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="text">Text to insert</param>
        /// <returns>True if the file was created</returns>
        public static bool CreateFile(string path, string text)
        {
            var content = Encoding.UTF8.GetBytes(text);
            return CreateFile(path, content, content.Length);
        }

        /// <summary>
        /// Thread safe file copying
        /// </summary>
        /// <param name="from">From path</param>
        /// <param name="to">To path</param>
        public static void FileCopy(string from, string to)
        {
            lock (GetLockSemaphore(from))
            {
                lock (GetLockSemaphore(to))
                {
                    File.Copy(from, to);
                }
            }
        }

        /// <summary>
        /// Thread safe file reading method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Bytes of the file</returns>
        public static byte[] ReadFile(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return File.Exists(path) ? File.ReadAllBytes(path) : new byte[0];
            }
        }

        /// <summary>
        /// Thread safe file text reading method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Bytes of the file</returns>
        public static string ReadFileText(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }
        }

        /// <summary>
        /// Thread safe file text reading method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>Test lines of the file</returns>
        public static string[] ReadFileLines(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            }
        }

        /// <summary>
        /// Thread safe file exist method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <returns>File exists or not</returns>
        public static bool FileExists(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return File.Exists(path);
            }
        }

        /// <summary>
        /// Thread safe folder exist method
        /// </summary>
        /// <param name="path">Path to the folder</param>
        /// <returns>Folder exists or not</returns>
        public static bool FolderExists(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return Directory.Exists(path);
            }
        }

        /// <summary>
        /// Thread safe folder delete method
        /// </summary>
        /// <param name="path">Path to the folder</param>
        /// <param name="recursive">Also delete the files and subfolders inside the folder</param>
        public static void FolderDelete(string path, bool recursive = false)
        {
            lock (GetLockSemaphore(path))
            {
                Directory.Delete(path, recursive);
            }
        }

        /// <summary>
        /// Thread safe folder create method
        /// </summary>
        /// <param name="path">Path to the folder</param>
        /// <returns>Folder exists or not</returns>
        public static void FolderCreate(string path)
        {
            lock (GetLockSemaphore(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Thread safe file moving method
        /// </summary>
        /// <param name="sourcePath">Original path</param>
        /// <param name="destPath">Destination path</param>
        public static void MoveFile(string sourcePath, string destPath)
        {
            lock (GetLockSemaphore(sourcePath))
            {
                lock (GetLockSemaphore(destPath))
                {
                    File.Move(sourcePath, destPath);
                }
            }
        }

        /// <summary>
        /// Thread safe file deleting method, checks for existence before removing the file
        /// </summary>
        /// <param name="path">Path of the file to remove</param>
        public static void FileDelete(string path)
        {
            lock (GetLockSemaphore(path))
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        /// <summary>
        /// Thread safe retrieval of files in a given path
        /// </summary>
        /// <param name="path">Path to look into</param>
        /// <param name="searchOption">Search options</param>
        /// <returns>List of files</returns>
        public static string[] GetFilesInPath(string path, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            lock (GetLockSemaphore(path))
            {
                return Directory.GetFiles(path, "*", searchOption).OrderBy(f => new FileInfo(f).CreationTime).ToArray();
            }
        }

        /// <summary>
        ///     Thread safe retrieval of folders in a given path
        /// </summary>
        /// <param name="path">Path to look into</param>
        /// <returns>List of folders</returns>
        public static string[] GetDirectoriesInPath(string path)
        {
            lock (GetLockSemaphore(path))
            {
                return Directory.GetDirectories(path);
            }
        }

        /// <summary>
        ///     Thread safe attribute setting method
        /// </summary>
        /// <param name="path">Path to the file</param>
        /// <param name="attributes">Attributes</param>
        public static void SetAttributes(string path, FileAttributes attributes)
        {
            lock (GetLockSemaphore(path))
            {
                if (File.Exists(path))
                    File.SetAttributes(path, attributes);
            }
        }

        /// <summary>
        ///     Method to retrieve the correct lock based on the path we are editing
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static object GetLockSemaphore(string path)
        {
            lock (SemaphoreLock)
            {
                var realPath = Path.HasExtension(path) ? Path.GetDirectoryName(path) : path;
                if (!string.IsNullOrEmpty(realPath))
                {
                    if (!LockSemaphore.TryGetValue(realPath, out var semaphore))
                    {
                        semaphore = new object();
                        LockSemaphore.Add(realPath, semaphore);
                    }
                    return semaphore;
                }
                throw new HandledException($"Bad folder/file path ({path})");
            }
        }
    }
}
