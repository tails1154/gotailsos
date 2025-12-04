using System;
using System.Linq;
using Sys = Cosmos.System;

namespace gotailsos
{
    public static class BugCheck
    {
        public static void Bsod(Exception ex)
        {
            Console.Clear();
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Clear(); // fill screen with blue

            // Big ASCII sad face
            string sadFace = @"      
 .:.                                 ::.      
 ::::.   ::                          ::::     
  -::::.:::.       -.:+=         :.. .:::.    
  .:::::::      :=-:::::::::.    :.:-::::.    
   :::::::    .--::::::::::::-.   ::::::::    
    :::::.   :::::=#%%%%*-:::::.  ::::::.     
     .::.   ::::-#%%%%%%%%*:-:::   .::.       
            ::::#%%%%%%%%%%*:--:-             
            :::+%%%%%%%%%%%%+::--             
            ::-%%%*:::::-#%%#-:-              
             -=%#=::::::::+%%=-:              
             .:--::::::::---+--.              
              ..:----::-----::.               
                  ..::::::";

            Console.WriteLine(sadFace);

            // Display exception info
            Console.WriteLine("\nA Unhandled Exception Occurred!\n");
            Console.WriteLine("Message: " + ex.Message + "\n");
            // Console.WriteLine("Exception Type: " + ex.GetType().FullName + "\n");


            if (ex.InnerException != null)
            {
                Console.WriteLine("Inner Exception:\n" + ex.InnerException + "\n");
            }

            Console.WriteLine("Press any key to reboot...");
            Console.WriteLine("The system is probably unstable, but if you want to continue anyways, press 'C'.");
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.C)
            {
                Console.Clear();
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Continuing execution in 5 seconds...");
                System.Threading.Thread.Sleep(5000);
                Console.Clear();
                Console.ResetColor();
                Console.WriteLine("Resuming execution...");
                return; // continue execution
            }

            // Optional: reboot Cosmos OS
            // Sys.Power.Reboot();
            Sys.Power.Reboot();
        }
    }
}
