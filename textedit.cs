using System;
using Sys = Cosmos.System;
using Cosmos.System.FileSystem.Listing;
using Cosmos.System.FileSystem;
using System.Collections.Generic;  // for Stack<T>
using System.Linq;
using System.IO;                 // for Reverse()
using gotailsos;
using System.Reflection.Metadata.Ecma335;

namespace gotailsos
{
    public static class TextEdit
    {
        public static void OpenNano(string path)
        {
            if (!Sys.FileSystem.VFS.VFSManager.FileExists(path))
            {
                Console.WriteLine("File does not exist. Creating new file: " + path);
                var file = Sys.FileSystem.VFS.VFSManager.CreateFile(path);
            }

            List<string> lines = new List<string>();
            try
            {
                using (var stream = File.OpenRead(path))
                using (var reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error reading file: " + ex.Message);
                return;
            }

            Stack<string> undoStack = new Stack<string>();
            Stack<string> redoStack = new Stack<string>();

            int cursorX = 0;
            int cursorY = 0;

            ConsoleKeyInfo keyInfo;

            void Render()
            {
                Console.Clear();
                for (int i = 0; i < lines.Count; i++)
                {
                    Console.WriteLine(lines[i]);
                }
                Console.SetCursorPosition(cursorX, cursorY);
            }

            Render();

            while (true)
            {
                keyInfo = Console.ReadKey(true);

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    break;
                }
                else if (keyInfo.Key == ConsoleKey.Z && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (undoStack.Count > 0)
                    {
                        redoStack.Push(string.Join("\n", lines));
                        string previousState = undoStack.Pop();
                        lines = previousState.Split('\n').ToList();
                        cursorX = 0;
                        cursorY = 0;
                        Render();
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Y && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (redoStack.Count > 0)
                    {
                        undoStack.Push(string.Join("\n", lines));
                        string nextState = redoStack.Pop();
                        lines = nextState.Split('\n').ToList();
                        cursorX = 0;
                        cursorY = 0;
                        Render();
                    }
                }
                else
                {
                    undoStack.Push(string.Join("\n", lines));
                    redoStack.Clear();
                    if (keyInfo.Key == ConsoleKey.Backspace)
                    {
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
                    }
                    else if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        string newLine = lines[cursorY].Substring(cursorX);
                        lines[cursorY] = lines[cursorY].Substring(0, cursorX);
                        lines.Insert(cursorY + 1, newLine);
                        cursorY++;
                        cursorX = 0;
                    }
                    else
                    {
                        lines[cursorY] = lines[cursorY].Insert(cursorX, keyInfo.KeyChar.ToString());
                        cursorX++;
                    }
                    Render();
                }
            }
        }
                
    }
}