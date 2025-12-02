using System;
using System.Text;
using System.Collections.Generic;
using testOS.FileSystem;
namespace testOS
{
    public class CommandInterpreter
    {
        private readonly Dictionary<int, BadFS3> drives;
        private readonly Kernel kernel;
        private readonly Func<int> getCurrentDrive;
        private readonly Func<string> getCurrentDir;
        private readonly Action<int> setCurrentDrive;
        private readonly Action<string> setCurrentDir;
        private readonly bool diskAvailable;

        public CommandInterpreter(
            Dictionary<int, BadFS3> drives,
            Kernel kernel,
            Func<int> getCurrentDrive,
            Func<string> getCurrentDir,
            Action<int> setCurrentDrive,
            Action<string> setCurrentDir,
            bool diskAvailable)
        {
            this.drives = drives;
            this.kernel = kernel;
            this.getCurrentDrive = getCurrentDrive;
            this.getCurrentDir = getCurrentDir;
            this.setCurrentDrive = setCurrentDrive;
            this.setCurrentDir = setCurrentDir;
            this.diskAvailable = diskAvailable;
        }

        public void Execute(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0];
            var arg = parts.Length > 1 ? parts[1] : null;

            switch (cmd)
            {
                case "format":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }

                    if (arg != null && kernel.TryParseDrivePath(arg, out int fDrive, out _))
                    {
                        drives[fDrive].Format();
                        Console.WriteLine($"Formatted BadFS3 on disk {fDrive}.");
                    }
                    else
                    {
                        drives[getCurrentDrive()].Format();
                        Console.WriteLine($"Formatted BadFS3 on disk {getCurrentDrive()}.");
                    }
                    break;

                case "mkdir":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: mkdir <dir>"); break; }
                    if (kernel.TryParseDrivePath(arg, out int mDrive, out string mPath))
                    {
                        drives[mDrive].CreateDirectory(mPath);
                        Console.WriteLine("ok");
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;

                case "ls":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    int lDrive = getCurrentDrive();
                    string lPath = getCurrentDir();
                    if (arg != null && kernel.TryParseDrivePath(arg, out int parsedDrive, out string parsedPath))
                    {
                        lDrive = parsedDrive;
                        lPath = parsedPath;
                    }
                    var listing = drives[lDrive].ListDirectory(lPath);
                    if (listing == null || listing.Length == 0) Console.WriteLine("[empty]");
                    else foreach (var l in listing) Console.WriteLine(l);
                    break;

                case "write":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: write <file>"); break; }

                    if (kernel.TryParseDrivePath(arg, out int wDrive, out string wPath))
                    {
                        Console.WriteLine("Enter text:");
                        var t = Console.ReadLine() ?? "";
                        drives[wDrive].WriteFile(wPath, Encoding.UTF8.GetBytes(t));
                        Console.WriteLine("ok");
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;

                case "read":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: read <file>"); break; }
                    if (kernel.TryParseDrivePath(arg, out int rDrive, out string rPath))
                    {
                        Console.WriteLine(Encoding.UTF8.GetString(drives[rDrive].ReadFile(rPath)));
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;

                case "rm":
                case "rmdir":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: rm <path>"); break; }
                    if (kernel.TryParseDrivePath(arg, out int rmDrive, out string rmPath))
                    {
                        drives[rmDrive].Delete(rmPath);
                        Console.WriteLine("ok");
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;

                case "cd":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: cd <dir>"); break; }
                    if (kernel.TryParseDrivePath(arg, out int cdDrive, out string cdPath))
                    {
                        int idx = drives[cdDrive].ResolvePathPublic(cdPath);
                        if (idx < 0) { Console.WriteLine("ERR: not found"); break; }
                        if (!drives[cdDrive].IsDirectory(idx)) { Console.WriteLine("ERR: not a directory"); break; }
                        setCurrentDrive(cdDrive);
                        setCurrentDir(cdPath);
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;

                case "pwd":
                    Console.WriteLine($"{getCurrentDrive()}:{getCurrentDir()}");
                    break;
                case "edit":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: edit <file>"); break; }
                    if (kernel.TryParseDrivePath(arg, out int eDrive, out string ePath))
                    {
                        TextEditor.Run(drives[eDrive], ePath);
                    }
                    else
                    {
                        Console.WriteLine("ERR: invalid drive or path");
                    }
                    break;
                    // implement other commands (run, edit, echo, batch, help, etc.) in similar fashion...

                case "help":
                    Console.WriteLine("Available commands (multi-drive):");
                    Console.WriteLine("  help");
                    Console.WriteLine("  format [N:]");
                    Console.WriteLine("  mkdir <N:/dir>");
                    Console.WriteLine("  ls [N:/dir]");
                    Console.WriteLine("  write <N:/file>");
                    Console.WriteLine("  read <N:/file>");
                    Console.WriteLine("  rm <N:/path>");
                    Console.WriteLine("  cd <N:/dir>");
                    Console.WriteLine("  pwd");
                    Console.WriteLine("  edit <N:/file>");
                    // etc.
                    break;

                default:
                    Console.WriteLine("Command not found. Type 'help' for help.");
                    break;
            }
        }
    }
}
