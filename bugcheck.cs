using System;
using System.Linq;
using Sys = Cosmos.System;

namespace GoTailsOS
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
      .                                              .           
       :--                                              -        
      .   +    :..                                   =.  .       
     -. ..  -    :           =:  #             :::   =..  -      
      :..   .....          :+:    :===:        :   ..:.  ..      
       :...    :        :: ...   .-:.  .:=      = .=. .   -      
       -...    -      ..::. :.........:...:     =     ... -      
       ::..  . :     .......:=+***+=........-   = .   ...-       
         =..  -     ......:**********+. .....    =.  ..=         
           ++-     = ....-****#***#****:.... :     ++            
                   - ...-***########****. ... *                  
                   - ..:****##*#####****+...  *                  
                   - ..=****#*-..:=##****-...                    
                   -..:****=.       :+***+:..:                   
                    :.-***-.  .      .+***:..-                   
                     .:*=:... .   .  ..-**::-                    
                     -  :................. .-                    
                        :..............::-:.                     
                           .=-:::::--.";

            Console.WriteLine(sadFace);

            // Display exception info
            Console.WriteLine("\nA Unhandled Exception Occurred!\n");
            Console.WriteLine("Message: " + ex.Message + "\n");
            Console.WriteLine("Exception Type: " + ex.GetType().FullName + "\n");


            if (ex.InnerException != null)
            {
                Console.WriteLine("Inner Exception:\n" + ex.InnerException + "\n");
            }

            Console.WriteLine("Press any key to reboot...");
            Console.ReadKey();

            // Optional: reboot Cosmos OS
            // Sys.Power.Reboot();
            Sys.Power.Reboot();
        }
    }
}
