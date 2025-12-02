using System;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem;
using System.Collections.Generic;  // for Stack<T>
using System.Linq;                 // for Reverse()


namespace gotailsOS
{
    public static class CommandHandler
    {
        // Check if a directory exists
        private static bool DirectoryExists(CosmosVFS vfs, string path)
        {
            try
            {
                vfs.GetDirectory(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Check if a file exists
        private static bool FileExists(CosmosVFS vfs, string path)
        {
            try
            {
                vfs.GetFile(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string CurrentDirectory = "0:\\";

        public static void handleCommand(string command, Sys.FileSystem.CosmosVFS vfs)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLower();
            string[] args;
            if (parts.Length > 1)
            {
                args = new string[parts.Length - 1];
                Array.Copy(parts, 1, args, 0, parts.Length - 1);
            }
            else
            {
                args = Array.Empty<string>();
            }

            switch (cmd)
            {
                case "pwd":
                    Console.WriteLine(CurrentDirectory);
                    break;

                case "cd":
                    CmdCD(args, vfs);
                    break;

                case "ls":
                    CmdLS(args, vfs);
                    break;

                case "mkdir":
                    CmdMKDir(args, vfs);
                    break;

                case "touch":
                    CmdTouch(args, vfs);
                    break;

                case "rm":
                    CmdRM(args, vfs);
                    break;

                case "help":
                    PrintHelp();
                    break;
                case "fdisk":
                    fdisk.CmdFDiskInteractive(args);
                    break;

                default:
                    Console.WriteLine("Unknown command: " + cmd);
                    break;
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("GoTailsOS commands:");
            Console.WriteLine("  pwd            - Print working directory");
            Console.WriteLine("  cd <dir>       - Change directory");
            Console.WriteLine("  ls <dir?>      - List directory contents");
            Console.WriteLine("  mkdir <dir>    - Create directory");
            Console.WriteLine("  touch <file>   - Create file");
            Console.WriteLine("  rm <file/dir>  - Delete file or directory");
            Console.WriteLine("  fdisk <drive>  - Open a partition manager");
        }

        //-------------------------------------------------------------------------------------------------------
        // PATH RESOLUTION
        //-------------------------------------------------------------------------------------------------------
        private static string NormalizeInputPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.Replace('/', '\\');
        }

        public static string DisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.Replace('\\', '/');
        }

        private static string Resolve(string path)
        {
            // Convert user-friendly "/" to proper "\" for VFS
            path = NormalizeInputPath(path);

            if (string.IsNullOrWhiteSpace(path))
                return CurrentDirectory;

            string fullPath;

            // Absolute drive path: 0:\, 1:\, etc.
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
            {
                fullPath = path;
            }
            // Absolute on current drive: \folder\file
            else if (path.StartsWith("\\"))
            {
                string drive = CurrentDirectory.Split(':')[0];
                fullPath = drive + ":" + path;
            }
            // Relative path
            else
            {
                fullPath = CurrentDirectory;
                if (!fullPath.EndsWith("\\"))
                    fullPath += "\\";
                fullPath += path;
            }

            // Normalize . and ..
            var segments = fullPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();

            // Keep the drive letter as first element
            stack.Push(segments[0]);

            for (int i = 1; i < segments.Length; i++)
            {
                if (segments[i] == ".")
                {
                    // Current directory → do nothing
                    continue;
                }
                else if (segments[i] == "..")
                {
                    // Parent directory → pop last if not the drive
                    if (stack.Count > 1)
                        stack.Pop();
                }
                else
                {
                    stack.Push(segments[i]);
                }
            }

            // Rebuild path with trailing \
            var normalizedSegments = stack.Reverse().ToArray();
            string normalized = string.Join("\\", normalizedSegments) + "\\";

            return normalized;
        }

        //-------------------------------------------------------------------------------------------------------
        // COMMANDS
        //-------------------------------------------------------------------------------------------------------
        private static string NormalizePath(string path, string currentDirectory)
        {
            if (string.IsNullOrWhiteSpace(path))
                return currentDirectory;

            // Convert user input / to \
            path = path.Replace('/', '\\').Trim();

            string fullPath;

            // Absolute drive path: 0:\ or C:\ etc.
            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
            {
                fullPath = path;
            }
            else if (path.StartsWith("\\")) // absolute on current drive
            {
                string drive = currentDirectory.Split(':')[0];
                fullPath = drive + ":" + path;
            }
            else
            {
                // Relative path
                fullPath = currentDirectory;
                if (!fullPath.EndsWith("\\"))
                    fullPath += "\\";
                fullPath += path;
            }

            // Split into segments
            var segments = fullPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();

            // Keep the drive as first element (e.g. 0:)
            stack.Push(segments[0]);

            for (int i = 1; i < segments.Length; i++)
            {
                if (segments[i] == ".")
                {
                    // Current directory → do nothing
                    continue;
                }
                else if (segments[i] == "..")
                {
                    // Parent directory → pop last if not the drive
                    if (stack.Count > 1)
                        stack.Pop();
                }
                else
                {
                    stack.Push(segments[i]);
                }
            }

            // Rebuild path
            var normalizedSegments = stack.Reverse().ToArray();
            string normalized = string.Join("\\", normalizedSegments) + "\\"; // keep trailing \

            return normalized;
        }

        private static void CmdCD(string[] args, CosmosVFS vfs)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(CurrentDirectory);
                return;
            }

            string target = Resolve(args[0]);
            string oldCurDir = CurrentDirectory;
            try
            {
                var dir = vfs.GetDirectory(target);
                CurrentDirectory = dir.mFullPath.EndsWith("\\") ? dir.mFullPath : dir.mFullPath + "\\";
                if (CurrentDirectory == "\\") { throw new Exception("Nonexistant directory"); }
            }
            catch
            {
                Console.WriteLine("cd: no such directory");
                CurrentDirectory = oldCurDir;
            }
        }

        private static void CmdLS(string[] args, CosmosVFS vfs)
        {
            string target = args.Length > 0 ? Resolve(args[0]) : CurrentDirectory;

            try
            {
                var listing = vfs.GetDirectoryListing(target);
                foreach (var entry in listing)
                {
                    bool isDir = entry.mEntryType == DirectoryEntryTypeEnum.Directory;
                    Console.WriteLine(entry.mName + (isDir ? "\\" : ""));
                }
            }
            catch
            {
                Console.WriteLine("ls: cannot access '" + target + "'");
            }
        }

        private static void CmdMKDir(string[] args, CosmosVFS vfs)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("mkdir: missing operand");
                return;
            }

            string path = Resolve(args[0]);
            try
            {
                vfs.CreateDirectory(path);
            }
            catch
            {
                Console.WriteLine("mkdir: cannot create '" + args[0] + "'");
            }
        }

        private static void CmdTouch(string[] args, CosmosVFS vfs)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("touch: missing filename");
                return;
            }

            string path = Resolve(args[0]);
            try
            {
                vfs.CreateFile(path);
            }
            catch
            {
                Console.WriteLine("touch: cannot create '" + args[0] + "'");
            }
        }

        private static void CmdRM(string[] args, CosmosVFS vfs)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("rm: missing operand");
                return;
            }

            string path = Resolve(args[0]);

            try
            {
                if (FileExists(vfs, path))
                {
                    var fileEntry = vfs.GetFile(path);
                    vfs.DeleteFile(fileEntry);
                }
                else if (DirectoryExists(vfs, path))
                {
                    DeleteDirectoryRecursive(vfs, vfs.GetDirectory(path));
                }
                else
                {
                    Console.WriteLine("rm: no such file or directory");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("rm: error - " + e.Message);
            }
        }

        //-------------------------------------------------------------------------------------------------------
        // Recursive delete
        //-------------------------------------------------------------------------------------------------------
        private static void DeleteDirectoryRecursive(CosmosVFS vfs, DirectoryEntry dir)
        {
            try
            {
                var listing = vfs.GetDirectoryListing(dir);
                foreach (var entry in listing)
                {
                    if (entry.mEntryType == DirectoryEntryTypeEnum.Directory)
                        DeleteDirectoryRecursive(vfs, entry);
                    else
                        vfs.DeleteFile(entry);
                }
                vfs.DeleteDirectory(dir);
            }
            catch (Exception e)
            {
                Console.WriteLine("rm: error deleting directory '" + dir.mFullPath + "' - " + e.Message);
            }
        }
    }
}
