using System;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem;
using System.Net.Sockets;
using Cosmos.HAL.BlockDevice;
using System.Configuration.Assemblies;
using Cosmos.HAL;
using System.Runtime.CompilerServices;


namespace gotailsOS
{
    public static class fdisk
    {

        public static void CmdFDiskInteractive(string[] args)
        {
            Console.WriteLine("Fdisk starting up");
            if (args.Length < 1 || !int.TryParse(args[0], out int diskIndex))
            {
                Console.WriteLine("Usage: fdisk <diskIndex>");
                return;
            }
            Console.WriteLine("Getting disk " + diskIndex);

            var disks = new Disk(BlockDevice.Devices[diskIndex]);

            Console.WriteLine("Got disk");
            var disk = disks;

            Console.WriteLine();
            Console.WriteLine($"gotailsOS fdisk (Failure Disk) for Disk {diskIndex}");
            Console.WriteLine("Changes will be written as soon as you confirm them.");
            Console.WriteLine("To exit, send w or q");
            Console.WriteLine();

            bool running = true;
            bool modified = false;

            while (running)
            {
                Console.Write("Command (m for help): ");
                string cmd = Console.ReadLine().Trim();

                switch (cmd)
                {
                    case "m":
                        PrintFdiskHelp();
                        break;

                    case "p":
                        PrintPartitionTable(disk);
                        break;

                    case "n":
                        CreatePartitionInteractive(disk, ref modified);
                        break;

                    case "d":
                        DeletePartitionInteractive(disk, ref modified);
                        break;

                    case "w":
                        Console.WriteLine("Writing changes...");
                        // Cosmos API applies changes immediately anyway
                        running = false;
                        break;

                    case "q":
                        Console.WriteLine("Quitting without saving changes.");
                        // We can't revert, but Linux fdisk pretends to
                        running = false;
                        break;
                    case "g":
                        DeleteAll(disk);
                        break;
                    case "f":
                        FormatPartition(disk);
                        break;
                    case "o":
                        MountPartition(disk);
                        break;

                    default:
                        Console.WriteLine("Unknown command. Type 'm' for help.");
                        break;
                }
            }
        }
        private static void DeleteAll(Disk vfs)
        {
            vfs.Clear();
            Console.WriteLine("Cleared Partition table");
        }
        private static void MountPartition(Disk vfs)
        {
            PrintPartitionTable(vfs);
            Console.WriteLine();
            Console.WriteLine("Enter partition to mount, or a for all");
            Console.WriteLine();
            string input = Console.ReadLine().Trim();
            try
            {
                if (input == "a")
                {
                    Console.WriteLine("Mounting all partitions");
                    vfs.Mount();
                    Console.WriteLine("Mounted!");
                    return;
                }
                int partnum = int.Parse(input);
                Console.WriteLine("Mounting");
                vfs.MountPartition(partnum);
                Console.WriteLine("Mounted!");
            }
            catch (Exception ex)
            {
                ex.ToString(); // i hate the "unused variable" warning lol
                Console.WriteLine("Thats either not a valid partition, or you forgot what a number was and need to go back to PreK (joking)");
            }
        }
        private static void FormatPartition(Disk vfs)
        {
            PrintPartitionTable(vfs);
            Console.WriteLine();
            Console.WriteLine("Enter a partition to format");
            Console.WriteLine();
            string input = Console.ReadLine().Trim();
            try
            {
                int partnum = int.Parse(input);
                Console.WriteLine("Formatting");
                vfs.FormatPartition(partnum, "FAT", true);
                Console.WriteLine("Formatted!");
            }
            catch (Exception ex)
            {
                ex.ToString(); // so it wont complain about me not using it
                Console.WriteLine("Thats either not a valid partition, or you forgot what a number was and need to go back to PreK /j");
            }
        }
        private static void PrintFdiskHelp()
        {
            Console.WriteLine();
            Console.WriteLine("Help:");
            Console.WriteLine();
            Console.WriteLine("  d   delete a partition");
            Console.WriteLine("  n   add a new partition");
            Console.WriteLine("  p   print the partition table");
            Console.WriteLine("  m   print this menu");
            Console.WriteLine("  w   write table to disk and exit");
            Console.WriteLine("  q   quit without saving changes");
            Console.WriteLine("  g   new partition table");
            Console.WriteLine("  f   format partition");
            Console.WriteLine("  o   mount partition");
            Console.WriteLine();
        }
        private static void PrintPartitionTable(Cosmos.System.FileSystem.Disk disk)
        {
            Console.WriteLine();
            Console.WriteLine("Partition table:");
            Console.WriteLine("------------------------------------------------");

            if (disk.Partitions.Count == 0)
            {
                Console.WriteLine("No partitions on this disk.");
                return;
            }

            for (int i = 0; i < disk.Partitions.Count; i++)
            {
                ManagedPartition part = disk.Partitions[i];

                ulong sizeMB = (part.Host.BlockCount * part.Host.BlockSize) / (1024 * 1024);

                Console.WriteLine(
                    $"Partition {i}: {sizeMB} MB | FS: {(part.HasFileSystem ? part.LimitFS ?? "Unknown" : "None")}"
                );
            }

            Console.WriteLine();
        }

        private static void CreatePartitionInteractive(Cosmos.System.FileSystem.Disk disk, ref bool modified)
        {
            Console.Write("Partition size (MB) (Total Space: " + disk.Size + "): ");
            string input = Console.ReadLine().Trim();

            if (!int.TryParse(input, out int size))
            {
                Console.WriteLine("Invalid size.");
                return;
            }

            try
            {
                disk.CreatePartition(size);
                modified = true;
                Console.WriteLine($"Created {size} MB partition.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error creating partition: " + e.Message);
            }
        }
        private static void DeletePartitionInteractive(Cosmos.System.FileSystem.Disk disk, ref bool modified)
        {
            Console.Write("Partition number: ");
            string input = Console.ReadLine().Trim();

            if (!int.TryParse(input, out int index))
            {
                Console.WriteLine("Invalid index.");
                return;
            }

            if (index < 0 || index >= disk.Partitions.Count)
            {
                Console.WriteLine("Partition does not exist.");
                return;
            }

            try
            {
                disk.DeletePartition(index);
                modified = true;
                Console.WriteLine($"Deleted partition {index}.");
            }
            catch (Exception e)
            {
                Console.WriteLine("Error deleting partition: " + e.Message);
            }
        }
    }
}