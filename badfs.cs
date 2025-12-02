// BadFS3 rewritten implementation (API-compatible)
using System;
using System.Text;
using System.Collections.Generic;
using Cosmos.HAL.BlockDevice;

namespace testOS.FileSystem
{
    public class BadFS3
    {
        private BlockDevice device;
        private const string MAGIC = "BADFS3";
        private const uint FAT_EOC = 0xFFFFFFFF;
        private const uint FAT_FREE = 0x00000000;

        private const ulong HEADER_LBA = 0;
        private const ulong DIR_LBA = 1;
        private const ulong FAT_LBA = 2;
        private const ulong DATA_START_LBA = 3;

        private const int DIR_ENTRY_SIZE = 64;
        private const int DIR_ENTRIES_PER_BLOCK = 8;
        private const int NAME_MAX = 31;
        private const int FAT_ENTRIES_PER_BLOCK = 128;

        public BadFS3(BlockDevice dev) => device = dev;

        public void Format()
        {
            var header = device.NewBlockArray(1);
            Array.Clear(header, 0, header.Length);
            Encoding.ASCII.GetBytes(MAGIC).CopyTo(header, 0);
            header[6] = 0x03; header[7] = 0x00;
            BitConverter.GetBytes((uint)DIR_ENTRIES_PER_BLOCK).CopyTo(header, 8);
            BitConverter.GetBytes((uint)FAT_ENTRIES_PER_BLOCK).CopyTo(header, 12);
            BitConverter.GetBytes((uint)DATA_START_LBA).CopyTo(header, 16);
            device.WriteBlock(HEADER_LBA, 1, ref header);

            var dir = device.NewBlockArray(1);
            Array.Clear(dir, 0, dir.Length);
            // root entry stored at index 0
            WriteDir(dir, 0, "/", 0, true, 0, 0);
            device.WriteBlock(DIR_LBA, 1, ref dir);

            var fat = device.NewBlockArray(1);
            for (int i = 0; i < FAT_ENTRIES_PER_BLOCK; i++) BitConverter.GetBytes(FAT_FREE).CopyTo(fat, i * 4);
            device.WriteBlock(FAT_LBA, 1, ref fat);
        }

        public bool Detect()
        {
            var hdr = device.NewBlockArray(1);
            device.ReadBlock(HEADER_LBA, 1, ref hdr);
            return Encoding.ASCII.GetString(hdr, 0, MAGIC.Length) == MAGIC;
        }

        public void CreateDirectory(string path)
        {
            var (parent, name) = ResolveParent(path);
            if (name == null) throw new Exception("Invalid path");

            var dir = LoadDir();
            if (FindUnder(dir, parent, name) >= 0) throw new Exception("Already exists");

            int free = FindFreeDir(dir);
            if (free < 0) throw new Exception("Directory table full");

            WriteDir(dir, free, name, (uint)parent, true, 0, 0);
            SaveDir(dir);
        }

        public string[] ListDirectory(string path = "/")
        {
            int idx = ResolvePath(path);
            if (idx < 0) throw new Exception("Not found");

            var dir = LoadDir();
            var list = new List<string>();
            for (int i = 0; i < DIR_ENTRIES_PER_BLOCK; i++)
            {
                int off = i * DIR_ENTRY_SIZE;
                if (ReadUInt(dir, off + 32) != (uint)idx) continue;
                string name = ReadName(dir, off);
                if (string.IsNullOrEmpty(name)) continue;
                list.Add((dir[off + 36] == 1 ? "<DIR> " : "      ") + name);
            }
            return list.ToArray();
        }

        public void WriteFile(string path, byte[] data)
        {
            data ??= Array.Empty<byte>();
            var (parent, name) = ResolveParent(path);
            if (name == null) throw new Exception("Invalid path");

            var dir = LoadDir();
            var fat = LoadFat();

            int existing = FindUnder(dir, parent, name);
            if (existing >= 0 && dir[existing * DIR_ENTRY_SIZE + 36] == 1)
                throw new Exception("Is a directory");

            if (existing >= 0)
            {
                uint start = ReadUInt(dir, existing * DIR_ENTRY_SIZE + 37);
                FreeChain(fat, start);
                ClearDir(dir, existing);
            }

            int bs = (int)device.BlockSize;
            int need = Math.Max(1, (data.Length + bs - 1) / bs);
            int[] chain = AllocBlocks(fat, need);
            if (chain == null) throw new Exception("No space");

            int pos = 0;
            for (int i = 0; i < chain.Length; i++)
            {
                var blk = device.NewBlockArray(1);
                Array.Clear(blk, 0, blk.Length);
                int len = Math.Min(bs, data.Length - pos);
                if (len > 0) Array.Copy(data, pos, blk, 0, len);
                pos += Math.Max(0, len);
                ulong lba = DATA_START_LBA + (ulong)chain[i];
                device.WriteBlock(lba, 1, ref blk);
            }

            for (int i = 0; i < chain.Length; i++)
                fat[chain[i]] = (i == chain.Length - 1) ? FAT_EOC : (uint)chain[i + 1];

            SaveFat(fat);

            int idx = existing >= 0 ? existing : FindFreeDir(dir);
            WriteDir(dir, idx, name, (uint)parent, false, (uint)chain[0], (uint)data.Length);
            SaveDir(dir);
        }

        public byte[] ReadFile(string path)
        {
            int idx = ResolvePath(path);
            if (idx < 0) throw new Exception("Not found");
            var dir = LoadDir();

            int off = idx * DIR_ENTRY_SIZE;
            if (dir[off + 36] == 1) throw new Exception("Is a directory");

            uint start = ReadUInt(dir, off + 37);
            uint size = ReadUInt(dir, off + 41);

            var fat = LoadFat();
            var result = new byte[size];
            int pos = 0;
            int bs = (int)device.BlockSize;

            uint cur = start;
            while (cur != FAT_EOC)
            {
                var blk = device.NewBlockArray(1);
                device.ReadBlock(DATA_START_LBA + cur, 1, ref blk);
                int len = Math.Min(bs, (int)(size - (uint)pos));
                if (len > 0) Array.Copy(blk, 0, result, pos, len);
                pos += Math.Max(0, len);
                cur = fat[cur];
            }
            return result;
        }

        public void Delete(string path)
        {
            int idx = ResolvePath(path);
            if (idx < 0) throw new Exception("Not found");

            var dir = LoadDir();
            int off = idx * DIR_ENTRY_SIZE;

            bool isDir = dir[off + 36] == 1;
            if (isDir)
            {
                for (int i = 0; i < DIR_ENTRIES_PER_BLOCK; i++)
                    if (ReadUInt(dir, i * DIR_ENTRY_SIZE + 32) == (uint)idx)
                        throw new Exception("Directory not empty");

                ClearDir(dir, idx);
                SaveDir(dir);
                return;
            }

            var fat = LoadFat();
            uint start = ReadUInt(dir, off + 37);
            FreeChain(fat, start);
            SaveFat(fat);

            ClearDir(dir, idx);
            SaveDir(dir);
        }

        // Internal helpers -------------------------------------------------------
        private byte[] LoadDir()
        {
            var b = device.NewBlockArray(1);
            device.ReadBlock(DIR_LBA, 1, ref b);
            return b;
        }

        private void SaveDir(byte[] d) => device.WriteBlock(DIR_LBA, 1, ref d);

        private uint[] LoadFat()
        {
            var b = device.NewBlockArray(1);
            device.ReadBlock(FAT_LBA, 1, ref b);
            uint[] fat = new uint[FAT_ENTRIES_PER_BLOCK];
            for (int i = 0; i < FAT_ENTRIES_PER_BLOCK; i++) fat[i] = BitConverter.ToUInt32(b, i * 4);
            return fat;
        }

        private void SaveFat(uint[] fat)
        {
            var b = device.NewBlockArray(1);
            for (int i = 0; i < FAT_ENTRIES_PER_BLOCK; i++) BitConverter.GetBytes(fat[i]).CopyTo(b, i * 4);
            device.WriteBlock(FAT_LBA, 1, ref b);
        }

        private string ReadName(byte[] d, int off)
        {
            int len = 0;
            for (int i = 0; i < NAME_MAX; i++)
            {
                if (d[off + i] == 0) break;
                len++;
            }
            return len == 0 ? null : Encoding.ASCII.GetString(d, off, len);
        }

        private void WriteDir(byte[] d, int idx, string name, uint parent, bool isDir, uint start, uint size)
        {
            int off = idx * DIR_ENTRY_SIZE;
            for (int i = 0; i < DIR_ENTRY_SIZE; i++) d[off + i] = 0;
            var nb = Encoding.ASCII.GetBytes(name);
            Array.Copy(nb, 0, d, off, Math.Min(NAME_MAX, nb.Length));
            BitConverter.GetBytes(parent).CopyTo(d, off + 32);
            d[off + 36] = (byte)(isDir ? 1 : 0);
            BitConverter.GetBytes(start).CopyTo(d, off + 37);
            BitConverter.GetBytes(size).CopyTo(d, off + 41);
        }

        private void ClearDir(byte[] d, int idx)
        {
            int off = idx * DIR_ENTRY_SIZE;
            for (int i = 0; i < DIR_ENTRY_SIZE; i++) d[off + i] = 0;
        }

        private int FindFreeDir(byte[] d)
        {
            for (int i = 0; i < DIR_ENTRIES_PER_BLOCK; i++)
            {
                bool empty = true;
                for (int j = 0; j < NAME_MAX; j++) if (d[i * DIR_ENTRY_SIZE + j] != 0) { empty = false; break; }
                if (empty) return i;
            }
            return -1;
        }

        private int FindUnder(byte[] d, int parent, string name)
        {
            for (int i = 0; i < DIR_ENTRIES_PER_BLOCK; i++)
            {
                int off = i * DIR_ENTRY_SIZE;
                if (ReadUInt(d, off + 32) != (uint)parent) continue;
                if (ReadName(d, off) == name) return i;
            }
            return -1;
        }

        private (int parent, string name) ResolveParent(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/")) return (-1, null);

            // special-case root
            if (path == "/") return (0, null);

            // remove trailing slashes but don't lose a single leading slash
            path = path.TrimEnd('/');
            if (string.IsNullOrEmpty(path) || path == "/") return (0, null);

            int cut = path.LastIndexOf('/');
            if (cut < 0) return (-1, null); // malformed

            string parent = path.Substring(0, cut == 0 ? 1 : cut);
            string name = path.Substring(cut + 1);
            if (string.IsNullOrEmpty(name)) return (-1, null);

            int idx = ResolvePath(parent);
            return (idx, name);
        }

        private int ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("/")) return -1;

            // exact root
            if (path == "/") return 0;

            // normalize trailing slash
            path = path.TrimEnd('/');
            if (string.IsNullOrEmpty(path) || path == "/") return 0;

            // split into components safely
            var parts = path.Substring(1).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var d = LoadDir();
            int cur = 0;

            foreach (var p in parts)
            {
                bool ok = false;
                for (int i = 0; i < DIR_ENTRIES_PER_BLOCK; i++)
                {
                    int off = i * DIR_ENTRY_SIZE;
                    if (ReadUInt(d, off + 32) != (uint)cur) continue;
                    if (ReadName(d, off) == p)
                    {
                        cur = i;
                        ok = true;
                        break;
                    }
                }
                if (!ok) return -1;
            }
            return cur;
        }
        public int ResolvePathPublic(string path) => ResolvePath(path);

        public bool IsDirectory(int index)
        {
            var d = LoadDir();
            return d[index * 64 + 36] == 1;
        }


        private uint ReadUInt(byte[] d, int off) => BitConverter.ToUInt32(d, off);

        private void FreeChain(uint[] fat, uint start)
        {
            uint cur = start;
            while (cur != FAT_EOC)
            {
                uint nxt = fat[cur];
                fat[cur] = FAT_FREE;
                if (nxt == FAT_EOC) break;
                cur = nxt;
            }
        }

        private int[] AllocBlocks(uint[] fat, int count)
        {
            var list = new List<int>();
            for (int i = 0; i < FAT_ENTRIES_PER_BLOCK && list.Count < count; i++)
            {
                if (fat[i] == FAT_FREE) list.Add(i);
            }
            return list.Count == count ? list.ToArray() : null;
        }
    }
}
