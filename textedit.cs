using System;
using System.Collections.Generic;
using Sys = Cosmos.System;
using System.IO;
using Cosmos;
using Cosmos.System.FileSystem.VFS;
using Cosmos.System.FileSystem;

namespace gotailsos
{
    public static class TextEdit
    {
        public static void OpenNano(string path, Cosmos.System.FileSystem.CosmosVFS vfs)
        {
            // Ensure path is valid
            if (!File.Exists(path))
            {
                Console.WriteLine("File does not exist. Creating: " + path);
                vfs.CreateFile(path);
            }

            // Read file (VFS compatible)
            List<string> lines = new List<string>();
            try
            {
                string[] fileLines = VFSManager.GetFile(path).GetFileStream().Read(new byte[VFSManager.GetFile(path).GetFileStream().Length], 0, (int)VFSManager.GetFile(path).GetFileStream().Length).ToString().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                lines.AddRange(fileLines);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to read file: " + ex.Message);
                return;
            }

            if (lines.Count == 0)
                lines.Add("");

            Stack<string> undo = new Stack<string>();
            Stack<string> redo = new Stack<string>();

            int cursorX = 0;
            int cursorY = 0;

            Func<string> Serialize = () => string.Join("\0", lines);

            Action<string> Deserialize = s =>
            {
                lines = new List<string>(s.Split('\0'));
                if (lines.Count == 0) lines.Add("");

                if (cursorY >= lines.Count) cursorY = lines.Count - 1;
                if (cursorX > lines[cursorY].Length) cursorX = lines[cursorY].Length;
            };

            void Render()
            {
                Console.Clear();
                foreach (string line in lines)
                {
                    Console.WriteLine(line);
                }
                Console.SetCursorPosition(cursorX, cursorY);
            }

            Render();

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Escape)
                {
                    // Save file before exit
                    try
                    {
                        File.WriteAllLines(path, lines);
                        Console.WriteLine("Saved.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Failed to save: " + ex.Message);
                    }
                    break;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (key.Key == ConsoleKey.Z)
                    {
                        if (undo.Count > 0)
                        {
                            redo.Push(Serialize());
                            Deserialize(undo.Pop());
                            Render();
                        }
                        continue;
                    }
                    if (key.Key == ConsoleKey.Y)
                    {
                        if (redo.Count > 0)
                        {
                            undo.Push(Serialize());
                            Deserialize(redo.Pop());
                            Render();
                        }
                        continue;
                    }
                }

                undo.Push(Serialize());
                redo.Clear();

                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:
                        if (cursorX > 0) cursorX--;
                        else if (cursorY > 0)
                        {
                            cursorY--;
                            cursorX = lines[cursorY].Length;
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (cursorX < lines[cursorY].Length) cursorX++;
                        else if (cursorY < lines.Count - 1)
                        {
                            cursorY++;
                            cursorX = 0;
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (cursorY > 0)
                        {
                            cursorY--;
                            if (cursorX > lines[cursorY].Length)
                                cursorX = lines[cursorY].Length;
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (cursorY < lines.Count - 1)
                        {
                            cursorY++;
                            if (cursorX > lines[cursorY].Length)
                                cursorX = lines[cursorY].Length;
                        }
                        break;

                    case ConsoleKey.Backspace:
                        if (cursorX > 0)
                        {
                            lines[cursorY] = lines[cursorY].Remove(cursorX - 1, 1);
                            cursorX--;
                        }
                        else if (cursorY > 0)
                        {
                            cursorX = lines[cursorY - 1].Length;
                            lines[cursorY - 1] += lines[cursorY];
                            lines.RemoveAt(cursorY);
                            cursorY--;
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (cursorX < lines[cursorY].Length)
                        {
                            lines[cursorY] = lines[cursorY].Remove(cursorX, 1);
                        }
                        else if (cursorY < lines.Count - 1)
                        {
                            lines[cursorY] += lines[cursorY + 1];
                            lines.RemoveAt(cursorY + 1);
                        }
                        break;

                    case ConsoleKey.Enter:
                        {
                            string cur = lines[cursorY];
                            string left = cur.Substring(0, cursorX);
                            string right = cur.Substring(cursorX);

                            lines[cursorY] = left;
                            lines.Insert(cursorY + 1, right);

                            cursorY++;
                            cursorX = 0;
                        }
                        break;

                    default:
                        char c = key.KeyChar;
                        if (c >= ' ' && c < 127)
                        {
                            lines[cursorY] = lines[cursorY].Insert(cursorX, c.ToString());
                            cursorX++;
                        }
                        else
                        {
                            undo.Pop(); // invalid key = ignore change
                        }
                        break;
                }

                Render();
            }
        }
    }
}
