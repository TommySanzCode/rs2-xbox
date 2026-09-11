#pragma once

enum { CACHE_CRC_COUNT = 9, CACHE_CRC_BYTES = CACHE_CRC_COUNT * 4 };

typedef enum {
    CACHE_CRC_OK,
    CACHE_CRC_OPEN_ERROR,
    CACHE_CRC_READ_ERROR,
    CACHE_CRC_SIZE_ERROR,
    CACHE_CRC_HEADER_ERROR
} CacheCrcStatus;

/* Load nine big-endian checksums. Leave checksums unchanged on failure. */
CacheCrcStatus cachecrc_load(const char *path, int checksums[CACHE_CRC_COUNT]);
const char *cachecrc_status_message(CacheCrcStatus status);
