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
    class InstallWizard
    {
        public static void StartInstallWizard(CosmosVFS vfs)
        {
            Console.Clear();
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Clear();
            Console.WriteLine("gotailsos Setup");
            Console.WriteLine("================");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Welcome to the gotailsOS Install Wizard!");
            Console.WriteLine("This wizard will guide you through the installation process.");
            Console.WriteLine();

            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write("\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\nENTER = Continue F3 = Exit Setup                                               ");
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            while (true)
            {
                DateTime end = DateTime.Now.AddSeconds(1);
                while (DateTime.Now < end)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            Console.Clear();
                            Console.BackgroundColor = ConsoleColor.Blue;
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.Clear();
                            Console.WriteLine("gotailsos Setup");
                            Console.WriteLine("================");
                            Console.ResetColor();
                            Console.BackgroundColor = ConsoleColor.Blue;
                            Console.WriteLine("Finding source device");
                            string rootpath = null;
                            foreach (var device in BlockDevice.Devices)
                            {
                                Console.WriteLine("Checking device " + (BlockDevice.Devices.IndexOf(device) + 1) + " of " + BlockDevice.Devices.Count);
                                Disk p = new Disk(device);
                                foreach (var part in p.Partitions)
                                {
                                    Console.WriteLine("  Checking partition " + (p.Partitions.IndexOf(part) + 1) + " of " + p.Partitions.Count);
                                    if (part.HasFileSystem && part.MountedFS.Label == "ISOIMAGE")
                                    {
                                        Console.WriteLine("Source device found: " + part.MountedFS.RootPath);
                                        rootpath = part.MountedFS.RootPath;
                                    }
                                }
                            }
                            if (rootpath == null)
                            {
                                Console.Clear();
                                Console.BackgroundColor = ConsoleColor.Blue;
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.Clear();
                                Console.WriteLine("gotailsos Setup");
                                Console.WriteLine("================");
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Error: Install media not found!");
                                Console.WriteLine("Please enter the drive path to install from (e.g., 0:\\): ");
                                rootpath = Console.ReadLine();
                            }
                            Console.Clear();
                            Console.BackgroundColor = ConsoleColor.Blue;
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.Clear();
                            Console.WriteLine("gotailsos Setup");
                            Console.WriteLine("================");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("Where would you like to install gotailsOS?");
                            Console.WriteLine("Use your arrow keys to navigate and press ENTER to select a partition:");
                            int selectedIndex = 0;
                            List<string> partitions = new List<string>();
                            BlockDevice targetDevice = null;
                            foreach (var device in BlockDevice.Devices)
                            {
                                Disk p = new Disk(device);
                                foreach (var part in p.Partitions)
                                {
                                    partitions.Add(part.MountedFS.RootPath + " - " + part.MountedFS.Label + " - " + part.MountedFS.Size + " bytes");

                                }
                            }

                            while (true)
                            {
                                Console.Clear();
                                Console.BackgroundColor = ConsoleColor.Blue;
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.WriteLine("gotailsos Setup");
                                Console.WriteLine("================");
                                Console.ForegroundColor = ConsoleColor.White;
                                Console.WriteLine("Where would you like to install gotailsOS?");
                                Console.WriteLine("Use your arrow keys to navigate and press ENTER to select a partition:");
                                Console.WriteLine();

                                for (int i = 0; i < partitions.Count; i++)
                                {
                                    if (i == selectedIndex)
                                    {
                                        Console.BackgroundColor = ConsoleColor.White;
                                        Console.ForegroundColor = ConsoleColor.Black;
                                    }
                                    Console.WriteLine(partitions[i]);
                                    Console.BackgroundColor = ConsoleColor.Blue;
                                    Console.ForegroundColor = ConsoleColor.White;
                                }

                                var key2 = Console.ReadKey(true);
                                if (key2.Key == ConsoleKey.DownArrow)
                                {
                                    selectedIndex = (selectedIndex + 1) % partitions.Count;
                                }
                                else if (key2.Key == ConsoleKey.UpArrow)
                                {
                                    selectedIndex = (selectedIndex - 1 + partitions.Count) % partitions.Count;
                                }
                                else if (key2.Key == ConsoleKey.Enter)
                                {
                                    targetDevice = partitions.Select((part, index) => new { part, index })
                                        .Where(x => x.index == selectedIndex)
                                        .Select(x =>
                                        {
                                            foreach (var device in BlockDevice.Devices)
                                            {
                                                Disk disk = new Disk(device);
                                                foreach (var part in disk.Partitions)
                                                {
                                                    string desc = part.MountedFS.RootPath + " - " + part.MountedFS.Label + " - " + part.MountedFS.Size + " bytes";
                                                    if (desc == x.part)
                                                    {
                                                        return device;
                                                    }
                                                }
                                            }
                                            return null;
                                        })
                                        .FirstOrDefault();
                                    break;
                                }
                            }
                            Console.Clear();
                            Console.BackgroundColor = ConsoleColor.Blue;
                            Console.ForegroundColor = ConsoleColor.DarkCyan;
                            Console.Clear();
                            Console.WriteLine("gotailsos Setup");
                            Console.WriteLine("================");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine("Installing gotailsOS...");
                            Console.WriteLine("Writing MBR");
                            MBR mbr = new MBR(targetDevice);
                            mbr.CreateMBR(targetDevice);
                            Console.WriteLine("Formatting partition");
                            try
                            {
                                CosmosVFS fs = new CosmosVFS();
                                Disk disk = new Disk(targetDevice);
                                var part = disk.Partitions[0];
                                //       part.MountedFS.Format("FAT32", true);
                                Console.WriteLine("Copying files...");
                                string targetRoot = part.MountedFS.GetRootDirectory().ToString();
                                Directory.CreateDirectory(targetRoot + "boot");
                                File.Copy(rootpath + "boot\\gotailsos.bin.gz", targetRoot + "boot\\gotailsos.bin.gz", true);
                                File.Copy(rootpath + "boot\\limine-bios-cd.bin", targetRoot + "boot\\limine-bios-cd.bin", true);
                                File.Copy(rootpath + "boot\\limine-bios.sys", targetRoot + "boot\\limine-bios.sys", true);
                                File.Copy(rootpath + "boot\\limine.cfg", targetRoot + "boot\\limine.cfg", true);
                                File.Copy(rootpath + "boot\\limine-uefi-cd.bin", targetRoot + "boot\\limine-uefi-cd.bin", true);
                                File.Copy(rootpath + "boot\\liminewp.bmp", targetRoot + "boot\\liminewp.bmp", true);
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Installation failed: " + ex.Message);
                                Console.WriteLine("Press any key to exit.");
                                Console.ReadKey();
                                return;
                            }
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("Installation complete!");
                            Console.WriteLine("Press any key to reboot.");
                            Console.ReadKey();
                            Sys.Power.Reboot();
                            return;
                        }
                        else if (key.Key == ConsoleKey.F3)
                        {
                            Console.Clear();
                            Console.WriteLine("Exiting setup. No changes were made.");
                            return;
                        }
                    }
                }
            }
        }
    }
}