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


namespace testOS.FileSystem {
    // This file was coded without the help of a LLM. The only LLM powered parts would be me being too lazy to fix syntax errors and asking it to do it for me or not knowing how to do something (but not like teling it to make a entire filesystem like i did with BadFS*). TailsFS2 will probably be AI Assisted to fix FS issues. I'm stil learning here lol.
    public class TailsFS {
         private BlockDevice device;
         private static readonly string MAGIC = "TAILSFS1";
         public TailsFS(BlockDevice device)
         {
             this.device = device;
         }

         public bool Detect()
         {
             byte[] block = device.NewBlockArray(1); // correct size
             device.ReadBlock(0, 1, ref block);

             string magic = Encoding.ASCII.GetString(block, 0, MAGIC.Length);
             return magic == MAGIC;
         }

         public void Format()
         {
             byte[] block = device.NewBlockArray(1);

             // Write magic string
             byte[] magicBytes = Encoding.ASCII.GetBytes(MAGIC);
             Array.Copy(magicBytes, 0, block, 0, magicBytes.Length);

             // Start directory tree
             block[8] = 0x01;
             block[9] = 0x03;

             byte[] rootName = Encoding.ASCII.GetBytes("/");
             Array.Copy(rootName, 0, block, 10, rootName.Length);

             block[11] = 0xFF;  // end name
             block[12] = 0x02;  // end tree

             device.WriteBlock(0, 1, ref block);
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
