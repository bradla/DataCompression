#include <stdio.h>
#include <stdlib.h>
#include <SDL2/SDL.h>

#define SCREEN_WIDTH 320
#define SCREEN_HEIGHT 200

void display_file(const char *filename, SDL_Renderer *renderer, SDL_Texture *texture) {
    FILE *file = fopen(filename, "rb");
    if (file == NULL) {
        fprintf(stderr, "Error: Unable to open file %s\n", filename);
        return;
    }

    unsigned char buffer[SCREEN_WIDTH * SCREEN_HEIGHT];
    if (fread(buffer, 1, SCREEN_WIDTH * SCREEN_HEIGHT, file) != SCREEN_WIDTH * SCREEN_HEIGHT) {
        fprintf(stderr, "Error: File %s is not the expected size\n", filename);
        fclose(file);
        return;
    }
    fclose(file);

    // Scale grayscale values (0-255 to 0-63 for compatibility)
    for (int i = 0; i < SCREEN_WIDTH * SCREEN_HEIGHT; i++) {
        buffer[i] = buffer[i] >> 2;
    }

    // Create a pixel buffer for rendering
    Uint32 pixels[SCREEN_WIDTH * SCREEN_HEIGHT];
    for (int i = 0; i < SCREEN_WIDTH * SCREEN_HEIGHT; i++) {
        Uint8 intensity = buffer[i] * 4; // Scale back to 0-255 for display
        pixels[i] = SDL_MapRGB(SDL_AllocFormat(SDL_PIXELFORMAT_RGB888), intensity, intensity, intensity);
    }

    // Update texture with pixel data
    SDL_UpdateTexture(texture, NULL, pixels, SCREEN_WIDTH * sizeof(Uint32));
    SDL_RenderClear(renderer);
    SDL_RenderCopy(renderer, texture, NULL, NULL);
    SDL_RenderPresent(renderer);
}

int main(int argc, char *argv[]) {
    if (argc < 2) {
        printf("Usage: %s file1 [file2 ...]\n", argv[0]);
        return 1;
    }

    if (SDL_Init(SDL_INIT_VIDEO) != 0) {
        fprintf(stderr, "Error: Unable to initialize SDL: %s\n", SDL_GetError());
        return 1;
    }

    SDL_Window *window = SDL_CreateWindow("Grayscale Viewer",
                                          SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
                                          SCREEN_WIDTH, SCREEN_HEIGHT, SDL_WINDOW_SHOWN);
    if (!window) {
        fprintf(stderr, "Error: Unable to create window: %s\n", SDL_GetError());
        SDL_Quit();
        return 1;
    }

    SDL_Renderer *renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_ACCELERATED);
    SDL_Texture *texture = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_RGB888,
                                              SDL_TEXTUREACCESS_STREAMING, SCREEN_WIDTH, SCREEN_HEIGHT);

    for (int i = 1; i < argc; i++) {
        display_file(argv[i], renderer, texture);

        printf("Displaying file: %s\nPress any key to continue to the next file...\n", argv[i]);
        SDL_Event e;
        int running = 1;
        while (running) {
            while (SDL_PollEvent(&e)) {
                if (e.type == SDL_KEYDOWN || e.type == SDL_QUIT) {
                    running = 0;
                    break;
                }
            }
        }
    }

    SDL_DestroyTexture(texture);
    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);
    SDL_Quit();

    return 0;
}

