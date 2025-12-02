using Cosmos.System.Graphics;
using System.Drawing;
namespace testOS {
public class gui
{
    static VBECanvas canvas;

    public static void BeforeRun()
    {
        // Set graphics mode (example: 800x600x32)
        canvas = new VBECanvas(new Mode(800, 600, ColorDepth.ColorDepth32));

        // Clear screen
        canvas.Clear(Color.Black);

        // Draw a simple box
        DrawBox(100, 100, 200, 150, Color.White);

        // Display to screen
        canvas.Display();
    }

    public static void Run()
    {
        // Nothing needed for static drawing
    }

    private static void DrawBox(int x, int y, int w, int h, Color color)
    {
        // Draw rectangle outline
        canvas.DrawRectangle(color, x, y, w, h);
    }
}
}
