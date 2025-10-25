using System;
using System.IO;
using SDL2;

class GrayscaleViewer
{
    const int SCREEN_WIDTH = 320;
    const int SCREEN_HEIGHT = 200;

    static void DisplayFile(string filename, IntPtr renderer, IntPtr texture)
    {
        // Read the grayscale file
        byte[] buffer;
        try
        {
            buffer = File.ReadAllBytes(filename);
            if (buffer.Length != SCREEN_WIDTH * SCREEN_HEIGHT)
            {
                Console.WriteLine($"Error: File {filename} is not the expected size.");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: Unable to read file {filename}: {ex.Message}");
            return;
        }

        // Scale the grayscale values (0-255 to 0-63) and create pixel data
        uint[] pixels = new uint[SCREEN_WIDTH * SCREEN_HEIGHT];
        for (int i = 0; i < buffer.Length; i++)
        {
            byte intensity = (byte)(buffer[i] >> 2); // Scale down to 0-63
            intensity = (byte)(intensity * 4);       // Scale back to 0-255 for display
            pixels[i] = SDL.SDL_MapRGB(SDL.SDL_AllocFormat(SDL.SDL_PIXELFORMAT_RGB888), intensity, intensity, intensity);
        }

        // Update the texture with pixel data
        unsafe
        {
            fixed (uint* pixelPointer = pixels)
            {
                SDL.SDL_UpdateTexture(texture, IntPtr.Zero, (IntPtr)pixelPointer, SCREEN_WIDTH * sizeof(uint));
            }
        }

        // Render the texture
        SDL.SDL_RenderClear(renderer);
        SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, IntPtr.Zero);
        SDL.SDL_RenderPresent(renderer);
    }

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: GrayscaleViewer <file1> [file2 ...]");
            return;
        }

        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO) < 0)
        {
            Console.WriteLine($"Error: Unable to initialize SDL: {SDL.SDL_GetError()}");
            return;
        }

        IntPtr window = SDL.SDL_CreateWindow("Grayscale Viewer",
            SDL.SDL_WINDOWPOS_CENTERED, SDL.SDL_WINDOWPOS_CENTERED,
            SCREEN_WIDTH, SCREEN_HEIGHT, SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN);

        if (window == IntPtr.Zero)
        {
            Console.WriteLine($"Error: Unable to create window: {SDL.SDL_GetError()}");
            SDL.SDL_Quit();
            return;
        }

        IntPtr renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED);
        IntPtr texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_RGB888,
            (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, SCREEN_WIDTH, SCREEN_HEIGHT);

        foreach (string file in args)
        {
            Console.WriteLine($"Displaying file: {file}");
            DisplayFile(file, renderer, texture);

            Console.WriteLine("Press any key to continue to the next file...");
            bool waiting = true;
            while (waiting)
            {
                while (SDL.SDL_PollEvent(out SDL.SDL_Event e) != 0)
                {
                    if (e.type == SDL.SDL_EventType.SDL_KEYDOWN || e.type == SDL.SDL_EventType.SDL_QUIT)
                    {
                        waiting = false;
                        break;
                    }
                }
            }
        }

        SDL.SDL_DestroyTexture(texture);
        SDL.SDL_DestroyRenderer(renderer);
        SDL.SDL_DestroyWindow(window);
        SDL.SDL_Quit();
    }
}

