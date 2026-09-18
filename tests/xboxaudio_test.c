/* Host test: compile with src/xboxaudio.c and -DXBOX_RAM_MB=128, then pass
 * rom/TimGM6mb.sf2. Exercises synthesized music and the real WAV mixer. */
#include "../src/xboxaudio.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static void put32(uint8_t *p, unsigned v) {
    for (int i = 0; i < 4; ++i) p[i] = (uint8_t)(v >> (i * 8));
}
static void wave(uint8_t *data, unsigned count, uint8_t sample) {
    memset(data, 0, 44 + count);
    memcpy(data, "RIFF", 4); put32(data + 4, 36 + count);
    memcpy(data + 8, "WAVEfmt ", 8); put32(data + 16, 16);
    data[20] = data[22] = data[32] = 1;
    put32(data + 24, 22050); put32(data + 28, 22050); data[34] = 8;
    memcpy(data + 36, "data", 4); put32(data + 40, count);
    memset(data + 44, sample, count);
}
static int peak(const int16_t *data, int n) {
    int value = 0;
    for (int i = 0; i < n; ++i) if (abs(data[i]) > value) value = abs(data[i]);
    return value;
}
int main(int argc, char **argv) {
    assert(argc == 2);
    int16_t out[4800 * 2];
    uint8_t wav[44 + 2205];
    xbox_audio_shutdown();
    xbox_audio_render(out, 4800);
    assert(peak(out, 9600) == 0);
    wave(wav, 2205, 255);
    assert(xbox_audio_wave(wav, sizeof(wav)));
    memset(wav + 44, 0, 2205); /* Caller reuses its buffer immediately. */
    xbox_audio_render(out, 4800);
    for (int i = 0; i < 9600; ++i) assert(out[i] == 127 * 128);
    xbox_audio_render(out, 1);
    assert(out[0] == 0 && out[1] == 0); /* Exactly 100 ms at 48 kHz. */

    wave(wav, 2205, 255);
    for (int i = 0; i < 4; ++i) assert(xbox_audio_wave(wav, sizeof(wav)));
    xbox_audio_render(out, 1);
    assert(out[0] == 32767 && out[1] == 32767);
    xbox_audio_volumes(1, 0);
    xbox_audio_render(out, 1);
    assert(out[0] == 0 && out[1] == 0);
    wav[34] = 16;
    assert(!xbox_audio_wave(wav, sizeof(wav)));
    assert(!xbox_audio_wave(wav, 12));
    assert(!xbox_audio_wave(NULL, sizeof(wav)));
    xbox_audio_shutdown();

    assert(!xbox_audio_init("missing-test-soundfont.sf2"));
    assert(xbox_audio_init(argv[1]));
    const uint8_t midi[] = {
        'M','T','h','d', 0,0,0,6, 0,0, 0,1, 0,96,
        'M','T','r','k', 0,0,0,15,
        0,0xc0,0, 0,0x90,60,100, 96,0x80,60,0, 0,0xff,0x2f,0
    };
    assert(xbox_audio_music(midi, sizeof(midi)));
    assert(!xbox_audio_music("invalid", 7));
    xbox_audio_volumes(1, 128);
    xbox_audio_render(out, 4800);
    assert(peak(out, 9600) > 100);
    xbox_audio_volumes(0, 128);
    xbox_audio_render(out, 100);
    assert(peak(out, 200) == 0);
    xbox_audio_volumes(1, 128);
    xbox_audio_render(out, 100);
    assert(peak(out, 200) > 100);
    xbox_audio_stop_music();
    xbox_audio_render(out, 4800);
    /* tsf's quick release is allowed to drain before silence. */
    xbox_audio_render(out, 4800);
    assert(peak(out, 9600) == 0);
    for (int i = 0; i < 100; ++i) {
        assert(xbox_audio_music(midi, sizeof(midi)));
        xbox_audio_render(out, 127); /* Odd callback sizes preserve phase. */
        xbox_audio_stop_music();
    }
    xbox_audio_shutdown();
    xbox_audio_shutdown();
    puts("PASS: music synthesis/replacement/stop, WAV ownership, duration, stereo, clipping, mute, invalid inputs and shutdown.");
    return 0;
}
