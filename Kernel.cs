using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.HAL.BlockDevice;
using testOS.FileSystem;
using MiniGfx;
using Cosmos.System.Graphics;
using System.Drawing;
///using testOS.CommandInterpreter;

namespace testOS
{
    public class Kernel : Cosmos.System.Kernel
    {
        private BadFS3 fs;
        private BlockDevice disk;
        private bool diskAvailable = false; // true when we have a disk
        private string currentDir = "/";
        private string ResolvePath(string input)
        {
            if (string.IsNullOrEmpty(input))
                return currentDir;

            // If absolute path, return directly
            if (input.StartsWith("/"))
                return input;

            // If relative, join with currentDir
            if (currentDir == "/")
                return "/" + input;

            return currentDir + "/" + input;
        }
       // private Cosmos.System.Graphics.SVGAIICanvas canvas;
        private CommandInterpreter interpreter;


        protected override void BeforeRun()
        {
            Console.WriteLine("[ OK ] Boot");

            if (BlockDevice.Devices == null || BlockDevice.Devices.Count == 0)
            {
                Console.WriteLine("[ERR] no disks");
                diskAvailable = false;
                fs = null;
                disk = null;
                return;
            }

            disk = BlockDevice.Devices[0];
            if (disk == null)
            {
                Console.WriteLine("[ERR] selected disk is null");
                diskAvailable = false;
                fs = null;
                return;
            }

            fs = new BadFS3(disk);
            diskAvailable = true;

            if (fs.Detect()) Console.WriteLine("[ OK ] BadFS3 detected");
            else Console.WriteLine("[ WARN ] No BadFS3 present. Type 'format' to create. (DISK 0 WILL BE FORMATTED)");
            interpreter = new CommandInterpreter(
                fs,
                ResolvePath,
                newDir => currentDir = newDir,
                () => currentDir,
                                                 diskAvailable
            );
        }
        protected override void Run() {
            Console.Write($"GoTailsOS: {currentDir}> ");
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
