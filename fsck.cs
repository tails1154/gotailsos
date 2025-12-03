using System;
using System.Linq;
using Sys = Cosmos.System;
using Cosmos.HAL.BlockDevice;
using Cosmos.System.FileSystem;
using Cosmos.System.FileSystem.VFS;
using System.IO;

namespace gotailsos
{
    public static class fsck
    {
        // Entry point: fsck [diskIndex] [partitionIndex] [--repair]
        public static void CmdFsckInteractive(string[] args)
        {
            try
            {
                if (args == null || args.Length == 0)
                {
                    ScanAllDisks();
                    return;
                }

                if (!int.TryParse(args[0], out int diskIndex) || diskIndex < 0 || diskIndex >= BlockDevice.Devices.Count)
                {
                    Console.WriteLine("Invalid disk index. Use 'fsck' to list disks.");
                    return;
                }

                var disk = new Disk(BlockDevice.Devices[diskIndex]);
                disk.Mount();

                if (args.Length == 1)
                {
                    // Scan all partitions on disk
                    ScanDiskPartitions(disk, diskIndex);
                    return;
                }

                if (!int.TryParse(args[1], out int partIndex) || partIndex < 0 || partIndex >= disk.Partitions.Count)
                {
                    Console.WriteLine("Invalid partition index. Use 'fsck <disk>' to list partitions.");
                    return;
                }

                bool repair = args.Any(a => a == "--repair" || a == "-r");

                CheckPartition(disk, diskIndex, partIndex, repair);
            }
            catch (Exception ex)
            {
                Console.WriteLine("fsck: unexpected error: " + ex.Message);
            }
        }

        private static void ScanAllDisks()
        {
            Console.WriteLine("Scanning all block devices and partitions...");
            for (int i = 0; i < BlockDevice.Devices.Count; i++)
            {
                var dev = BlockDevice.Devices[i];
                Console.WriteLine($"Disk {i}: Blocks: {dev.BlockCount} | BlockSize: {dev.BlockSize}");
                try
                {
                    var disk = new Disk(dev);
                    disk.Mount();
                    PrintPartitionTable(disk);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  Could not read partitions: " + ex.Message);
                }
            }
        }

        private static void ScanDiskPartitions(Disk disk, int diskIndex)
        {
            Console.WriteLine($"Partitions for disk {diskIndex}:");
            PrintPartitionTable(disk);
        }

        private static void PrintPartitionTable(Disk disk)
        {
            if (disk.Partitions.Count == 0)
            {
                Console.WriteLine("  No partitions.");
                return;
            }

            for (int i = 0; i < disk.Partitions.Count; i++)
            {
                var part = disk.Partitions[i];
                ulong sizeMB = (part.Host.BlockCount * part.Host.BlockSize) / (1024 * 1024);
                Console.WriteLine($"  [{i}] Size: {sizeMB} MB | HasFS: {part.HasFileSystem} | MountedFS: {(part.HasFileSystem ? part.MountedFS?.ToString() ?? "Unknown" : "None")} | RootPath: {part.RootPath}");
            }
        }

        private static void CheckPartition(Disk disk, int diskIndex, int partIndex, bool repair)
        {
            var part = disk.Partitions[partIndex];
            Console.WriteLine($"Checking disk {diskIndex} partition {partIndex}...");
            Console.WriteLine($"  Host blocks: {part.Host.BlockCount}, BlockSize: {part.Host.BlockSize}");
            Console.WriteLine($"  HasFileSystem: {part.HasFileSystem}");
            Console.WriteLine($"  RootPath: {part.RootPath}");

            // Attempt to mount partition if not mounted
            try
            {
                if (!part.HasFileSystem)
                {
                    Console.WriteLine("  No filesystem detected on this partition.");
                    if (repair)
                    {
                        Console.WriteLine("  Repair requested: formatting as FAT32 (destructive). Proceed? (y/N)");
                        var k = Console.ReadKey(true);
                        if (k.Key == ConsoleKey.Y)
                        {
                            disk.FormatPartition(partIndex, "FAT32", true);
                            Console.WriteLine("  Formatted as FAT32.");
                        }
                        else Console.WriteLine("  Skipped formatting.");
                    }
                    return;
                }

                Console.WriteLine("  Attempting to mount partition...");
                disk.MountPartition(partIndex);
                // Refresh partition reference
                part = disk.Partitions[partIndex];
                Console.WriteLine($"  Mounted. RootPath: {part.RootPath}");

                try
                {
                    var root = part.RootPath ?? string.Empty;
                    // Ensure trailing backslash (Cosmos uses forms like "3:\")
                    if (!string.IsNullOrWhiteSpace(root) && !root.EndsWith("\\"))
                    {
                        root = root + "\\";
                    }

                    Console.WriteLine("  Attempting to access root path: " + (string.IsNullOrWhiteSpace(root) ? "(none)" : root));

                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                    {
                        Console.WriteLine("  Listing root directory:");
                        try
                        {
                            var dirs = Directory.GetDirectories(root);
                            foreach (var d in dirs)
                            {
                                var name = Path.GetFileName(d);
                                Console.WriteLine("    " + name + "\\");
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("    Could not list directories: " + e.Message);
                        }

                        try
                        {
                            var files = Directory.GetFiles(root);
                            foreach (var f in files)
                            {
                                var name = Path.GetFileName(f);
                                Console.WriteLine("    " + name);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("    Could not list files: " + e.Message);
                        }
                    }
                    else
                    {
                        Console.WriteLine("  RootPath not accessible or not set for this partition.");
                    }
                }
                catch (Exception exEnum)
                {
                    Console.WriteLine("  Filesystem enumeration failed: " + exEnum.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  Error while checking partition: " + ex.Message);
                if (repair)
                {
                    Console.WriteLine("  Repair requested: attempting to format as FAT32 (destructive). Proceed? (y/N)");
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Y)
                    {
                        try
                        {
                            disk.FormatPartition(partIndex, "FAT32", true);
                            Console.WriteLine("\n  Formatted as FAT32.");
                        }
                        catch (Exception ex2)
                        {
                            Console.WriteLine("  Format failed: " + ex2.Message);
                        }
                    }
                    else Console.WriteLine("  Skipped formatting.");
                }
            }
        }
    }
}
