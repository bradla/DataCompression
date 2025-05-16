import sys
import sdl2
import struct
import ctypes

SCREEN_WIDTH = 320
SCREEN_HEIGHT = 200

def display_file(filename, renderer, texture):
    """Displays a grayscale file using SDL2."""
    try:
        with open(filename, 'rb') as f:
            buffer = f.read()
        if len(buffer) != SCREEN_WIDTH * SCREEN_HEIGHT:
            print(f"Error: File {filename} is not the expected size.")
            return
    except FileNotFoundError:
        print(f"Error: Unable to read file {filename}: File not found.")
        return
    except Exception as e:
        print(f"Error: Unable to read file {filename}: {e}")
        return

    # Scale the grayscale values (0-255 to 0-63) and create pixel data
    pixels = (ctypes.c_uint * (SCREEN_WIDTH * SCREEN_HEIGHT))()
    for i in range(len(buffer)):
        intensity = buffer[i] >> 2  # Scale down to 0-63
        intensity = intensity * 4     # Scale back to 0-255 for display
        color = sdl2.SDL_MapRGB(sdl2.SDL_AllocFormat(sdl2.SDL_PIXELFORMAT_RGB888), intensity, intensity, intensity)
        pixels[i] = color

    # Update the texture with pixel data
    pixels_bytes = (ctypes.c_ubyte * (SCREEN_WIDTH * SCREEN_HEIGHT * 4)).from_buffer(pixels)
    sdl2.SDL_UpdateTexture(texture, None, ctypes.addressof(pixels_bytes), SCREEN_WIDTH * 4)

    # Render the texture
    sdl2.SDL_RenderClear(renderer)
    sdl2.SDL_RenderCopy(renderer, texture, None, None)
    sdl2.SDL_RenderPresent(renderer)

def main():
    if len(sys.argv) < 2:
        print("Usage: python gs.py <file1> [file2 ...]")
        return

    if sdl2.SDL_Init(sdl2.SDL_INIT_VIDEO) < 0:
        print(f"Error: Unable to initialize SDL: {sdl2.SDL_GetError().decode()}")
        return

    window = sdl2.SDL_CreateWindow(b"Grayscale Viewer",
                                    sdl2.SDL_WINDOWPOS_CENTERED, sdl2.SDL_WINDOWPOS_CENTERED,
                                    SCREEN_WIDTH, SCREEN_HEIGHT, sdl2.SDL_WINDOW_SHOWN)

    if not window:
        print(f"Error: Unable to create window: {sdl2.SDL_GetError().decode()}")
        sdl2.SDL_Quit()
        return

    renderer = sdl2.SDL_CreateRenderer(window, -1, sdl2.SDL_RENDERER_ACCELERATED)
    texture = sdl2.SDL_CreateTexture(renderer, sdl2.SDL_PIXELFORMAT_RGB888,
                                     sdl2.SDL_TEXTUREACCESS_STREAMING, SCREEN_WIDTH, SCREEN_HEIGHT)

    for file in sys.argv[1:]:
        print(f"Displaying file: {file}")
        display_file(file, renderer, texture)

        print("Press any key to continue to the next file...")
        waiting = True
        event = sdl2.SDL_Event()
        while waiting:
            while sdl2.SDL_PollEvent(ctypes.byref(event)) != 0:
                if event.type == sdl2.SDL_KEYDOWN or event.type == sdl2.SDL_QUIT:
                    waiting = False
                    break

    sdl2.SDL_DestroyTexture(texture)
    sdl2.SDL_DestroyRenderer(renderer)
    sdl2.SDL_DestroyWindow(window)
    sdl2.SDL_Quit()

if __name__ == "__main__":
    main()