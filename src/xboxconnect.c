#include "xboxconnect.h"
#include <stdio.h>
#include <string.h>

static bool hex_string(const char *value, size_t minimum, size_t maximum) {
    size_t length = strlen(value);
    if (length < minimum || length > maximum || length % 2) return false;
    return strspn(value, "0123456789abcdefABCDEF") == length;
}

int xbox_connect_parse(const char *message, const char *nonce, XboxConnectReply *reply) {
    char prefix[48];
    if (!hex_string(nonce, 16, 16)) return 0;
    snprintf(prefix, sizeof(prefix), "RS2CONNECT2 %s ", nonce);
    size_t n = strlen(prefix);
    if (strncmp(message, prefix, n)) return 0;
    if (!strcmp(message + n, "PAIR")) return 2;
    XboxConnectReply parsed = {0}; int end = 0;
    if (sscanf(message + n, "OK %5d %16s %256s %1d%n", &parsed.port, parsed.exponent, parsed.modulus, &parsed.members, &end) != 4 ||
        message[n + end] != '\0' || parsed.port != 43597 || (parsed.members != 0 && parsed.members != 1) ||
        !hex_string(parsed.exponent, 2, 16) || !hex_string(parsed.modulus, 256, 256)) return 0;
    *reply = parsed;
    return 1;
}

#ifdef NXDK
#include <windows.h>
#include <lwip/sockets.h>
#include <lwip/inet.h>
#include "client.h"
#include "custom.h"
#include "platform.h"

extern ClientData _Client;
extern Custom _Custom;
static int connect_enabled = -1;
static char nonce[17];
static char message[96];

bool xbox_connect_login(Client *game) {
    if (connect_enabled < 0) connect_enabled = !strcmp(_Client.socketip, "auto");
    if (!connect_enabled) return true;
    /* Correlation/display value only, never a credential or encryption key. */
    if (!nonce[0]) snprintf(nonce, sizeof(nonce), "%08lx%08lx", (unsigned long)GetTickCount(), (unsigned long)(uintptr_t)game);
    game->title_screen_state = 2;
    game->login_message0 = "Finding RS2 Xbox Connect on your LAN...";
    game->login_message1 = "";
    client_draw_title_screen(game); platform_update_surface();
    int fd = socket(AF_INET, SOCK_DGRAM, 0);
    if (fd < 0) { game->login_message1 = "Network unavailable. Check the Xbox cable."; return false; }
    int broadcast = 1;
    setsockopt(fd, SOL_SOCKET, SO_BROADCAST, &broadcast, sizeof(broadcast));
    struct sockaddr_in target = {0}, selected = {0};
    target.sin_family = AF_INET; target.sin_port = htons(43596); target.sin_addr.s_addr = INADDR_BROADCAST;
    char request[48]; snprintf(request, sizeof(request), "RS2CONNECT2 %s", nonce);
    XboxConnectReply info = {0}; int result = 0; bool multiple = false;
    for (int attempt = 0; attempt < 2; attempt++) {
        sendto(fd, request, strlen(request), 0, (struct sockaddr *)&target, sizeof(target));
        uint32_t deadline = GetTickCount() + 900;
        while ((int32_t)(deadline - GetTickCount()) > 0) {
            fd_set read_set; FD_ZERO(&read_set); FD_SET(fd, &read_set);
            struct timeval wait = {0, 100000};
            if (select(fd + 1, &read_set, NULL, NULL, &wait) <= 0) continue;
            char response[384]; struct sockaddr_in from = {0}; socklen_t from_length = sizeof(from);
            int length = recvfrom(fd, response, sizeof(response) - 1, 0, (struct sockaddr *)&from, &from_length);
            if (length <= 0 || (size_t)length >= sizeof(response) - 1) continue;
            response[length] = '\0';
            if ((int)strlen(response) != length) continue;
            XboxConnectReply parsed = {0}; int status = xbox_connect_parse(response, nonce, &parsed);
            if (!status) continue;
            if (result && selected.sin_addr.s_addr != from.sin_addr.s_addr) { multiple = true; break; }
            result = status; info = parsed; selected = from;
        }
        if (result || multiple) break;
    }
    closesocket(fd);
    game->login_message0 = "RS2 Xbox Connect";
    if (multiple) { game->login_message1 = "Multiple PCs found. Stop the other connection."; return false; }
    if (!result) { game->login_message1 = "Start Connect on your PC; check Private firewall."; return false; }
    if (result == 2) {
        snprintf(message, sizeof(message), "Approve Xbox code %.6s in Connect, then Start.", nonce + 10);
        game->login_message1 = message; return false;
    }
    snprintf(_Client.socketip, sizeof(_Client.socketip), "%s", inet_ntoa(selected.sin_addr));
    _Client.portoff = info.port - 43594;
    strcpy(_Client.rsa_exponent, info.exponent); strcpy(_Client.rsa_modulus, info.modulus);
    _Client.nodeid = 10; _Client.members = info.members != 0;
    strcpy(game->username, "connect"); strcpy(game->password, "connect");
    _Custom.remember_username = _Custom.remember_password = true;
    return true;
}
#endif
