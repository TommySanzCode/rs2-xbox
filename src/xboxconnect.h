#pragma once
#include <stdbool.h>
#include <stddef.h>

typedef struct {
    int port, members;
    char exponent[17], modulus[257];
} XboxConnectReply;

/* 1 = approved, 2 = awaiting PC approval, 0 = malformed/unrelated. */
int xbox_connect_parse(const char *message, const char *nonce, XboxConnectReply *reply);
#ifdef NXDK
struct Client;
bool xbox_connect_login(struct Client *game);
bool xbox_connect_active(void);
#endif
