#include "cachecrc.h"

#include <stdint.h>
#include <stdio.h>

CacheCrcStatus cachecrc_load(const char *path, int checksums[CACHE_CRC_COUNT]) {
    FILE *file = fopen(path, "rb");
    if (!file) {
        return CACHE_CRC_OPEN_ERROR;
    }

    /* The extra byte detects oversized files without seeking or allocating. */
    uint8_t bytes[CACHE_CRC_BYTES + 1];
    size_t size = fread(bytes, 1, sizeof(bytes), file);
    int read_error = ferror(file);
    int close_error = fclose(file);
    if (read_error || close_error != 0) {
        return CACHE_CRC_READ_ERROR;
    }
    if (size != CACHE_CRC_BYTES) {
        return CACHE_CRC_SIZE_ERROR;
    }
    /* Revision 225 reserves the first checksum for an unused archive. */
    if (bytes[0] || bytes[1] || bytes[2] || bytes[3]) {
        return CACHE_CRC_HEADER_ERROR;
    }

    for (int i = 0; i < CACHE_CRC_COUNT; ++i) {
        const uint8_t *word = bytes + i * 4;
        uint32_t value = ((uint32_t)word[0] << 24) | ((uint32_t)word[1] << 16) |
                         ((uint32_t)word[2] << 8) | word[3];
        /* Preserve signed protocol values without an out-of-range unsigned cast. */
        checksums[i] = value <= INT32_MAX ? (int)value : (int)((int64_t)value - INT64_C(4294967296));
    }
    return CACHE_CRC_OK;
}

const char *cachecrc_status_message(CacheCrcStatus status) {
    switch (status) {
        case CACHE_CRC_OK:
            return "Cache checksums loaded.";
        case CACHE_CRC_OPEN_ERROR:
            return "Could not open the cache checksum file.";
        case CACHE_CRC_READ_ERROR:
            return "Could not read the cache checksum file.";
        case CACHE_CRC_SIZE_ERROR:
            return "The cache checksum file must be exactly 36 bytes.";
        case CACHE_CRC_HEADER_ERROR:
            return "The first cache checksum must be zero.";
    }
    return "Invalid cache checksum file.";
}
