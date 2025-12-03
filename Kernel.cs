using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem;
using gotailsOS;

namespace gotailsOS
{
    using Cosmos.HAL.BlockDevice;
    using Cosmos.System.FileSystem;
    using Cosmos.System.FileSystem.VFS;
    public class Kernel : Sys.Kernel
    {
        CosmosVFS fs;
        protected override void BeforeRun()
        {
            try
            {
                Console.WriteLine("[ OK ] Booted");
                Console.WriteLine("[ ... ] Init VFS");
                fs = new CosmosVFS();
                Sys.FileSystem.VFS.VFSManager.RegisterVFS(fs);
                Console.WriteLine("[ OK ] Init VFS");
                foreach (var items in BlockDevice.Devices)
                {
                    var device = items;
                    Disk disk = new Disk(device);
                    disk.Mount();
                    for (int i = 0; i < disk.Partitions.Count; i++)
                    {
                        ManagedPartition part = disk.Partitions[i];

                        ulong sizeMB = (part.Host.BlockCount * part.Host.BlockSize) / (1024 * 1024);
                        Console.WriteLine(
                            $"Partition {i}: {sizeMB} MB | FS: {(part.HasFileSystem ? part.MountedFS.ToString() ?? "Unknown" : "None")} | Drive letter: {part.RootPath}"
                        );
                    }

                    // Console.WriteLine("Device: " + device.Model);
                }
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Welcome to GoTailsOS!");
                Console.ForegroundColor = ConsoleColor.White;
            }
            catch (Exception ex)
            {
                gotailsos.BugCheck.Bsod(ex);
            }

        }

        protected override void Run()
        {
            try
            {
                Console.Write("GoTailsOS " + CommandHandler.DisplayPath(CommandHandler.CurrentDirectory) + ">");
                string input = Console.ReadLine();

                CommandHandler.handleCommand(input);
            }
            catch (Exception ex)
            {
                gotailsos.BugCheck.Bsod(ex);
            }
        }
    }
}
