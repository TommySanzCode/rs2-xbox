#include "xboxdisplay.h"

#include <string.h>

/* Integrate each output pixel's source footprint. Cumulative rounding makes
 * the Q8 weights sum to exactly 256, so flat colors remain unchanged. */
static void init_samples(XboxDisplaySample *samples, int source_size, int output_size) {
    for (int d = 0; d < output_size; d++) {
        const int start = d * source_size;
        const int end = (d + 1) * source_size;
        XboxDisplaySample *sample = &samples[d];
        sample->first = start / output_size;
        sample->count = (end + output_size - 1) / output_size - sample->first;
        int previous = 0;
        for (int tap = 0; tap < sample->count; tap++) {
            int edge = (sample->first + tap + 1) * output_size;
            if (edge > end) edge = end;
            const int cumulative = ((edge - start) * 256 + source_size / 2) / source_size;
            sample->weight[tap] = (uint16_t)(cumulative - previous);
            previous = cumulative;
        }
    }
}

bool xbox_display_init(XboxDisplay *display, int source_width, int source_height,
                       int framebuffer_width, int framebuffer_height, int inset,
                       uint32_t *canvas) {
    if (!display || !canvas || source_width <= 0 || source_width > 4096 ||
        source_height <= 0 || source_height > 4096 ||
        framebuffer_width <= 0 || framebuffer_width > 640 ||
        framebuffer_height <= 0 || framebuffer_height > 480 || inset < 0 || inset > 320 ||
        inset * 2 >= framebuffer_width || inset * 2 >= framebuffer_height) {
        return false;
    }
    const int available_width = framebuffer_width - inset * 2;
    const int available_height = framebuffer_height - inset * 2;
    int width = available_width;
    int height = width * source_height / source_width;
    if (height > available_height) {
        height = available_height;
        width = height * source_width / source_height;
    }
    /* Up to a 2:1 reduction has at most three contributing pixels per axis. */
    if (width == 0 || height == 0 || source_width > width * 2 || source_height > height * 2) {
        return false;
    }

    display->framebuffer_width = framebuffer_width;
    display->framebuffer_height = framebuffer_height;
    display->source_width = source_width;
    display->source_height = source_height;
    display->width = width;
    display->height = height;
    display->x = (framebuffer_width - width) / 2;
    display->y = (framebuffer_height - height) / 2;
    display->canvas = canvas;
    display->dirty[0] = (XboxDisplayRect){0, 0, source_width, source_height};
    display->dirty_count = 1;
    display->cursor_visible = false;
    init_samples(display->x_samples, source_width, width);
    init_samples(display->y_samples, source_height, height);
    return true;
}

static void invalidate(XboxDisplay *display, int left, int top, int right, int bottom) {
    if (left < 0) left = 0;
    if (top < 0) top = 0;
    if (right > display->source_width) right = display->source_width;
    if (bottom > display->source_height) bottom = display->source_height;
    if (left >= right || top >= bottom) return;
    /* Keep distant panels separate: the viewport and minimap redraw every
     * frame, but the large inventory below the minimap often stays unchanged. */
    for (int i = 0; i < display->dirty_count;) {
        const XboxDisplayRect *rect = &display->dirty[i];
        if (left <= rect->right && right >= rect->left &&
            top <= rect->bottom && bottom >= rect->top) {
            if (rect->left < left) left = rect->left;
            if (rect->top < top) top = rect->top;
            if (rect->right > right) right = rect->right;
            if (rect->bottom > bottom) bottom = rect->bottom;
            display->dirty[i] = display->dirty[--display->dirty_count];
            i = 0; /* The enlarged rectangle can now reach an earlier one. */
        } else {
            i++;
        }
    }
    if (display->dirty_count == 32) {
        display->dirty[0] = (XboxDisplayRect){0, 0, display->source_width, display->source_height};
        display->dirty_count = 1;
    } else {
        display->dirty[display->dirty_count++] = (XboxDisplayRect){left, top, right, bottom};
    }
}

void xbox_display_blit(XboxDisplay *display, const int *pixels,
                       int width, int height, int x, int y) {
    if (!pixels || width <= 0 || height <= 0) return;
    const int left = x > 0 ? x : 0;
    const int top = y > 0 ? y : 0;
    const int right = x + width < display->source_width ? x + width : display->source_width;
    const int bottom = y + height < display->source_height ? y + height : display->source_height;
    if (left >= right || top >= bottom) return;
    for (int sy = top; sy < bottom; sy++) {
        memcpy(display->canvas + sy * display->source_width + left,
               pixels + (sy - y) * width + left - x, (size_t)(right - left) * sizeof(uint32_t));
    }
    invalidate(display, left, top, right, bottom);
}

/* Round the Q8 horizontal result before the vertical pass. Each channel loses
 * at most half a level; packed RGB rows reduce cache traffic and permit the
 * same two-lane red/blue arithmetic in both passes. */
static uint32_t pack_filtered(uint32_t red_blue, uint32_t green) {
    return (((red_blue + 0x00800080u) >> 8) & 0x00ff00ffu) |
           ((green + 128u) & 0x0000ff00u);
}

static void filter_row(XboxDisplay *display, int slot, int sy, int left, int right,
                       const uint8_t *cursor_rgba, int cursor_width, int cursor_height,
                       int cursor_x, int cursor_y) {
    const uint32_t *source = display->canvas + sy * display->source_width;
    uint32_t *filtered = display->rows[slot];
    for (int dx = left; dx < right; dx++) {
        const XboxDisplaySample *sample = &display->x_samples[dx];
        uint32_t red_blue = 0, green = 0;
        for (int tap = 0; tap < sample->count; tap++) {
            const uint32_t color = source[sample->first + tap];
            const uint32_t weight = sample->weight[tap];
            red_blue += (color & 0x00ff00ffu) * weight;
            green += ((color >> 8) & 0xffu) * weight;
        }
        filtered[dx] = pack_filtered(red_blue, green);
    }

    /* Most rows never touch the pointer. Repair only intersecting footprints
     * so cursor bounds/alpha checks stay out of the bulk filtering loop. */
    if (cursor_rgba && sy >= cursor_y && sy - cursor_y < cursor_height) {
        const uint8_t *cursor_row = cursor_rgba + (sy - cursor_y) * cursor_width * 4;
        while (left < right && display->x_samples[left].first + display->x_samples[left].count <= cursor_x) left++;
        while (right > left && display->x_samples[right - 1].first >= cursor_x + cursor_width) right--;
        for (int dx = left; dx < right; dx++) {
            const XboxDisplaySample *sample = &display->x_samples[dx];
            uint32_t red_blue = 0, green = 0;
            for (int tap = 0; tap < sample->count; tap++) {
                const int sx = sample->first + tap;
                uint32_t color = source[sx];
                if (sx >= cursor_x && sx - cursor_x < cursor_width) {
                    const uint8_t *pixel = cursor_row + (sx - cursor_x) * 4;
                    if (pixel[3]) {
                        color = ((uint32_t)pixel[0] << 16) | ((uint32_t)pixel[1] << 8) | pixel[2];
                    }
                }
                const uint32_t weight = sample->weight[tap];
                red_blue += (color & 0x00ff00ffu) * weight;
                green += ((color >> 8) & 0xffu) * weight;
            }
            filtered[dx] = pack_filtered(red_blue, green);
        }
    }
    display->row_source[slot] = sy;
}

void xbox_display_present(XboxDisplay *display, volatile uint32_t *framebuffer,
                          const uint8_t *cursor_rgba, int cursor_width, int cursor_height,
                          int cursor_x, int cursor_y) {
    const bool visible = cursor_rgba && cursor_width > 0 && cursor_height > 0;
    const bool cursor_changed = visible != display->cursor_visible ||
        (visible && (cursor_rgba != display->cursor_rgba || cursor_x != display->cursor_x ||
                     cursor_y != display->cursor_y || cursor_width != display->cursor_width ||
                     cursor_height != display->cursor_height));
    if (cursor_changed && display->cursor_visible) {
        invalidate(display, display->cursor_x, display->cursor_y,
                   display->cursor_x + display->cursor_width, display->cursor_y + display->cursor_height);
    }
    if (cursor_changed && visible) {
        invalidate(display, cursor_x, cursor_y, cursor_x + cursor_width, cursor_y + cursor_height);
    }
    display->cursor_visible = visible;
    display->cursor_rgba = cursor_rgba;
    display->cursor_x = cursor_x;
    display->cursor_y = cursor_y;
    display->cursor_width = cursor_width;
    display->cursor_height = cursor_height;
    for (int rectangle = 0; rectangle < display->dirty_count; rectangle++) {
        const XboxDisplayRect *dirty = &display->dirty[rectangle];
        int left = 0, right = display->width, top = 0, bottom = display->height;
        while (left < right && display->x_samples[left].first + display->x_samples[left].count <= dirty->left) left++;
        while (right > left && display->x_samples[right - 1].first >= dirty->right) right--;
        while (top < bottom && display->y_samples[top].first + display->y_samples[top].count <= dirty->top) top++;
        while (bottom > top && display->y_samples[bottom - 1].first >= dirty->bottom) bottom--;
        for (int slot = 0; slot < 3; slot++) display->row_source[slot] = -1;

        for (int dy = top; dy < bottom; dy++) {
            const XboxDisplaySample *sample = &display->y_samples[dy];
            const uint32_t *rows[3] = {NULL};
            for (int tap = 0; tap < sample->count; tap++) {
                const int sy = sample->first + tap;
                const int slot = sy % 3;
                if (display->row_source[slot] != sy) {
                    filter_row(display, slot, sy, left, right, visible ? cursor_rgba : NULL,
                               cursor_width, cursor_height, cursor_x, cursor_y);
                }
                rows[tap] = display->rows[slot];
            }
            volatile uint32_t *destination = framebuffer +
                (display->y + dy) * display->framebuffer_width + display->x;
            const uint32_t *row0 = rows[0];
            if (sample->count == 1) {
                for (int dx = left; dx < right; dx++) destination[dx] = row0[dx];
            } else if (sample->count == 2) {
                const uint32_t *row1 = rows[1];
                const uint32_t weight0 = sample->weight[0];
                for (int dx = left; dx < right; dx++) {
                    const uint32_t color0 = row0[dx], color1 = row1[dx];
                    const uint32_t base_rb = color1 & 0x00ff00ffu;
                    /* Weights sum to 256: using the last tap as a base saves one
                     * multiply per lane. Unsigned differences preserve the sum. */
                    const uint32_t rb = (base_rb << 8) +
                        ((color0 & 0x00ff00ffu) - base_rb) * weight0;
                    const uint32_t g = (color1 & 0x0000ff00u) +
                        (((color0 >> 8) & 0xffu) - ((color1 >> 8) & 0xffu)) * weight0;
                    destination[dx] = pack_filtered(rb, g);
                }
            } else {
                const uint32_t *row1 = rows[1], *row2 = rows[2];
                const uint32_t weight0 = sample->weight[0], weight1 = sample->weight[1];
                for (int dx = left; dx < right; dx++) {
                    const uint32_t color0 = row0[dx], color1 = row1[dx], color2 = row2[dx];
                    const uint32_t base_rb = color2 & 0x00ff00ffu;
                    const uint32_t base_g = (color2 >> 8) & 0xffu;
                    const uint32_t rb = (base_rb << 8) +
                        ((color0 & 0x00ff00ffu) - base_rb) * weight0 +
                        ((color1 & 0x00ff00ffu) - base_rb) * weight1;
                    const uint32_t g = (color2 & 0x0000ff00u) +
                        (((color0 >> 8) & 0xffu) - base_g) * weight0 +
                        (((color1 >> 8) & 0xffu) - base_g) * weight1;
                    destination[dx] = pack_filtered(rb, g);
                }
            }
        }
    }
    display->dirty_count = 0;
}
