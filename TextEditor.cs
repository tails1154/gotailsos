using System;
using System.Collections.Generic;
using System.Text;
using Sys = Cosmos.System;
using testOS.FileSystem;

namespace testOS
{
    public static class TextEditor
    {
        private class Cursor
        {
            public int Row = 0;
            public int Col = 0;
        }

        private static List<StringBuilder> buffer;
        private static int scrollOffset = 0;
        private static Cursor cursor = new Cursor();
        private static string status = "";
        private static string filePath = "";
        private static BadFS4 fsRef;

        private static void LoadFile(string path, BadFS4 fs)
        {
            buffer = new List<StringBuilder>();
            filePath = path;
            fsRef = fs;

            try
            {
                var data = fs.ReadFile(path);
                var text = Encoding.UTF8.GetString(data);
                var lines = text.Replace("\r", "").Split('\n');

                foreach (var l in lines)
                    buffer.Add(new StringBuilder(l));
            }
            catch
            {
                // File doesn’t exist -> start empty
                buffer.Add(new StringBuilder());
            }

            cursor.Row = 0;
            cursor.Col = 0;
            scrollOffset = 0;
            status = "Loaded " + path;
        }

        private static void SaveFile()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < buffer.Count; i++)
            {
                sb.Append(buffer[i].ToString());
                if (i < buffer.Count - 1)
                    sb.Append("\n");
            }

            fsRef.WriteFile(filePath, Encoding.UTF8.GetBytes(sb.ToString()));
            status = "Saved " + filePath;
        }

        private static void Render()
        {
            Console.Clear();

            int h = Console.WindowHeight - 2; // reserve bottom line for status

            for (int y = 0; y < h; y++)
            {
                int lineIndex = scrollOffset + y;
                if (lineIndex < buffer.Count)
                {
                    var line = buffer[lineIndex].ToString();
                    if (line.Length > Console.WindowWidth)
                        line = line.Substring(0, Console.WindowWidth);

                    Console.SetCursorPosition(0, y);
                    Console.Write(line);
                }
            }

            // Draw status bar
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            Console.BackgroundColor = ConsoleColor.DarkBlue;
            Console.ForegroundColor = ConsoleColor.White;
            string s = $" {filePath} | {status}  (Ctrl+S save, Ctrl+Q quit)";
            if (s.Length < Console.WindowWidth)
                s = s + new string(' ', Console.WindowWidth - s.Length);
            else
                s = s.Substring(0, Console.WindowWidth);

            Console.Write(s);
            Console.ResetColor();

            // Place cursor
            int cy = cursor.Row - scrollOffset;
            if (cy >= 0 && cy < h)
            {
                Console.SetCursorPosition(cursor.Col, cy);
            }
        }

        private static void InsertChar(char c)
        {
            if (c == '\n')
            {
                var current = buffer[cursor.Row];
                var newLine = current.ToString().Substring(cursor.Col);
                current.Remove(cursor.Col, current.Length - cursor.Col);
                buffer.Insert(cursor.Row + 1, new StringBuilder(newLine));
                cursor.Row++;
                cursor.Col = 0;
                return;
            }

            buffer[cursor.Row].Insert(cursor.Col, c);
            cursor.Col++;
        }

        private static void Backspace()
        {
            if (cursor.Col > 0)
            {
                buffer[cursor.Row].Remove(cursor.Col - 1, 1);
                cursor.Col--;
                return;
            }

            // Merge with previous line
            if (cursor.Row > 0)
            {
                int prevLen = buffer[cursor.Row - 1].Length;
                buffer[cursor.Row - 1].Append(buffer[cursor.Row].ToString());
                buffer.RemoveAt(cursor.Row);
                cursor.Row--;
                cursor.Col = prevLen;

                if (cursor.Row < scrollOffset)
                    scrollOffset = cursor.Row;
            }
        }

        private static void DeleteKey()
        {
            var line = buffer[cursor.Row];
            if (cursor.Col < line.Length)
            {
                line.Remove(cursor.Col, 1);
            }
            else if (cursor.Row < buffer.Count - 1)
            {
                // merge with next line
                line.Append(buffer[cursor.Row + 1].ToString());
                buffer.RemoveAt(cursor.Row + 1);
            }
        }

        private static void MoveCursorUp()
        {
            if (cursor.Row > 0)
                cursor.Row--;

            if (cursor.Col > buffer[cursor.Row].Length)
                cursor.Col = buffer[cursor.Row].Length;

            if (cursor.Row < scrollOffset)
                scrollOffset--;
        }

        private static void MoveCursorDown()
        {
            if (cursor.Row < buffer.Count - 1)
                cursor.Row++;

            if (cursor.Col > buffer[cursor.Row].Length)
                cursor.Col = buffer[cursor.Row].Length;

            int h = Console.WindowHeight - 2;
            if (cursor.Row >= scrollOffset + h)
                scrollOffset++;
        }

        private static void MoveCursorLeft()
        {
            if (cursor.Col > 0)
            {
                cursor.Col--;
            }
            else if (cursor.Row > 0)
            {
                cursor.Row--;
                cursor.Col = buffer[cursor.Row].Length;
            }
        }

        private static void MoveCursorRight()
        {
            if (cursor.Col < buffer[cursor.Row].Length)
            {
                cursor.Col++;
            }
            else if (cursor.Row < buffer.Count - 1)
            {
                cursor.Row++;
                cursor.Col = 0;
            }
        }

        public static void Run(BadFS4 fs, string path)
        {
            LoadFile(path, fs);

            while (true)
            {
                Render();

                var key = Sys.KeyboardManager.ReadKey();
                if (key == null) continue;

                var c = key.KeyChar;

                // CTRL+S
                if (Sys.KeyboardManager.ControlPressed &&
                    key.Key == Sys.ConsoleKeyEx.S)
                {
                    SaveFile();
                    continue;
                }

                // CTRL+Q
                if (Sys.KeyboardManager.ControlPressed &&
                    key.Key == Sys.ConsoleKeyEx.Q)
                {
                    return;
                }

                switch (key.Key)
                {
                    case Sys.ConsoleKeyEx.Enter:
                        InsertChar('\n');
                        break;

                    case Sys.ConsoleKeyEx.Backspace:
                        Backspace();
                        break;

                    case Sys.ConsoleKeyEx.Delete:
                        DeleteKey();
                        break;

                    case Sys.ConsoleKeyEx.LeftArrow:
                        MoveCursorLeft();
                        break;

                    case Sys.ConsoleKeyEx.RightArrow:
                        MoveCursorRight();
                        break;

                    case Sys.ConsoleKeyEx.UpArrow:
                        MoveCursorUp();
                        break;

                    case Sys.ConsoleKeyEx.DownArrow:
                        MoveCursorDown();
                        break;

                    default:
                        if (!char.IsControl(c))
                            InsertChar(c);
                    break;
                }
            }
        }


    }
}
