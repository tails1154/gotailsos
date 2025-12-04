using System;
using Sys = Cosmos.System;
using System.Collections.Generic;  // for Stack<T>
using System.Linq;
using System.IO;
using gotailsos;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;
using System.Security.Cryptography.X509Certificates;
using Cosmos.HAL.BlockDevice;

namespace gotailsos
{
    public static class CommandHandler
    {
        public static string CurrentDirectory = "0:\\";
        static CosmosVFS fs;
        public static void handleCommand(string command)
        {
            try
            {
                fs = new CosmosVFS();
                Sys.FileSystem.VFS.VFSManager.RegisterVFS(fs);
            }
            catch { }
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
                    CmdCD(args);
                    break;

                case "ls":
                    CmdLS(args);
                    break;

                case "mkdir":
                    CmdMKDir(args);
                    break;

                case "touch":
                    CmdTouch(args);
                    break;

                case "rm":
                    CmdRM(args);
                    break;

                case "help":
                    PrintHelp();
                    break;
                case "fdisk":
                    fdisk.CmdFDiskInteractive(args);
                    break;
                case "install":
                    InstallWizard.StartInstallWizard(fs);
                    break;
                case "textedit":
                    if (args.Length == 0)
                    {
                        Console.WriteLine("textedit: missing filename");
                    }
                    else
                    {
                        gotailsos.TextEdit.OpenNano(Resolve(args[0]), fs);
                    }
                    break;
                case "reboot":
                    Sys.Power.Reboot();
                    break;
                case "shutdown":
                    Sys.Power.Shutdown();
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
            Console.WriteLine("  textedit <file>- Open text editor");
            Console.WriteLine("  reboot         - Reboot the system");
            Console.WriteLine("  shutdown       - Shutdown the system");
            Console.WriteLine("  install        - Open the OS Install Wizard");
            Console.WriteLine("  help           - Seems like you know how to use it");
        }

        //-------------------------------------------------------------------------------------------------------
        // PATH RESOLUTION
        //-------------------------------------------------------------------------------------------------------
        private static string NormalizeInputPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.Replace('/', '\\').Trim();
        }

        public static string DisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            return path.Replace('\\', '/');
        }

        // Resolve a path to an absolute VFS path. Returns paths like "0:\\" (root) or "0:\\dir\\file.txt" (no trailing slash for files).
        private static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return CurrentDirectory;

            path = NormalizeInputPath(path);

            // Helper to normalize segments while preserving drive
            string NormalizeSegments(string full)
            {
                var parts = full.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    return CurrentDirectory;

                var stack = new Stack<string>();
                // first part should be drive like "0:" or "C:"
                stack.Push(parts[0]);

                for (int i = 1; i < parts.Length; i++)
                {
                    var seg = parts[i];
                    if (seg == ".") continue;
                    if (seg == "..")
                    {
                        if (stack.Count > 1) stack.Pop();
                        continue;
                    }
                    stack.Push(seg);
                }

                var arr = stack.Reverse().ToArray();
                if (arr.Length == 1)
                    return arr[0] + "\\"; // drive root
                return arr[0] + "\\" + string.Join("\\", arr.Skip(1));
            }

            // Absolute with explicit drive: e.g. "0:\\foo"
            if (path.Length >= 2 && path[1] == ':')
            {
                return NormalizeSegments(path);
            }

            // Absolute on current drive (starts with "\\") => prepend current drive
            if (path.StartsWith("\\"))
            {
                var drive = CurrentDirectory.Length >= 2 ? CurrentDirectory.Substring(0, 1) : "0";
                return NormalizeSegments(drive + ":" + path);
            }

            // Relative -> combine with current directory (CurrentDirectory expected to be directory, may end with \
            var baseDir = CurrentDirectory;
            if (!baseDir.EndsWith("\\")) baseDir += "\\";
            return NormalizeSegments(baseDir + path);
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

        private static void CmdCD(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(CurrentDirectory);
                return;
            }

            string target = Resolve(args[0]);
            try
            {
                if (VFSManager.DirectoryExists(target))
                {
                    CurrentDirectory = target.EndsWith("\\") ? target : target + "\\";
                }
                else
                {
                    Console.WriteLine("cd: no such directory");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("cd: error - " + ex.Message);
            }
        }

        private static void CmdLS(string[] args)
        {
            string target = args.Length > 0 ? Resolve(args[0]) : CurrentDirectory;

            try
            {
                if (!VFSManager.DirectoryExists(target))
                {
                    Console.WriteLine("ls: cannot access '" + target + "'");
                    return;
                }

                // List directories
                try
                {
                    var dirs = VFSManager.GetDirectoryListing(target);
                    foreach (var dir in dirs)
                    {
                        var name = dir.mName;
                        if (dir.mEntryType == Sys.FileSystem.Listing.DirectoryEntryTypeEnum.Directory)
                        {
                            //name += "";
                            Console.WriteLine("<DIR> " + name);
                        }
                        else
                        {
                            Console.WriteLine(name);
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ls: cannot access '" + target + "'");
                Console.WriteLine("Exception: " + ex.Message);
            }
        }

        private static void CmdMKDir(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("mkdir: missing operand");
                return;
            }

            string path = Resolve(args[0]);
            Console.WriteLine(path);
            try
            {
                VFSManager.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine("mkdir: cannot create '" + args[0] + "': " + ex.Message);
            }
        }

        private static void CmdTouch(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("touch: missing filename");
                return;
            }

            string path = Resolve(args[0]);

            try
            {
                if (!VFSManager.FileExists(path))
                {
                }
                else
                {
                    Console.WriteLine("touch: cannot create '" + args[0] + "': File already exists");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"touch: cannot create '{args[0]}': {ex.Message}");
            }
        }

        private static void CmdRM(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("rm: missing operand");
                return;
            }

            string path = Resolve(args[0]);

            try
            {
                if (VFSManager.FileExists(path))
                {
                    VFSManager.DeleteFile(path);
                }
                else if (VFSManager.DirectoryExists(path))
                {
                    VFSManager.DeleteDirectory(path, true);  // recursive delete
                }
                else
                {
                    Console.WriteLine("rm: no such file or directory");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("rm: error - " + ex.Message);
            }
        }
    }
}
