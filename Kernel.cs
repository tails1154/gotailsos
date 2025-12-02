using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.HAL.BlockDevice;
using testOS.FileSystem;
using Cosmos.System.Graphics;
using System.Drawing;

namespace testOS
{
    public class Kernel : Cosmos.System.Kernel
    {
        // Multi-drive support: map index (0,1,2,...) to TailsFS instance
        private Dictionary<int, TailsFS> drives = new Dictionary<int, TailsFS>();
        private int currentDrive = 0;
        private string currentDir = "/";
        private bool diskAvailable = false;

        private CommandInterpreter interpreter;

        // Utility to resolve and map N:/path style to drive and path
        public bool TryParseDrivePath(string input, out int driveNum, out string path)
        {
            driveNum = currentDrive;
            path = currentDir;
            if (string.IsNullOrEmpty(input)) return false;

            var parts = input.Split(new[] { ':' }, 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out driveNum))
            {
                path = parts[1];
                if (!path.StartsWith("/")) path = "/" + path;
                return drives.ContainsKey(driveNum);
            }

            // No drive prefix, use current drive and resolve relative path
            path = ResolvePath(input);
            return drives.ContainsKey(driveNum);
        }

        // Handles relative/absolute paths for current drive/dir
        private string ResolvePath(string input)
        {
            if (string.IsNullOrEmpty(input)) return currentDir;
            if (input.StartsWith("/")) return input;
            if (currentDir == "/") return "/" + input;
            return currentDir + "/" + input;
        }

        protected override void BeforeRun()
        {
            Console.WriteLine("[ OK ] Boot");

            drives.Clear();
            diskAvailable = false;

            if (BlockDevice.Devices == null || BlockDevice.Devices.Count == 0)
            {
                Console.WriteLine("[ERR] no disks");
                return;
            }

            int idx = 0;
            foreach (var device in BlockDevice.Devices)
            {
                if (device != null)
                {
                    var fs = new TailsFS(device);
                    drives[idx] = fs;
                    if (fs.Detect())
                        Console.WriteLine($"[ OK ] TailsFS detected on disk {idx}");
                    else
                        Console.WriteLine($"[ WARN ] No TailsFS present on disk {idx}. Type 'format {idx}:' to create.");
                }
                idx++;
            }

            if (drives.Count > 0)
            {
                diskAvailable = true;
                currentDrive = 0; // default
            }

            interpreter = new CommandInterpreter(
                drives,
                this,
                () => currentDrive,
                                                 () => currentDir,
                                                 (drive) => currentDrive = drive,
                                                 (dir) => currentDir = dir,
                                                 diskAvailable
            );
        }

        protected override void Run()
        {
            Console.Write($"GoTailsOS: {currentDrive}:{currentDir}> ");
            string line = Console.ReadLine();
            try
            {
                interpreter.Execute(line);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERR: " + ex.Message);
            }
        }
    }
}
