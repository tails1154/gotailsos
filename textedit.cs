using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace gotailsos
{
    public static class TextEdit
    {
        public static void OpenNano(string path)
        {
            // Create file if it doesn't exist
            if (!File.Exists(path))
            {
                Console.WriteLine("File does not exist. Creating new file: " + path);
                File.Create(path).Close();
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

            // Ensure at least one line exists to prevent crashes
            if (lines.Count == 0)
                lines.Add("");

            Stack<string> undoStack = new Stack<string>();
            Stack<string> redoStack = new Stack<string>();

            int cursorX = 0;
            int cursorY = 0;

            // Serialization helpers for undo/redo using null char separator
            Func<string> SerializeLines = () => string.Join("\0", lines);
            Action<string> DeserializeLines = (state) =>
            {
                lines = new List<string>(state.Split('\0'));
                if (lines.Count == 0) lines.Add("");
                if (cursorY >= lines.Count) cursorY = lines.Count - 1;
                if (cursorX > lines[cursorY].Length) cursorX = lines[cursorY].Length;
            };

            ConsoleKeyInfo keyInfo;

            void Render()
            {
                Console.Clear();
                for (int i = 0; i < lines.Count; i++)
                {
                    Console.WriteLine(lines[i]);
                }
                // Set cursor to correct position: row is line number, column is position in line
                if (cursorY < Console.BufferHeight && cursorX < Console.BufferWidth)
                {
                    Console.SetCursorPosition(cursorX, cursorY);
                }
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
                        redoStack.Push(SerializeLines());
                        string previousState = undoStack.Pop();
                        DeserializeLines(previousState);
                        Render();
                    }
                }
                else if (keyInfo.Key == ConsoleKey.Y && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    if (redoStack.Count > 0)
                    {
                        undoStack.Push(SerializeLines());
                        string nextState = redoStack.Pop();
                        DeserializeLines(nextState);
                        Render();
                    }
                }
                else
                {
                    undoStack.Push(SerializeLines());
                    redoStack.Clear();

                    if (keyInfo.Key == ConsoleKey.LeftArrow)
                    {
                        if (cursorX > 0)
                            cursorX--;
                        else if (cursorY > 0)
                        {
                            cursorY--;
                            cursorX = lines[cursorY].Length;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.RightArrow)
                    {
                        if (cursorY < lines.Count && cursorX < lines[cursorY].Length)
                            cursorX++;
                        else if (cursorY < lines.Count - 1)
                        {
                            cursorY++;
                            cursorX = 0;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.UpArrow)
                    {
                        if (cursorY > 0)
                        {
                            cursorY--;
                            if (cursorX > lines[cursorY].Length)
                                cursorX = lines[cursorY].Length;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.DownArrow)
                    {
                        if (cursorY < lines.Count - 1)
                        {
                            cursorY++;
                            if (cursorX > lines[cursorY].Length)
                                cursorX = lines[cursorY].Length;
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Home)
                    {
                        cursorX = 0;
                    }
                    else if (keyInfo.Key == ConsoleKey.End)
                    {
                        if (cursorY < lines.Count)
                            cursorX = lines[cursorY].Length;
                    }
                    else if (keyInfo.Key == ConsoleKey.Delete)
                    {
                        if (cursorY < lines.Count && cursorX < lines[cursorY].Length)
                        {
                            lines[cursorY] = lines[cursorY].Remove(cursorX, 1);
                        }
                        else if (cursorY < lines.Count - 1)
                        {
                            // Delete line break - merge with next line
                            lines[cursorY] += lines[cursorY + 1];
                            lines.RemoveAt(cursorY + 1);
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (cursorX > 0)
                        {
                            // Delete character before cursor
                            if (cursorY < lines.Count && cursorX <= lines[cursorY].Length)
                            {
                                lines[cursorY] = lines[cursorY].Remove(cursorX - 1, 1);
                                cursorX--;
                            }
                        }
                        else if (cursorY > 0)
                        {
                            // Merge with previous line
                            if (cursorY - 1 >= 0 && cursorY < lines.Count)
                            {
                                cursorX = lines[cursorY - 1].Length;
                                lines[cursorY - 1] += lines[cursorY];
                                lines.RemoveAt(cursorY);
                                cursorY--;
                            }
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        // Split line at cursor
                        if (cursorY < lines.Count)
                        {
                            string currentLine = lines[cursorY];
                            if (cursorX > currentLine.Length) cursorX = currentLine.Length;
                            string newLine = currentLine.Substring(cursorX);
                            lines[cursorY] = currentLine.Substring(0, cursorX);
                            lines.Insert(cursorY + 1, newLine);
                            cursorY++;
                            cursorX = 0;
                        }
                    }
                    else if (keyInfo.KeyChar >= ' ' && keyInfo.KeyChar < (char)127)
                    {
                        // Insert printable character
                        if (cursorY < lines.Count)
                        {
                            string line = lines[cursorY];
                            if (cursorX > line.Length) cursorX = line.Length;
                            lines[cursorY] = line.Insert(cursorX, keyInfo.KeyChar.ToString());
                            cursorX++;
                        }
                    }
                    else
                    {
                        // Unknown key - remove from undo stack
                        if (undoStack.Count > 0) undoStack.Pop();
                        Render();
                        continue;
                    }

                    Render();
                }
            }
        }

    }
}