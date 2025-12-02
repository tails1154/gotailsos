// testOS/Exec/BadExecLoader.cs
using System;
using System.Text;
using testOS.FileSystem;  // your instance-based FS
using Sys = Cosmos.System;

namespace testOS.Exec
{
    public static class BadExecLoader
    {
        private const uint Magic = 0x45584542; // 'BEXE' little-endian

        // TEMP GUEST MEMORY BUFFER (simple, no paging)
        private static byte[] GuestBuffer = new byte[4096];
        private static int GuestPtr = 0;

        private static void ResetGuestMemory()
        {
            GuestPtr = 0;
            Array.Clear(GuestBuffer, 0, GuestBuffer.Length);
        }

        private static void PutByteToGuest(byte b)
        {
            if (GuestPtr < GuestBuffer.Length)
                GuestBuffer[GuestPtr++] = b;
        }

        private static void WriteToGuestMemory(string s)
        {
            ResetGuestMemory();
            byte[] bytes = Encoding.ASCII.GetBytes(s ?? "");
            int count = Math.Min(bytes.Length, GuestBuffer.Length);
            Array.Copy(bytes, 0, GuestBuffer, 0, count);
            GuestPtr = count;
        }

        private static string GetGuestString()
        {
            return Encoding.ASCII.GetString(GuestBuffer, 0, GuestPtr);
        }

        private static string ReadString(byte[] code, ref int ip)
        {
            var sb = new StringBuilder();
            while (ip < code.Length && code[ip] != 0x00)
            {
                sb.Append((char)code[ip]);
                ip++;
            }
            if (ip < code.Length && code[ip] == 0x00) ip++; // skip terminator safely
            return sb.ToString();
        }

        // NOTE: RunExecutable now takes a TailsFS instance so it can call your filesystem methods
        public static bool RunExecutable(byte[] data, TailsFS fs)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (fs == null) throw new ArgumentNullException(nameof(fs));

            if (data.Length < 16)
                throw new Exception("Invalid executable: too small");

            uint magic = BitConverter.ToUInt32(data, 0);
            if (magic != Magic)
                throw new Exception("Invalid executable: bad magic");

            byte version = data[4];
            if (version != 1)
                throw new Exception("Unsupported version");

            uint codeLen = BitConverter.ToUInt32(data, 6);

            if (16 + codeLen > data.Length)
                throw new Exception("Truncated executable");

            byte[] code = new byte[codeLen];
            Array.Copy(data, 16, code, 0, codeLen);

            // reset guest memory for this run
            ResetGuestMemory();

            return ExecuteBytecode(code, fs);
        }

        private static bool ExecuteBytecode(byte[] code, TailsFS fs)
        {
            int ip = 0;

            while (ip < code.Length)
            {
                byte op = code[ip++];

                switch (op)
                {
                    case 0x01: // putchar
                        Console.Write((char)code[ip++]);
                        break;

                    case 0x02: // print string until 0
                        while (ip < code.Length && code[ip] != 0x00)
                            Console.Write((char)code[ip++]);
                    if (ip < code.Length && code[ip] == 0x00) ip++;
                    break;

                    case 0xFF: // exit
                        return true;

                    case 0xF4: // noop
                        break;

                        // --- NEW OPCODES ---

                    case 0x10: // read file -> writes file contents to guest memory buffer
                    {
                        string path = ReadString(code, ref ip);
                        try
                        {
                            var fileData = fs.ReadFile(path);
                            WriteToGuestMemory(Encoding.ASCII.GetString(fileData ?? Array.Empty<byte>()));
                        }
                        catch (Exception)
                        {
                            // on error, return empty string in guest memory
                            WriteToGuestMemory("");
                        }
                        break;
                    }

                    case 0x11: // write file (path, data)
                    {
                        string path = ReadString(code, ref ip);
                        string dataStr = ReadString(code, ref ip);
                        try
                        {
                            fs.WriteFile(path, Encoding.ASCII.GetBytes(dataStr ?? ""));
                        }
                        catch (Exception ex)
                        {
                            // Write failure: you can decide to throw or ignore; we print an error for debugging
                            Console.WriteLine($"[BX] writefile error: {ex.Message}");
                        }
                        break;
                    }

                    case 0x20: // read key -> stores one byte in guest buffer
                    {
                        var key = Sys.KeyboardManager.ReadKey();
                        PutByteToGuest((byte)key.KeyChar);
                        break;
                    }

                    case 0x30: // clear screen
                        Console.Clear();
                        break;

                    case 0x31: // set cursor (x,y)
                    {
                        int x = code[ip++];
                        int y = code[ip++];
                        try { Console.SetCursorPosition(x, y); }
                        catch { /* ignore if out of bounds */ }
                        break;
                    }
                    // inside ExecuteBytecode switch - add these cases

                    case 0x12: // print entire guest buffer to console
                    {
                        // print bytes [0 .. GuestPtr-1]
                        for (int i = 0; i < GuestPtr; i++)
                            Console.Write((char)GuestBuffer[i]);
                        break;
                    }

                    case 0x13: // write guest buffer to file: path (null-terminated)
                    {
                        string path = ReadString(code, ref ip);
                        try
                        {
                            byte[] toWrite = new byte[GuestPtr];
                            Array.Copy(GuestBuffer, 0, toWrite, 0, GuestPtr);
                            fs.WriteFile(path, toWrite);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[BX] writebuf error: {ex.Message}");
                        }
                        break;
                    }

                    case 0x14: // print last guest byte (if any)
                    {
                        if (GuestPtr > 0)
                            Console.Write((char)GuestBuffer[GuestPtr - 1]);
                        break;
                    }
                    // Add these cases inside the large switch in ExecuteBytecode

                    case 0x50: // JMP_REL16 (signed int16)
                    {
                        if (ip + 1 >= code.Length) throw new Exception("Truncated JMP");
                        short rel = (short)(code[ip] | (code[ip + 1] << 8));
                        ip += 2;
                        int newIp = ip + rel;
                        if (newIp < 0 || newIp > code.Length) throw new Exception("Invalid jump target");
                        ip = newIp;
                        break;
                    }

                    case 0x51: // JEQ_LAST_REL16 <byte> <lo><hi>
                    {
                        if (ip + 2 >= code.Length) throw new Exception("Truncated JEQ");
                        byte val = code[ip++];
                        short rel = (short)(code[ip] | (code[ip + 1] << 8));
                        ip += 2;
                        if (GuestPtr > 0 && GuestBuffer[GuestPtr - 1] == val)
                        {
                            int newIp = ip + rel;
                            if (newIp < 0 || newIp > code.Length) throw new Exception("Invalid jump target");
                            ip = newIp;
                        }
                        break;
                    }


                    default:
                        throw new Exception($"Unknown opcode: {op:X2}");
                }
            }

            return true;
        }

        // Expose guest buffer read for the host caller (if an in-VM program wants to consume it)
        // Example: after running an executable that did a readfile, Kernel can call:
        //   string s = BadExecLoader.GetGuestOutput();
        public static string GetGuestOutput()
        {
            return GetGuestString();
        }
    }
}
