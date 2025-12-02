using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem;
using gotailsOS;

namespace gotailsOS
{
    using Cosmos.System.FileSystem;
    public class Kernel : Sys.Kernel
    {
        CosmosVFS fs;
        protected override void BeforeRun()
        {
            Console.WriteLine("[ OK ] Booted");
            Console.WriteLine("[ ... ] Init VFS");
            fs = new CosmosVFS();
            Sys.FileSystem.VFS.VFSManager.RegisterVFS(fs);
            Console.WriteLine("[ OK ] Init VFS");
            Console.WriteLine("Welcome to GoTailsOS!");

        }

        protected override void Run()
        {
            Console.Write("GoTailsOS " + gotailsOS.CommandHandler.CurrentDirectory + ">");
            string input = Console.ReadLine();
            CommandHandler.handleCommand(input, fs);
        }
    }
}
