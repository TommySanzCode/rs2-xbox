#if defined(NXDK)
#include <hal/debug.h>
#include <hal/xbox.h>
#include <nxdk/mount.h>
#include <nxdk/net.h>
#include <windows.h>

#include <SDL.h>
#include <hal/video.h>
#include <stdlib.h>

#include "../client.h"
#include "../custom.h"
#include "../gameshell.h"
#include "../inputtracking.h"
#include "../pixmap.h"
#include "../platform.h"
#include "../xboxdisplay.h"
#include "../xboxaudio.h"
#include "../thirdparty/ini.h"
#include "../thirdparty/bzip.h"

extern ClientData _Client;
extern InputTracking _InputTracking;
extern Custom _Custom;

volatile static uint32_t *rgbx;

/* Pointer positions stay in the client's logical coordinates. The renderer
 * scales both panels and the pointer with exactly the same transform. */
static int cursor_x = SCREEN_WIDTH / 2;
static int cursor_y = SCREEN_HEIGHT / 2;
static XboxDisplay display;
static uint32_t display_canvas[SCREEN_WIDTH * SCREEN_HEIGHT];
static int display_inset = 16;
static int framebuffer_width = 640, framebuffer_height = 480;
#if XBOX_ENHANCED
static SDL_AudioDeviceID audio_device;
static float music_volume = 1.0f;
static int effects_volume = 128;
static bool music_enabled = true;

static void audio_callback(void *userdata, Uint8 *stream, int length) {
    (void)userdata;
    memset(stream, 0, length);
    xbox_audio_render((int16_t *)stream, length / 4);
}

static void audio_start(void) {
    int enabled = 1, midi = 1;
    ini_t *config = ini_load("D:\\config.ini");
    if (config) {
        ini_sget(config, NULL, "xbox_audio", "%d", &enabled);
        ini_sget(config, NULL, "xbox_music", "%d", &midi);
        ini_free(config);
    }
    music_enabled = midi != 0;
    if (!enabled || _Client.lowmem) return;
    if (SDL_InitSubSystem(SDL_INIT_AUDIO) < 0) {
        debugPrint("Audio init failed: %s\n", SDL_GetError());
        return;
    }
    if (music_enabled && !xbox_audio_init("D:\\TimGM6mb.sf2")) {
        debugPrint("Music unavailable: check TimGM6mb.sf2. Effects remain enabled.\n");
    }
    SDL_AudioSpec requested = {0}, obtained = {0};
    requested.freq = 48000;
    requested.format = AUDIO_S16LSB;
    requested.channels = 2;
    requested.samples = 1024;
    requested.callback = audio_callback;
    audio_device = SDL_OpenAudioDevice(NULL, 0, &requested, &obtained, 0);
    if (!audio_device || obtained.freq != 48000 || obtained.format != AUDIO_S16LSB || obtained.channels != 2) {
        debugPrint("Audio open failed: %s\n", SDL_GetError());
        if (audio_device) SDL_CloseAudioDevice(audio_device);
        audio_device = 0;
        xbox_audio_shutdown();
        return;
    }
    SDL_PauseAudioDevice(audio_device, 0);
}
#endif
uint32_t xbox_last_present_ms;

#define CURSOR_W 12
#define CURSOR_H 18
#define CURSOR_SENSITIVITY 5000
#define CAMERA_DEADZONE 12000

static const unsigned char cursor[] = {
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff,
    0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xff,
    0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff,
    0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0xff, 0x00, 0x00, 0x01, 0xff,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};

SDL_GameController *pad = NULL;

bool platform_init(void) {
    if (!XVideoSetMode(640, 480, 32, REFRESH_DEFAULT)) return false;
#if XBOX_ENHANCED
    MM_STATISTICS memory = {0};
    memory.Length = sizeof(memory);
    if (MmQueryStatistics(&memory) != 0 || memory.TotalPhysicalPages < 96 * 256) {
        debugPrint("RS2 128 MB build needs expanded RAM exposed by the BIOS.\nUse the 64 MB build on this console.\n");
        Sleep(12000);
        return false;
    }
    int video = 480;
    ini_t *config = ini_load("D:\\config.ini");
    if (config) {
        ini_sget(config, NULL, "xbox_video", "%d", &video);
        ini_free(config);
    }
    if (video == 720) {
        if (XVideoSetMode(1280, 720, 32, REFRESH_DEFAULT)) {
            framebuffer_width = 1280;
            framebuffer_height = 720;
        } else {
            if (!XVideoSetMode(640, 480, 32, REFRESH_DEFAULT)) return false;
            debugPrint("720p unavailable; using 480 output.\n");
        }
    }
#endif
    rgbx = (uint32_t *)XVideoGetFB();
    if (!xbox_display_init(&display, SCREEN_WIDTH, SCREEN_HEIGHT,
                           framebuffer_width, framebuffer_height, display_inset, display_canvas)) {
        return false;
    }
    for (int i = 0; i < framebuffer_width * framebuffer_height; i++) {
        rgbx[i] = 0;
    }

    /* debugPrint("Content of D:\\\n");

    WIN32_FIND_DATA findFileData;
    HANDLE hFind;

    // Like on Windows, "*.*" and "*" will both list all files,
    // no matter whether they contain a dot or not
    hFind = FindFirstFile("D:\\*.*", &findFileData);
    if (hFind == INVALID_HANDLE_VALUE) {
        debugPrint("FindFirstHandle() failed!\n");
        Sleep(5000);
        return 1;
    }

    do {
        if (findFileData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            debugPrint("Directory: ");
        } else {
            debugPrint("File     : ");
        }

        debugPrint("%s\n", findFileData.cFileName);
    } while (FindNextFile(hFind, &findFileData) != 0);

    debugPrint("\n");

    DWORD error = GetLastError();
    if (error == ERROR_NO_MORE_FILES) {
        debugPrint("Done!\n");
    } else {
        debugPrint("error: %lx\n", error);
    }

    FindClose(hFind);

    while (1) {
        Sleep(2000);
    } */

    if (SDL_Init(SDL_INIT_GAMECONTROLLER) < 0) {
        debugPrint("SDL_Init failed: %s\n", SDL_GetError());
    }
    return true;
}

void platform_new(GameShell *shell) {
    shell->mouse_x = cursor_x;
    shell->mouse_y = cursor_y;
#if XBOX_ENHANCED
    audio_start();
#endif
}

void platform_free(void) {
#if XBOX_ENHANCED
    if (audio_device) SDL_CloseAudioDevice(audio_device);
    audio_device = 0;
    xbox_audio_shutdown();
#endif
    nxNetShutdown();
    SDL_GameControllerClose(pad);
    SDL_Quit();
}
void platform_set_wave_volume(int wavevol) {
#if XBOX_ENHANCED
    effects_volume = wavevol;
    if (!audio_device) return;
    SDL_LockAudioDevice(audio_device);
    xbox_audio_volumes(music_volume, effects_volume);
    SDL_UnlockAudioDevice(audio_device);
#endif
}
void platform_play_wave(int8_t *src, int length) {
#if XBOX_ENHANCED
    if (!audio_device || length <= 0) return;
    SDL_LockAudioDevice(audio_device);
    xbox_audio_wave(src, (size_t)length);
    SDL_UnlockAudioDevice(audio_device);
#endif
}
void platform_set_midi_volume(float midivol) {
#if XBOX_ENHANCED
    music_volume = midivol;
    if (!audio_device) return;
    SDL_LockAudioDevice(audio_device);
    xbox_audio_volumes(music_volume, effects_volume);
    SDL_UnlockAudioDevice(audio_device);
#endif
}
void platform_set_jingle(int8_t *src, int len) {
#if XBOX_ENHANCED
    if (audio_device && music_enabled) {
        SDL_LockAudioDevice(audio_device);
        xbox_audio_music(src, len);
        SDL_UnlockAudioDevice(audio_device);
    }
#endif
    free(src); /* The game transfers ownership of the decoded jingle. */
}
void platform_set_midi(const char *name, int crc, int len) {
#if XBOX_ENHANCED
    if (!audio_device || !music_enabled || !name || !*name || strlen(name) > 64) return;
    for (const char *p = name; *p; ++p) {
        if (!((*p >= 'a' && *p <= 'z') || (*p >= 'A' && *p <= 'Z') ||
              (*p >= '0' && *p <= '9') || *p == '_' || *p == '-')) return;
    }
    char filename[128];
    snprintf(filename, sizeof(filename), "D:\\cache\\client\\songs\\%s.mid", name);
    FILE *file = fopen(filename, "rb");
    if (!file) return;
    if (fseek(file, 0, SEEK_END) != 0) { fclose(file); return; }
    long size = ftell(file);
    if (size < 5 || size > 1024 * 1024 || (crc != 12345678 && size != len)) { fclose(file); return; }
    rewind(file);
    int8_t *compressed = malloc((size_t)size);
    if (!compressed) { fclose(file); return; }
    size_t got = fread(compressed, 1, (size_t)size, file);
    fclose(file);
    if (got != (size_t)size || (crc != 12345678 && rs_crc32(compressed, got) != crc)) {
        free(compressed); return;
    }
    const uint8_t *header = (const uint8_t *)compressed;
    uint32_t decoded_size = (uint32_t)header[0] << 24 | (uint32_t)header[1] << 16 | (uint32_t)header[2] << 8 | header[3];
    if (decoded_size < 14 || decoded_size > 1024 * 1024) { free(compressed); return; }
    int8_t *decoded = malloc(decoded_size);
    if (decoded && bzip_decompress_checked(decoded, (int)decoded_size, compressed + 4, (int)size - 4)) {
        SDL_LockAudioDevice(audio_device);
        xbox_audio_music(decoded, (int)decoded_size);
        SDL_UnlockAudioDevice(audio_device);
    }
    free(decoded);
    free(compressed);
#endif
}
void platform_stop_midi(void) {
#if XBOX_ENHANCED
    if (!audio_device) return;
    SDL_LockAudioDevice(audio_device);
    xbox_audio_stop_music();
    SDL_UnlockAudioDevice(audio_device);
#endif
}
void platform_poll_events(Client *c) {
    static uint8_t prev_buttons[SDL_CONTROLLER_BUTTON_MAX];
    static bool camera_held[4];
    static const int camera_keys[4] = {K_LEFT, K_RIGHT, K_UP, K_DOWN};
    bool pad_changed = false;
    SDL_Event e;
    while (SDL_PollEvent(&e)) {
        if (e.type == SDL_CONTROLLERDEVICEADDED) {
            SDL_GameController *new_pad = SDL_GameControllerOpen(e.cdevice.which);
            if (!pad) {
                pad = new_pad;
                pad_changed = new_pad != NULL;
            }
        } else if (e.type == SDL_CONTROLLERDEVICEREMOVED) {
            SDL_GameController *removed_pad = SDL_GameControllerFromInstanceID(e.cdevice.which);
            if (pad && pad == removed_pad) {
                pad = NULL;
                pad_changed = true;
            }
            SDL_GameControllerClose(removed_pad);
        } else if (e.type == SDL_CONTROLLERBUTTONDOWN) {
            if (e.cbutton.button == SDL_CONTROLLER_BUTTON_START) {
                SDL_GameController *selected_pad = SDL_GameControllerFromInstanceID(e.cdevice.which);
                if (selected_pad && pad != selected_pad) {
                    pad = selected_pad;
                    pad_changed = true;
                }
            }
        }
    }

    SDL_GameControllerUpdate();

    if (pad_changed) {
        // Release the old controller before accepting input from a replacement.
        if (prev_buttons[SDL_CONTROLLER_BUTTON_X]) {
            key_released(c->shell, K_CONTROL, -1);
        }
        for (int button = SDL_CONTROLLER_BUTTON_A; button <= SDL_CONTROLLER_BUTTON_B; button++) {
            if (prev_buttons[button]) {
                c->shell->mouse_button = 0;
                if (_InputTracking.enabled) {
                    inputtracking_mouse_released(&_InputTracking, button == SDL_CONTROLLER_BUTTON_B ? 1 : 0);
                }
            }
        }
        memset(prev_buttons, 0, sizeof(prev_buttons));
        for (int direction = 0; direction < 4; direction++) {
            if (camera_held[direction]) {
                key_released(c->shell, camera_keys[direction], -1);
                camera_held[direction] = false;
            }
        }
    }

    uint8_t current_buttons[SDL_CONTROLLER_BUTTON_MAX];

    for (int i = 0; i < SDL_CONTROLLER_BUTTON_MAX; ++i) {
        current_buttons[i] = pad ? SDL_GameControllerGetButton(pad, i) : 0;
    }

    const bool logout_chord = current_buttons[SDL_CONTROLLER_BUTTON_BACK] && current_buttons[SDL_CONTROLLER_BUTTON_START];
    const bool previous_logout_chord = prev_buttons[SDL_CONTROLLER_BUTTON_BACK] && prev_buttons[SDL_CONTROLLER_BUTTON_START];
    if (logout_chord && !previous_logout_chord && c->ingame) {
        c->shell->idle_cycles = 0;
        client_logout(c);
    }

    for (int i = 0; i < SDL_CONTROLLER_BUTTON_MAX; ++i) {
        if (current_buttons[i] && !prev_buttons[i]) {
            switch (i) {
                case SDL_CONTROLLER_BUTTON_X:
                    key_pressed(c->shell, K_CONTROL, -1);
                    break;
                case SDL_CONTROLLER_BUTTON_Y:
                    _Custom.show_performance = !_Custom.show_performance;
                    break;
                case SDL_CONTROLLER_BUTTON_START:
                    if (!c->ingame && !current_buttons[SDL_CONTROLLER_BUTTON_BACK]) {
                        client_login(c, c->username, c->password, false);
                    }
                    break;
                case SDL_CONTROLLER_BUTTON_LEFTSHOULDER:
                    // Cycle fitted sizes for different amounts of TV overscan.
                    display_inset = display_inset == 16 ? 0 : (display_inset == 0 ? 32 : 16);
                    xbox_display_init(&display, SCREEN_WIDTH, SCREEN_HEIGHT,
                                      framebuffer_width, framebuffer_height, display_inset, display_canvas);
                    for (int pixel = 0; pixel < framebuffer_width * framebuffer_height; pixel++) {
                        rgbx[pixel] = 0;
                    }
                    c->redraw_background = true;
                    break;
                case SDL_CONTROLLER_BUTTON_A:
                case SDL_CONTROLLER_BUTTON_B:
                    c->shell->idle_cycles = 0;
                    c->shell->mouse_click_x = cursor_x;
                    c->shell->mouse_click_y = cursor_y;

                    if (i == SDL_CONTROLLER_BUTTON_A) {
                        c->shell->mouse_click_button = 1;
                        c->shell->mouse_button = 1;
                    } else {
                        c->shell->mouse_click_button = 2;
                        c->shell->mouse_button = 2;
                    }

                    if (_InputTracking.enabled) {
                        inputtracking_mouse_pressed(&_InputTracking, cursor_x, cursor_y, i == SDL_CONTROLLER_BUTTON_B ? 1 : 0);
                    }
                    break;
            }
        } else if (!current_buttons[i] && prev_buttons[i]) {
            switch (i) {
                case SDL_CONTROLLER_BUTTON_X:
                    key_released(c->shell, K_CONTROL, -1);
                    break;
                case SDL_CONTROLLER_BUTTON_A:
                case SDL_CONTROLLER_BUTTON_B:
                    c->shell->idle_cycles = 0;
                    c->shell->mouse_button = 0;

                    if (_InputTracking.enabled) {
                        inputtracking_mouse_released(&_InputTracking, i == SDL_CONTROLLER_BUTTON_B ? 1 : 0);
                    }
                    break;
            }
        }
    }

    memcpy(prev_buttons, current_buttons, sizeof(prev_buttons));

    const int camera_x = pad ? SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_RIGHTX) : 0;
    const int camera_y = pad ? SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_RIGHTY) : 0;
    const bool camera_down[4] = {
        c->ingame && camera_x <= -CAMERA_DEADZONE,
        c->ingame && camera_x >= CAMERA_DEADZONE,
        c->ingame && camera_y <= -CAMERA_DEADZONE,
        c->ingame && camera_y >= CAMERA_DEADZONE
    };
    for (int direction = 0; direction < 4; direction++) {
        if (camera_down[direction] != camera_held[direction]) {
            if (camera_down[direction]) {
                key_pressed(c->shell, camera_keys[direction], -1);
            } else {
                key_released(c->shell, camera_keys[direction], -1);
            }
            camera_held[direction] = camera_down[direction];
        }
    }

    const int axis_x = pad ? SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_LEFTX) : 0;
    const int axis_y = pad ? SDL_GameControllerGetAxis(pad, SDL_CONTROLLER_AXIS_LEFTY) : 0;
    // Black slows the pointer for the smaller controls in the fitted interface.
    const int sensitivity = current_buttons[SDL_CONTROLLER_BUTTON_RIGHTSHOULDER] ?
                            CURSOR_SENSITIVITY * 2 : CURSOR_SENSITIVITY;
    const int delta_x = axis_x / sensitivity;
    const int delta_y = axis_y / sensitivity;
    if (delta_x != 0 || delta_y != 0) {
        const int old_cursor_x = cursor_x;
        const int old_cursor_y = cursor_y;

        cursor_x = MAX(0, MIN(cursor_x + delta_x, SCREEN_WIDTH - 1));
        cursor_y = MAX(0, MIN(cursor_y + delta_y, SCREEN_HEIGHT - 1));

        const bool cursor_moved = cursor_x != old_cursor_x || cursor_y != old_cursor_y;
        if (cursor_moved) {
            // The presenter erases the old pointer from the retained image.
            c->shell->idle_cycles = 0;
        }

        if (cursor_moved) {
            c->shell->mouse_x = cursor_x;
            c->shell->mouse_y = cursor_y;
            if (_InputTracking.enabled) {
                inputtracking_mouse_moved(&_InputTracking, cursor_x, cursor_y);
            }
        }
    }

}
void platform_blit_surface(Surface *surface, int x, int y) {
    xbox_display_blit(&display, surface->pixels, surface->w, surface->h, x, y);
}
void platform_update_surface(void) {
    const uint32_t started = GetTickCount();
    xbox_display_present(&display, rgbx, cursor, CURSOR_W, CURSOR_H, cursor_x, cursor_y);
    xbox_last_present_ms = GetTickCount() - started;
}
uint64_t rs2_now(void) {
    return GetTickCount();
}
void rs2_sleep(int ms) {
    Sleep(ms);
}
#endif
