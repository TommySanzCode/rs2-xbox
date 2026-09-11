/* Run from the repository root:
 * gcc -std=c99 -Wall -Wextra -Werror -pedantic src/cachecrc.c tests/cachecrc_test.c -o cachecrc_test.exe
 * ./cachecrc_test.exe tests/fixtures/client225-crc.bin build/cachecrc-test-fixture.bin
 * The second argument must be a nonexistent scratch file.
 */
#include "../src/cachecrc.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define CHECK(condition) do { \
    if (!(condition)) { \
        fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #condition); \
        exit(1); \
    } \
} while (0)

static void write_fixture(const char *path, const uint8_t *bytes, size_t size) {
    FILE *file = fopen(path, "wb");
    CHECK(file != NULL);
    CHECK(fwrite(bytes, 1, size, file) == size);
    CHECK(fclose(file) == 0);
}

static void check_failure(const char *path, CacheCrcStatus expected) {
    int checksums[CACHE_CRC_COUNT];
    for (int i = 0; i < CACHE_CRC_COUNT; ++i) {
        checksums[i] = 12345;
    }
    CHECK(cachecrc_load(path, checksums) == expected);
    CHECK(strlen(cachecrc_status_message(expected)) != 0);
    for (int i = 0; i < CACHE_CRC_COUNT; ++i) {
        CHECK(checksums[i] == 12345);
    }
}

int main(int argc, char **argv) {
    CHECK(argc == 3);
    const char *bundled_path = argv[1];
    const char *scratch_path = argv[2];
    FILE *existing = fopen(scratch_path, "rb");
    if (existing) {
        fclose(existing);
        fprintf(stderr, "Refusing to overwrite the existing scratch file.\n");
        return 1;
    }

    /* Real revision-225 fixture: assert signed values and archive ordering. */
    const int expected[CACHE_CRC_COUNT] = {
        0, -430779560, 511217062, 1614084464, -343404987,
        -2000991154, 1703545114, 1570981179, -1532605973
    };
    int checksums[CACHE_CRC_COUNT];
    CHECK(cachecrc_load(bundled_path, checksums) == CACHE_CRC_OK);
    CHECK(memcmp(checksums, expected, sizeof(expected)) == 0);

    check_failure(scratch_path, CACHE_CRC_OPEN_ERROR);

    uint8_t bytes[CACHE_CRC_BYTES + 1] = {0};
    /* Signed boundaries, a recognisable byte order, and a valid zero final CRC. */
    bytes[4] = 0x80;
    bytes[8] = 0x7f;
    bytes[9] = bytes[10] = bytes[11] = 0xff;
    bytes[12] = bytes[13] = bytes[14] = bytes[15] = 0xff;
    bytes[16] = 0x12;
    bytes[17] = 0x34;
    bytes[18] = 0x56;
    bytes[19] = 0x78;
    write_fixture(scratch_path, bytes, CACHE_CRC_BYTES);
    CHECK(cachecrc_load(scratch_path, checksums) == CACHE_CRC_OK);
    CHECK(checksums[1] == INT32_MIN);
    CHECK(checksums[2] == INT32_MAX);
    CHECK(checksums[3] == -1);
    CHECK(checksums[4] == 0x12345678);
    CHECK(checksums[8] == 0);

    write_fixture(scratch_path, bytes, 0);
    check_failure(scratch_path, CACHE_CRC_SIZE_ERROR);
    write_fixture(scratch_path, bytes, CACHE_CRC_BYTES - 1);
    check_failure(scratch_path, CACHE_CRC_SIZE_ERROR);
    write_fixture(scratch_path, bytes, CACHE_CRC_BYTES + 1);
    check_failure(scratch_path, CACHE_CRC_SIZE_ERROR);

    bytes[3] = 1;
    write_fixture(scratch_path, bytes, CACHE_CRC_BYTES);
    check_failure(scratch_path, CACHE_CRC_HEADER_ERROR);
    CHECK(remove(scratch_path) == 0);
    puts("PASS: bundled CRCs, byte order, signed limits, zero CRC, missing and malformed files.");
    return 0;
}
