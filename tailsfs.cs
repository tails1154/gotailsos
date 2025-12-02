using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using Cosmos.HAL.BlockDevice;

// TailsFS1 Spec
// 0x01 - Start of directory tree
// 0x02 - End of Directory tree
// 0x03 - Directory Entry (put directory name after and terminate directory name with 0xFF (that means a directory name cant have 0xFF but who puts 0xFF in a directory name?))
// 0x04 - Start of file tree
// 0x05 - End of Directory tree
// 0x06 - File Entry (append file size and terminate the size with 0xFF, then append the file data)


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
            byte buffer[] = new byte[12] // make a 8 byte buffer to write the *magic* stuff
            byte[] textBytes = Encoding.ASCII.GetBytes(MAGIC);
            Array.Copy(textBytes, 0, buffer, 0, textBytes.Length); // uhh i just noticed making the original buffer was pointless LOL
            // Make directory structure data
            buffer[9] = 0x01
            buffer[10] = 0x03 // pro tip: dont code at 1:07 AM
            byte[] textBytes = Encoding.ASCII.GetBytes("/");
            Array.Copy(textBytes, 11, buffer, 11, textBytes.Length);
            buffer[12] = 0x02
            device.WriteBlock(0, 1, ref buffer);
        }
        public string[] ListDirectory(string path) {
            throw new NotImplementedException();
        }
        public void CreateDirectory(string path) {
            throw new NotImplementedException();
        }
        public void Delete(string path) {
            throw new NotImplementedException();
        }
        public byte[] ReadFile(string path) {
            throw new NotImplementedException();
        }
        public void WriteFile(string path, byte[] data) {
            throw new NotImplementedException();
        }
        public int ResolvePathPublic(string path) {
            throw new NotImplementedException(); // what the heck is "ResolvePathPublic() supposed to be for???"
        }
        public bool IsDirectory(int index) {
            throw new NotImplementedException();
        }


    }
}
