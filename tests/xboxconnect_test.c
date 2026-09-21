#include "../src/xboxconnect.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>
int main(void) {
    XboxConnectReply result = {0};
    const char *nonce = "0123456789abcdef";
    char message[512], modulus[257]; memset(modulus, 'a', 256); modulus[256] = 0;
    assert(xbox_connect_parse("RS2CONNECT2 0123456789abcdef PAIR", nonce, &result) == 2);
    snprintf(message, sizeof(message), "RS2CONNECT2 %s OK 43597 010001 %s 1", nonce, modulus);
    assert(xbox_connect_parse(message, nonce, &result) == 1 && result.port == 43597 && result.members == 1);
    assert(xbox_connect_parse(message, "ffffffffffffffff", &result) == 0);
    strcat(message, " extra"); assert(xbox_connect_parse(message, nonce, &result) == 0);
    snprintf(message, sizeof(message), "RS2CONNECT2 %s OK 80 010001 %s 1", nonce, modulus);
    assert(xbox_connect_parse(message, nonce, &result) == 0);
    assert(xbox_connect_parse("RS2CONNECT2 0123456789abcdef OK 43597 01 short 1", nonce, &result) == 0);
    puts("PASS: Xbox discovery validates version, nonce, key bounds, port and complete response");
}
