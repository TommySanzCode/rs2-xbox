#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "xboxprofile.h"

typedef struct XboxDisplaySample {
    int first, count;
    uint16_t weight[3];
} XboxDisplaySample;

typedef struct XboxDisplayRect {
    int left, top, right, bottom;
} XboxDisplayRect;

/* The caller owns the source-sized RGB32 canvas. Retaining original pixels
 * across panel redraws lets the filter sample neighbors across panel edges.
 * Three cached horizontal rows avoid another full-frame allocation. */
typedef struct XboxDisplay {
    int framebuffer_width, framebuffer_height;
    int source_width, source_height;
    int x, y, width, height;
    uint32_t *canvas;
    XboxDisplaySample x_samples[XBOX_DISPLAY_MAX_WIDTH], y_samples[XBOX_DISPLAY_MAX_HEIGHT];
    uint32_t rows[3][XBOX_DISPLAY_MAX_WIDTH];
    int row_source[3];
    XboxDisplayRect dirty[32];
    int dirty_count;
    bool cursor_visible;
    const uint8_t *cursor_rgba;
    int cursor_x, cursor_y, cursor_width, cursor_height;
} XboxDisplay;

bool xbox_display_init(XboxDisplay *display, int source_width, int source_height,
                       int framebuffer_width, int framebuffer_height, int inset,
                       uint32_t *canvas);
void xbox_display_blit(XboxDisplay *display, const int *pixels,
                       int width, int height, int x, int y);
void xbox_display_present(XboxDisplay *display, volatile uint32_t *framebuffer,
                          const uint8_t *cursor_rgba, int cursor_width, int cursor_height,
                          int cursor_x, int cursor_y);
