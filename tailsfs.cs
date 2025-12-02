using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.HAL.BlockDevice;




namespace testOS.Filesystem {
    // This file was coded without the help of a LLM. The only LLM powered parts would be me being too lazy to fix syntax errors and asking it to do it for me or not knowing how to do something (but not like teling it to make a entire filesystem like i did with BadFS*). TailsFS2 will probably be AI Assisted to fix FS issues. I'm stil learning here lol.
    public class TailsFS(BlockDevice device) {
         private BlockDevice device;
         private static readonly string MAGIC = "TAILSFS1";

        public bool Detect() {
            byte buffer[] = new byte[8] // make a 8 byte buffer (because thats the maximum possible size of our *magic* number i mean text
            device.ReadBlock(0, 1, ref buffer); // what even IS this function the docs SUCK here LOL
            string magicNumber = Encoding.ASCII.GetString(buffer).TrimEnd('\0'); // aparantly the \0 is a null terminator but okay
            if (magicNumber === MAGIC) {
                return true;
            } else {
                return false;
            }
        }
        public void Format() {
            byte buffer[] = new byte[8] // make a 8 byte buffer to write the *magic* stuff
            byte[] textBytes = Encoding.ASCII.GetBytes(MAGIC);
            Array.Copy(textBytes, 0, buffer, 0, textBytes.Length); // uhh i just noticed making the original buffer was pointless LOL
            device.WriteBlock(0, 1, ref buffer);
        }
    }
}
