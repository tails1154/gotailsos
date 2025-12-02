using Cosmos.System.Graphics;
using System.Drawing;

namespace MiniGfx
{
    public static class Gfx
    {
        private static SVGAIICanvas canvas;

        public static void Init(SVGAIICanvas cvs)
        {
            canvas = cvs;
        }

        public static void Clear(Color c)
        {
            canvas.Clear(c);
            canvas.Display();
        }

        public static void Square(int x, int y, int size, Color color)
        {
            canvas.DrawFilledRectangle(color, x, y, size, size);
            canvas.Display();
        }
    }
}
