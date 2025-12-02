using System;
using System.Text;
using testOS;
using testOS.FileSystem;
namespace testOS
{
    public class CommandInterpreter
    {
        private readonly BadFS3 fs;
        private readonly Func<string, string> resolvePath;
        private readonly Action<string> setCurrentDir;
        private readonly Func<string> getCurrentDir;
        private readonly bool diskAvailable;

        public CommandInterpreter(
            BadFS3 fs,
            Func<string, string> resolvePath,
            Action<string> setCurrentDir,
            Func<string> getCurrentDir,
            bool diskAvailable)
        {
            this.fs = fs;
            this.resolvePath = resolvePath;
            this.setCurrentDir = setCurrentDir;
            this.getCurrentDir = getCurrentDir;
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
                    fs.Format();
                    Console.WriteLine("Formatted BadFS3.");
                    break;

                case "mkdir":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: mkdir <dir>"); break; }
                    fs.CreateDirectory(resolvePath(arg));
                    Console.WriteLine("ok");
                    break;

                case "ls":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    var target = arg != null ? resolvePath(arg) : getCurrentDir();
                    var listing = fs.ListDirectory(target);
                    if (listing == null || listing.Length == 0) Console.WriteLine("[empty]");
                    else foreach (var l in listing) Console.WriteLine(l);
                    break;

                case "write":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: write <file>"); break; }
                    var writePath = resolvePath(arg);
                    Console.WriteLine("Enter text:");
                    var t = Console.ReadLine() ?? "";
                    fs.WriteFile(writePath, Encoding.UTF8.GetBytes(t));
                    Console.WriteLine("ok");
                    break;

                case "read":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: read <file>"); break; }
                    Console.WriteLine(Encoding.UTF8.GetString(fs.ReadFile(resolvePath(arg))));
                    break;

                case "rm":
                case "rmdir":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: rm <path>"); break; }
                    fs.Delete(resolvePath(arg));
                    Console.WriteLine("ok");
                    break;

                case "cd":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: cd <dir>"); break; }

                    var path = resolvePath(arg);
                    int idx = fs.ResolvePathPublic(path);

                    if (idx < 0) { Console.WriteLine("ERR: not found"); break; }
                    if (!fs.IsDirectory(idx)) { Console.WriteLine("ERR: not a directory"); break; }

                    setCurrentDir(path);
                    break;

                case "pwd":
                    Console.WriteLine(getCurrentDir());
                    break;

                case "run":
                    if (!diskAvailable) { Console.WriteLine("ERR: no disk"); break; }
                    if (arg == null) { Console.WriteLine("usage: run <file>"); break; }

                    var filePath = resolvePath(arg);
                    var exeData = fs.ReadFile(filePath);

                    Console.WriteLine($"[ RUN ] {filePath}");
                    testOS.Exec.BadExecLoader.RunExecutable(exeData, fs);

                    var guestOut = testOS.Exec.BadExecLoader.GetGuestOutput();
                    if (!string.IsNullOrEmpty(guestOut))
                    {
                        Console.WriteLine();
                        Console.WriteLine("[ GUEST OUTPUT ]");
                        Console.WriteLine(guestOut);
                    }

                    Console.WriteLine("[ OK ] program finished");
                    break;

                case "edit":
                    if (arg == null) Console.WriteLine("usage: edit <file>");
                    else TextEditor.Run(fs, resolvePath(arg));
                    break;
                case "echo":
                    if (arg == null)
                    {
                        Console.WriteLine("usafe: echo <text>");
                    } else {
                        Console.WriteLine(arg);
                    }
                    break;
                case "batch":
                    if (arg == null)
                    {
                        Console.WriteLine("usage: batch <file>");
                        break;
                    }

                    try
                    {
                        var pathh = resolvePath(arg);
                        var textt = Encoding.UTF8.GetString(fs.ReadFile(pathh));

                        foreach (var linee in textt.Replace("\r", "").Split('\n'))
                        {
                            if (!string.IsNullOrWhiteSpace(linee))
                                Execute(linee);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("ERR: " + ex.Message);
                    }
                    break;

                case "help":
                    Console.WriteLine("Available commands:");
                    Console.WriteLine("  help");
                    Console.WriteLine("  format");
                    Console.WriteLine("  mkdir <dir>");
                    Console.WriteLine("  ls [dir]");
                    Console.WriteLine("  write <file>");
                    Console.WriteLine("  read <file>");
                    Console.WriteLine("  rm <path>");
                    Console.WriteLine("  cd <dir>");
                    Console.WriteLine("  pwd");
                    Console.WriteLine("  run <file>");
                    Console.WriteLine("  edit <file>");
                    Console.WriteLine("  echo <text>");
                    Console.WriteLine("  batch <file>");
                    break;

                default:
                    Console.WriteLine("Command not found. Type 'help' for help.");
                    break;
            }
        }
    }
}
