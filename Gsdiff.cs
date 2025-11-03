using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

class GSDiff
{
    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);
    
    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    
    [DllImport("gdi32.dll")]
    static extern bool SetPixelV(IntPtr hdc, int x, int y, uint color);

    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: gsdiff file1 file2 [-B]");
            return;
        }

        string file1 = args[0];
        string file2 = args[1];
        bool batch = args.Length > 2 && (args[2] == "-b" || args[2] == "-B");

        if (!File.Exists(file1) || !File.Exists(file2))
        {
            Console.WriteLine("Could not open file!");
            return;
        }

        double error = 0.0;
        int width = 320;
        int height = 200;

        try
        {
            using (Bitmap bmp1 = new Bitmap(file1))
            using (Bitmap bmp2 = new Bitmap(file2))
            {
                if (!batch)
                {
                    Console.WriteLine("Display mode not supported in C# version. Using batch mode.");
                }

                for (int y = 0; y < height && y < bmp1.Height && y < bmp2.Height; y++)
                {
                    for (int x = 0; x < width && x < bmp1.Width && x < bmp2.Width; x++)
                    {
                        Color c1 = bmp1.GetPixel(x, y);
                        Color c2 = bmp2.GetPixel(x, y);
                        
                        // Convert to grayscale if needed
                        int gray1 = (int)(c1.R * 0.299 + c1.G * 0.587 + c1.B * 0.114);
                        int gray2 = (int)(c2.R * 0.299 + c2.G * 0.587 + c2.B * 0.114);
                        
                        int diff = gray1 - gray2;
                        error += diff * diff;
                    }
                }

                int totalPixels = Math.Min(width, bmp1.Width) * Math.Min(height, bmp1.Height);
                error /= totalPixels;
                
                Console.WriteLine($"RMS error between {file1} and {file2} is {Math.Sqrt(error):F6}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing images: {ex.Message}");
        }
    }
}