/* Run from Client3 with a host C compiler:
 * gcc -std=c99 -O2 -Wall -Wextra -Werror -pedantic src/xboxdisplay.c tests/xboxdisplay_test.c -lm -o build/xboxdisplay_test.exe
 * ./build/xboxdisplay_test.exe
 */
#include "../src/xboxdisplay.h"

#include <limits.h>
#include <math.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef TEST_FB_WIDTH
#define TEST_FB_WIDTH 640
#define TEST_FB_HEIGHT 480
#endif
enum { SW = 789, SH = 532, FW = TEST_FB_WIDTH, FH = TEST_FB_HEIGHT, GUARD_WORDS = 16 };
static const uint32_t CANARY = UINT32_C(0xa9531ec7);
static const uint32_t MARGIN = UINT32_C(0xdeadcafe);

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); \
        exit(1); \
    } \
} while (0)

typedef struct { uint32_t *allocation, *pixels; size_t count; } GuardedBuffer;
typedef struct { XboxDisplay display; GuardedBuffer canvas, framebuffer; } Fixture;

static GuardedBuffer buffer_new(size_t count, uint32_t fill) {
    GuardedBuffer result;
    result.count = count;
    result.allocation = malloc((count + 2 * GUARD_WORDS) * sizeof(uint32_t));
    CHECK(result.allocation != NULL);
    result.pixels = result.allocation + GUARD_WORDS;
    for (size_t i = 0; i < count + 2 * GUARD_WORDS; ++i) result.allocation[i] = CANARY;
    for (size_t i = 0; i < count; ++i) result.pixels[i] = fill;
    return result;
}

static void buffer_check(const GuardedBuffer *buffer) {
    for (size_t i = 0; i < GUARD_WORDS; ++i) {
        CHECK(buffer->allocation[i] == CANARY);
        CHECK(buffer->pixels[buffer->count + i] == CANARY);
    }
}

static void buffer_free(GuardedBuffer *buffer) {
    buffer_check(buffer);
    free(buffer->allocation);
}

static Fixture fixture_new(int inset) {
    Fixture result;
    result.canvas = buffer_new(SW * SH, 0);
    result.framebuffer = buffer_new(FW * FH, MARGIN);
    CHECK(xbox_display_init(&result.display, SW, SH, FW, FH, inset, result.canvas.pixels));
    CHECK(result.display.framebuffer_width == FW && result.display.framebuffer_height == FH);
    CHECK(result.display.source_width == SW && result.display.source_height == SH);
    CHECK(result.display.x >= inset && result.display.y >= inset);
    CHECK(result.display.width <= FW - 2 * inset && result.display.height <= FH - 2 * inset);
    CHECK(abs(2 * result.display.x + result.display.width - FW) <= 1);
    CHECK(abs(result.display.height * SW - result.display.width * SH) <= SW);
    CHECK(abs(2 * result.display.y + result.display.height - FH) <= 1);
    return result;
}

static void fixture_free(Fixture *fixture) {
    buffer_free(&fixture->canvas);
    buffer_free(&fixture->framebuffer);
}

static uint32_t pattern(int x, int y) {
    return ((uint32_t)((x * 13 + y * 7) & 255) << 16) |
           ((uint32_t)((x * 5 + y * 17) & 255) << 8) |
           (uint32_t)((x * 19 + y * 3) & 255);
}

static int *surface_new(int width, int height, int x, int y) {
    int *pixels = malloc((size_t)width * (size_t)height * sizeof(int));
    CHECK(pixels != NULL);
    for (int row = 0; row < height; ++row) {
        for (int column = 0; column < width; ++column) pixels[row * width + column] = (int)pattern(x + column, y + row);
    }
    return pixels;
}

static void present(Fixture *fixture, const uint8_t *cursor, int cx, int cy) {
    xbox_display_present(&fixture->display, fixture->framebuffer.pixels, cursor, 12, 18, cx, cy);
    buffer_check(&fixture->canvas);
    buffer_check(&fixture->framebuffer);
}

/* Independent floating-point area integral: no helper tables or destination
 * dirty-bound calculations. The only approximation allowed in comparisons is
 * the renderer's stated Q8 weights (at most two levels per RGB channel). */
static uint32_t reference_pixel(const XboxDisplay *display, const uint32_t *canvas,
                                int dx, int dy, const uint8_t *cursor, int cx, int cy) {
    double left = (double)dx * SW / display->width;
    double right = (double)(dx + 1) * SW / display->width;
    double top = (double)dy * SH / display->height;
    double bottom = (double)(dy + 1) * SH / display->height;
    double red = 0, green = 0, blue = 0;
    for (int sy = (int)floor(top); sy < (int)ceil(bottom) && sy < SH; ++sy) {
        double wy = fmin(bottom, sy + 1.0) - fmax(top, sy);
        for (int sx = (int)floor(left); sx < (int)ceil(right) && sx < SW; ++sx) {
            double wx = fmin(right, sx + 1.0) - fmax(left, sx);
            uint32_t color = canvas[sy * SW + sx];
            int cursor_dx = sx - cx, cursor_dy = sy - cy;
            if (cursor && cursor_dx >= 0 && cursor_dx < 12 && cursor_dy >= 0 && cursor_dy < 18) {
                const uint8_t *rgba = cursor + (cursor_dy * 12 + cursor_dx) * 4;
                if (rgba[3]) color = ((uint32_t)rgba[0] << 16) | ((uint32_t)rgba[1] << 8) | rgba[2];
            }
            red += ((color >> 16) & 255) * wx * wy;
            green += ((color >> 8) & 255) * wx * wy;
            blue += (color & 255) * wx * wy;
        }
    }
    double area = (right - left) * (bottom - top);
    return ((uint32_t)(red / area + 0.5) << 16) |
           ((uint32_t)(green / area + 0.5) << 8) | (uint32_t)(blue / area + 0.5);
}

static void check_color(uint32_t actual, uint32_t expected, int tolerance, int x, int y) {
    for (int shift = 0; shift <= 16; shift += 8) {
        int difference = abs((int)((actual >> shift) & 255) - (int)((expected >> shift) & 255));
        if (difference > tolerance) {
            fprintf(stderr, "Pixel (%d,%d): %06x != %06x (channel difference %d, allowed %d)\n",
                    x, y, (unsigned)actual, (unsigned)expected, difference, tolerance);
            exit(1);
        }
    }
}

static void check_reference(const Fixture *fixture, const uint32_t *canvas, const uint8_t *cursor, int cx, int cy) {
    const XboxDisplay *display = &fixture->display;
    for (int y = 0; y < FH; ++y) {
        for (int x = 0; x < FW; ++x) {
            uint32_t actual = fixture->framebuffer.pixels[y * FW + x];
            if (x < display->x || x >= display->x + display->width ||
                y < display->y || y >= display->y + display->height) CHECK(actual == MARGIN);
            else check_color(actual, reference_pixel(display, canvas, x - display->x, y - display->y, cursor, cx, cy), 2, x, y);
        }
    }
    buffer_check(&fixture->canvas);
    buffer_check(&fixture->framebuffer);
}

static void check_tables(const XboxDisplay *display) {
    for (int axis = 0; axis < 2; ++axis) {
        int length = axis ? display->height : display->width;
        int source_size = axis ? SH : SW;
        const XboxDisplaySample *samples = axis ? display->y_samples : display->x_samples;
        for (int i = 0; i < length; ++i) {
            CHECK(samples[i].count >= 1 && samples[i].count <= 3);
            CHECK(samples[i].first >= 0 && samples[i].first + samples[i].count <= source_size);
            unsigned sum = 0;
            for (int tap = 0; tap < samples[i].count; ++tap) sum += samples[i].weight[tap];
            CHECK(sum == 256);
        }
    }
}

static void test_constants_and_checkerboard(int inset) {
    Fixture fixture = fixture_new(inset);
    check_tables(&fixture.display);
    int *source = surface_new(SW, SH, 0, 0);
    const int colors[] = {0, 0xffffff, 0x217fc3};
    for (size_t color = 0; color < sizeof(colors) / sizeof(colors[0]); ++color) {
        for (int i = 0; i < SW * SH; ++i) source[i] = colors[color];
        xbox_display_blit(&fixture.display, source, SW, SH, 0, 0);
        present(&fixture, NULL, 0, 0);
        for (int y = 0; y < fixture.display.height; ++y) {
            for (int x = 0; x < fixture.display.width; ++x) {
                CHECK(fixture.framebuffer.pixels[(fixture.display.y + y) * FW + fixture.display.x + x] == (uint32_t)colors[color]);
            }
        }
    }
    for (int y = 0; y < SH; ++y) for (int x = 0; x < SW; ++x) source[y * SW + x] = ((x + y) & 1) ? 0xffffff : 0;
    xbox_display_blit(&fixture.display, source, SW, SH, 0, 0);
    present(&fixture, NULL, 0, 0);
    check_reference(&fixture, (const uint32_t *)source, NULL, 0, 0);
    free(source);
    fixture_free(&fixture);
}

static void test_tiles(int inset) {
    const int columns[] = {0, 8, 128, 214, 520, 574, 661, SW};
    const int rows[] = {0, 11, 166, 186, 265, 334, 386, 503, SH};
    const int column_count = (int)(sizeof(columns) / sizeof(columns[0])) - 1;
    const int row_count = (int)(sizeof(rows) / sizeof(rows[0])) - 1;
    Fixture whole = fixture_new(inset);
    int *source = surface_new(SW, SH, 0, 0);
    xbox_display_blit(&whole.display, source, SW, SH, 0, 0);
    present(&whole, NULL, 0, 0);
    check_reference(&whole, (const uint32_t *)source, NULL, 0, 0);
    for (int incremental = 0; incremental < 2; ++incremental) {
        Fixture tiled = fixture_new(inset);
        for (int i = 0; i < column_count * row_count; ++i) {
            int index = incremental ? column_count * row_count - 1 - i : i;
            int column = index % column_count, row = index / column_count;
            int x = columns[column], y = rows[row];
            int width = columns[column + 1] - x, height = rows[row + 1] - y;
            int *tile = surface_new(width, height, x, y);
            xbox_display_blit(&tiled.display, tile, width, height, x, y);
            if (incremental) present(&tiled, NULL, 0, 0);
            free(tile);
        }
        present(&tiled, NULL, 0, 0);
        CHECK(memcmp(tiled.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
        CHECK(memcmp(tiled.framebuffer.pixels, whole.framebuffer.pixels, FW * FH * sizeof(uint32_t)) == 0);
        fixture_free(&tiled);
    }
    free(source);
    fixture_free(&whole);
}

static void test_clipping(int inset) {
    const int rectangles[][4] = {
        {-17, -11, 91, 71}, {770, 511, 47, 42}, {-40, 200, 40, 30},
        {SW, 100, 18, 18}, {100, SH, 18, 18}, {23, -29, 17, 30},
        {SW - 1, SH - 1, 1, 1}, {0, 0, 1, 1}, {-9, 310, 807, 3}
    };
    Fixture fixture = fixture_new(inset);
    GuardedBuffer expected = buffer_new(SW * SH, 0);
    for (size_t i = 0; i < sizeof(rectangles) / sizeof(rectangles[0]); ++i) {
        int x = rectangles[i][0], y = rectangles[i][1];
        int width = rectangles[i][2], height = rectangles[i][3];
        int *patch = surface_new(width, height, x, y);
        xbox_display_blit(&fixture.display, patch, width, height, x, y);
        for (int sy = 0; sy < height; ++sy) {
            for (int sx = 0; sx < width; ++sx) {
                if (sx + x >= 0 && sx + x < SW && sy + y >= 0 && sy + y < SH) {
                    expected.pixels[(sy + y) * SW + sx + x] = (uint32_t)patch[sy * width + sx];
                }
            }
        }
        CHECK(memcmp(fixture.canvas.pixels, expected.pixels, SW * SH * sizeof(uint32_t)) == 0);
        present(&fixture, NULL, 0, 0);
        check_reference(&fixture, expected.pixels, NULL, 0, 0);
        free(patch);
    }
    buffer_free(&expected);
    fixture_free(&fixture);
}

static void test_disjoint_dirty_rectangles(int inset) {
    Fixture fixture = fixture_new(inset);
    int *source = surface_new(SW, SH, 0, 0);
    xbox_display_blit(&fixture.display, source, SW, SH, 0, 0);
    present(&fixture, NULL, 0, 0); /* Clear the initial full-frame invalidation. */

    /* These distant rectangles share one source row and therefore identical
     * vertical sample keys. The second rectangle must refill cached rows for
     * its own horizontal interval instead of reusing the first interval. */
    int first[5], second[7];
    for (int i = 0; i < 5; ++i) { first[i] = 0xf02040; source[24 + i] = first[i]; }
    for (int i = 0; i < 7; ++i) { second[i] = 0x19e8b3; source[690 + i] = second[i]; }
    xbox_display_blit(&fixture.display, first, 5, 1, 24, 0);
    xbox_display_blit(&fixture.display, second, 7, 1, 690, 0);
    present(&fixture, NULL, 0, 0);
    CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
    check_reference(&fixture, (const uint32_t *)source, NULL, 0, 0);

    /* Forty separated writes exceed the 32-rectangle capacity before any
     * present. Verify pixels queued before and after the full-redraw fallback,
     * while also checking the unchanged gaps across the complete frame. */
    for (int i = 0; i < 40; ++i) {
        int x = 20 + (i % 10) * 73;
        int y = 60 + (i / 10) * 97;
        int pixel = source[y * SW + x] ^ 0xffffff;
        source[y * SW + x] = pixel;
        xbox_display_blit(&fixture.display, &pixel, 1, 1, x, y);
    }
    present(&fixture, NULL, 0, 0);
    CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
    check_reference(&fixture, (const uint32_t *)source, NULL, 0, 0);
    free(source);
    fixture_free(&fixture);
}

static void test_cursor_movement(int inset) {
    Fixture fixture = fixture_new(inset);
    uint8_t cursor[12 * 18 * 4];
    for (int i = 0; i < 12 * 18; ++i) {
        cursor[i * 4] = 240;
        cursor[i * 4 + 1] = (uint8_t)(80 + i % 18);
        cursor[i * 4 + 2] = 31;
        cursor[i * 4 + 3] = (uint8_t)(i % 4 == 3 ? 0 : 255);
    }
    int *source = surface_new(SW, SH, 0, 0);
    xbox_display_blit(&fixture.display, source, SW, SH, 0, 0);
    present(&fixture, cursor, 516, 178);
    check_reference(&fixture, (const uint32_t *)source, cursor, 516, 178);

    /* A small dirty panel under an unchanged pointer must retain opaque cursor
     * pixels and expose the new background through its transparent pixels. */
    int patch[6 * 7];
    for (int y = 0; y < 7; ++y) {
        for (int x = 0; x < 6; ++x) {
            int index = (181 + y) * SW + 518 + x;
            patch[y * 6 + x] = source[index] ^ 0xffffff;
            source[index] = patch[y * 6 + x];
        }
    }
    xbox_display_blit(&fixture.display, patch, 6, 7, 518, 181);
    present(&fixture, cursor, 516, 178);
    check_reference(&fixture, (const uint32_t *)source, cursor, 516, 178);
    CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);

    const int positions[][2] = {{516, 178}, {43, 82}, {-3, -2}, {SW - 1, SH - 1}};
    for (size_t i = 0; i < sizeof(positions) / sizeof(positions[0]); ++i) {
        /* No intervening panel redraw: present must erase the previous cursor.
         * The initial cursor straddles the x=520 and y=186 panel boundaries. */
        present(&fixture, cursor, positions[i][0], positions[i][1]);
        check_reference(&fixture, (const uint32_t *)source, cursor, positions[i][0], positions[i][1]);
        CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
    }

    /* Choose fractional output-footprint edges independently of the Q8 tables.
     * A one-logical-pixel move crosses each edge, changing the output segment
     * that needs cursor compositing even though the underlying panels stay put. */
    int edge_dx = fixture.display.width / 3;
    int edge_dy = fixture.display.height / 3;
    while ((edge_dx * SW) % fixture.display.width == 0) ++edge_dx;
    while ((edge_dy * SH) % fixture.display.height == 0) ++edge_dy;
    int before_x = edge_dx * SW / fixture.display.width;
    int before_y = edge_dy * SH / fixture.display.height;
    const int boundary_positions[][2] = {
        {before_x, before_y}, {before_x + 1, before_y}, {before_x + 1, before_y + 1}
    };
    for (size_t i = 0; i < sizeof(boundary_positions) / sizeof(boundary_positions[0]); ++i) {
        present(&fixture, cursor, boundary_positions[i][0], boundary_positions[i][1]);
        check_reference(&fixture, (const uint32_t *)source, cursor, boundary_positions[i][0], boundary_positions[i][1]);
        CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
    }
    present(&fixture, NULL, 0, 0);
    check_reference(&fixture, (const uint32_t *)source, NULL, 0, 0);
    CHECK(memcmp(fixture.canvas.pixels, source, SW * SH * sizeof(uint32_t)) == 0);
    free(source);
    fixture_free(&fixture);
}

static void test_one_pixel_strokes(int inset) {
    Fixture fixture = fixture_new(inset);
    int black[SW], white[SW];
    for (int i = 0; i < SW; ++i) { black[i] = 0; white[i] = 0xffffff; }
    for (int axis = 0; axis < 2; ++axis) {
        int source_size = axis ? SH : SW;
        int destination_size = axis ? fixture.display.height : fixture.display.width;
        for (int position = 0; position < source_size; ++position) {
            if (position > 0) xbox_display_blit(&fixture.display, black, axis ? SW : 1, axis ? 1 : SH,
                                               axis ? 0 : position - 1, axis ? position - 1 : 0);
            xbox_display_blit(&fixture.display, white, axis ? SW : 1, axis ? 1 : SH,
                              axis ? 0 : position, axis ? position : 0);
            present(&fixture, NULL, 0, 0);
            int strongest = 0;
            for (int destination = 0; destination < destination_size; ++destination) {
                int dx = axis ? fixture.display.width / 2 : destination;
                int dy = axis ? destination : fixture.display.height / 2;
                uint32_t actual = fixture.framebuffer.pixels[(fixture.display.y + dy) * FW + fixture.display.x + dx];
                double begin = (double)destination * source_size / destination_size;
                double end = (double)(destination + 1) * source_size / destination_size;
                double overlap = fmax(0.0, fmin(end, position + 1.0) - fmax(begin, position));
                int expected = (int)(255.0 * overlap / (end - begin) + 0.5);
                check_color(actual, (uint32_t)(expected * 0x010101), 2, dx, dy);
                if ((int)(actual & 255) > strongest) strongest = (int)(actual & 255);
            }
            CHECK(strongest > 0); /* Every phase, including both extreme edges. */
        }
        xbox_display_blit(&fixture.display, black, axis ? SW : 1, axis ? 1 : SH,
                          axis ? 0 : source_size - 1, axis ? source_size - 1 : 0);
        present(&fixture, NULL, 0, 0);
    }
    check_reference(&fixture, fixture.canvas.pixels, NULL, 0, 0);
    fixture_free(&fixture);
}

int main(void) {
    const int insets[] = {16, 0, 32};
    for (size_t i = 0; i < sizeof(insets) / sizeof(insets[0]); ++i) {
        test_constants_and_checkerboard(insets[i]);
        test_tiles(insets[i]);
        test_clipping(insets[i]);
        test_disjoint_dirty_rectangles(insets[i]);
        test_cursor_movement(insets[i]);
        test_one_pixel_strokes(insets[i]);
        printf("PASS inset %d: exact-area reference, constants/checkerboard, tiled/disjoint/overflow dirty updates, clipping, cursor trails, every stroke phase, and guards.\n", insets[i]);
    }
    Fixture fixture = fixture_new(16);
    CHECK(!xbox_display_init(&fixture.display, SW, SH, XBOX_DISPLAY_MAX_WIDTH + 1, FH, 0, fixture.canvas.pixels));
    CHECK(!xbox_display_init(&fixture.display, SW, SH, FW, XBOX_DISPLAY_MAX_HEIGHT + 1, 0, fixture.canvas.pixels));
    CHECK(!xbox_display_init(&fixture.display, SW, SH, FW, FH, INT_MAX, fixture.canvas.pixels));
    fixture_free(&fixture);
    return 0;
}
