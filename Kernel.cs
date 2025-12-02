using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem;
using gotailsOS;

namespace gotailsOS
{
    using Cosmos.System.FileSystem;
    public class Kernel: Sys.Kernel
    {
        Sys.FileSystem.CosmosVFS vfs;
        protected override void BeforeRun()
        {
            Console.WriteLine("[ OK ] Booted");
            Console.WriteLine("[ ... ] Init VFS");
            vfs = new CosmosVFS();
            Sys.FileSystem.VFS.VFSManager.RegisterVFS(vfs);
            Console.WriteLine("[ OK ] Init VFS");
            Console.WriteLine("Welcome to GoTailsOS!");

        }
        
        protected override void Run()
        {
            Console.Write("GoTailsOS>");
            string input = Console.ReadLine();
            CommandHandler.handleCommand(input, vfs);
        }
    }
}
