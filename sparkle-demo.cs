using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Threading.Tasks;

class SparkleDemo
{
    static async Task Main()
    {
        // Download a shiny Pikachu sprite
        string spriteUrl = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/official-artwork/shiny/25.png";

        using var client = new HttpClient();
        var imageBytes = await client.GetByteArrayAsync(spriteUrl);

        using var ms = new System.IO.MemoryStream(imageBytes);
        using var pokemonImg = Image.FromStream(ms);

        // Create output image with sparkles
        int size = 256;
        using var result = new Bitmap(size, size);
        using var g = Graphics.FromImage(result);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(54, 57, 63)); // Discord dark background

        // Draw Pokemon centered
        int pkSize = 180;
        int pkX = (size - pkSize) / 2;
        int pkY = (size - pkSize) / 2 + 10;
        g.DrawImage(pokemonImg, pkX, pkY, pkSize, pkSize);

        // Add sparkle effects
        var sparkles = new (int x, int y, int s)[]
        {
            (45, 35, 20),
            (200, 45, 16),
            (30, 150, 14),
            (215, 130, 18),
            (120, 25, 12),
            (180, 200, 15),
            (50, 200, 13),
            (140, 210, 11),
        };

        foreach (var (x, y, s) in sparkles)
        {
            DrawSparkle(g, x, y, s, Color.FromArgb(255, 255, 255));
            DrawSparkle(g, x, y, s - 4, Color.FromArgb(200, 255, 255, 180));
        }

        // Add glow around Pokemon
        using var glowPath = new GraphicsPath();
        glowPath.AddEllipse(pkX - 10, pkY + pkSize - 40, pkSize + 20, 50);
        using var glowBrush = new PathGradientBrush(glowPath);
        glowBrush.CenterColor = Color.FromArgb(60, 255, 255, 100);
        glowBrush.SurroundColors = new[] { Color.FromArgb(0, 255, 255, 100) };
        g.FillPath(glowBrush, glowPath);

        result.Save(@"C:\Users\ericr\OneDrive\Desktop\sparkle-demo.png");
        Console.WriteLine("Saved to Desktop: sparkle-demo.png");
    }

    static void DrawSparkle(Graphics g, int cx, int cy, int size, Color color)
    {
        // 4-pointed star sparkle
        var points = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            double radius = (i % 2 == 0) ? size : size / 4.0;
            points[i] = new PointF(
                (float)(cx + radius * Math.Cos(angle - Math.PI / 2)),
                (float)(cy + radius * Math.Sin(angle - Math.PI / 2))
            );
        }

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
    }
}
