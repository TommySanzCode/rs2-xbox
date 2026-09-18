#include "xboxprofile.h"
#if XBOX_ENHANCED
#include "xboxaudio.h"
#include <stdlib.h>
#include <string.h>

#define TSF_IMPLEMENTATION
#include "thirdparty/tsf.h"
#define TML_IMPLEMENTATION
#include "thirdparty/tml.h"

enum { SYNTH_RATE = 24000, OUTPUT_RATE = 48000, BLOCK = 64, EFFECTS = 4,
       MAX_WAVE = 1024 * 1024, MAX_MIDI = 1024 * 1024 };
typedef struct {
    uint8_t *samples;
    size_t count, position;
    unsigned phase, rate;
} Effect;
static tsf *font;
static tml_message *song, *event;
static uint64_t song_frames;
static Effect effects[EFFECTS];
static unsigned next_effect;
static int wave_volume = 128;
static int music_gain = 32768;
static int16_t music_block[BLOCK * 2];
static int output_position = BLOCK * 2;

static unsigned le16(const uint8_t *p) { return p[0] | (unsigned)p[1] << 8; }
static uint32_t le32(const uint8_t *p) { return le16(p) | (uint32_t)le16(p + 2) << 16; }

bool xbox_audio_init(const char *soundfont) {
    xbox_audio_shutdown();
    if (!soundfont) return false;
    font = tsf_load_filename(soundfont);
    if (!font) return false;
    tsf_set_output(font, TSF_STEREO_INTERLEAVED, SYNTH_RATE, -9.0f);
    if (!tsf_set_max_voices(font, 32)) {
        xbox_audio_shutdown();
        return false;
    }
    xbox_audio_stop_music();
    return font != NULL;
}

void xbox_audio_shutdown(void) {
    tml_free(song);
    song = event = NULL;
    tsf_close(font);
    font = NULL;
    for (int i = 0; i < EFFECTS; ++i) {
        free(effects[i].samples);
        memset(&effects[i], 0, sizeof(effects[i]));
    }
    song_frames = 0;
    next_effect = 0;
    wave_volume = 128;
    music_gain = 32768;
    output_position = BLOCK * 2;
    memset(music_block, 0, sizeof(music_block));
}

void xbox_audio_stop_music(void) {
    tml_free(song);
    song = event = NULL;
    song_frames = 0;
    output_position = BLOCK * 2;
    memset(music_block, 0, sizeof(music_block));
    if (!font) return;
    tsf_reset(font);
    /* Allocate all channels now; MIDI events in render cannot grow the array. */
    for (int channel = 0; channel < 16; ++channel) {
        if (!tsf_channel_set_presetnumber(font, channel, 0, channel == 9)) {
            tsf_close(font);
            font = NULL;
            return;
        }
        tsf_channel_sounds_off_all(font, channel);
    }
}

bool xbox_audio_music(const void *midi, int length) {
    if (!font || !midi || length < 14 || length > MAX_MIDI || memcmp(midi, "MThd", 4)) return false;
    tml_message *loaded = tml_load_memory(midi, length);
    if (!loaded) return false;
    xbox_audio_stop_music();
    if (!font) { tml_free(loaded); return false; }
    song = event = loaded;
    return true;
}

bool xbox_audio_wave(const void *wav, size_t length) {
    const uint8_t *bytes = wav, *samples = NULL;
    size_t count = 0;
    unsigned rate = 0;
    bool format_ok = false;
    if (!bytes || length < 12 || length > MAX_WAVE + 128 ||
        memcmp(bytes, "RIFF", 4) || memcmp(bytes + 8, "WAVE", 4) ||
        le32(bytes + 4) != length - 8) return false;
    for (size_t pos = 12; pos + 8 <= length;) {
        uint32_t size = le32(bytes + pos + 4);
        if (size > length - pos - 8) return false;
        const uint8_t *data = bytes + pos + 8;
        if (!memcmp(bytes + pos, "fmt ", 4)) {
            if (size < 16 || le16(data) != 1 || le16(data + 2) != 1 ||
                le16(data + 12) != 1 || le16(data + 14) != 8) return false;
            rate = le32(data + 4);
            format_ok = rate >= 8000 && rate <= OUTPUT_RATE;
        } else if (!memcmp(bytes + pos, "data", 4)) {
            samples = data;
            count = size;
        }
        pos += 8 + (size_t)size + (size & 1);
    }
    if (!format_ok || !samples || !count || count > MAX_WAVE) return false;
    uint8_t *copy = malloc(count);
    if (!copy) return false;
    memcpy(copy, samples, count); /* The game reuses its WAV scratch buffer. */
    Effect *effect = &effects[next_effect++ % EFFECTS];
    free(effect->samples);
    *effect = (Effect){copy, count, 0, 0, rate};
    return true;
}

void xbox_audio_volumes(float music, int wave) {
    if (!(music >= 0.0f)) music = 0.0f;
    if (music > 1.0f) music = 1.0f;
    wave_volume = wave < 0 ? 0 : (wave > 128 ? 128 : wave);
    /* Apply master volume after synthesis so held notes change immediately. */
    music_gain = (int)(music * 32768.0f);
}

static void dispatch(const tml_message *message) {
    if (message->channel >= 16) return;
    switch (message->type) {
        case TML_PROGRAM_CHANGE:
            tsf_channel_set_presetnumber(font, message->channel, message->program, message->channel == 9); break;
        case TML_NOTE_ON:
            tsf_channel_note_on(font, message->channel, message->key, message->velocity / 127.0f); break;
        case TML_NOTE_OFF:
            tsf_channel_note_off(font, message->channel, message->key); break;
        case TML_PITCH_BEND:
            tsf_channel_set_pitchwheel(font, message->channel, message->pitch_bend); break;
        case TML_CONTROL_CHANGE:
            tsf_channel_midi_control(font, message->channel, message->control, message->control_value); break;
    }
}

static void synth_block(void) {
    memset(music_block, 0, sizeof(music_block));
    if (!font || !song) return;
    /* Event boundaries are at most 2.67 ms apart. Tracks play once, matching
     * the client protocol; the game requests the next track after a jingle. */
    while (event && (uint64_t)event->time * SYNTH_RATE <= song_frames * 1000) {
        dispatch(event);
        event = event->next;
    }
    tsf_render_short(font, music_block, BLOCK, 0);
    song_frames += BLOCK;
}

void xbox_audio_render(int16_t *stereo, int frames) {
    if (!stereo || frames <= 0) return;
    for (int frame = 0; frame < frames; ++frame) {
        if (output_position == BLOCK * 2) { synth_block(); output_position = 0; }
        const int sample = (output_position++ / 2) * 2;
        int wave = 0;
        for (int i = 0; i < EFFECTS; ++i) {
            Effect *e = &effects[i];
            if (e->position >= e->count) continue;
            int first = (int)e->samples[e->position] - 128;
            int second = e->position + 1 < e->count ? (int)e->samples[e->position + 1] - 128 : first;
            wave += (first * (OUTPUT_RATE - (int)e->phase) + second * (int)e->phase) / OUTPUT_RATE;
            e->phase += e->rate;
            e->position += e->phase / OUTPUT_RATE;
            e->phase %= OUTPUT_RATE;
        }
        wave *= wave_volume; /* At most four effects; saturate instead of wrapping. */
        for (int channel = 0; channel < 2; ++channel) {
            int mixed = music_block[sample + channel] * music_gain / 32768 + wave;
            stereo[frame * 2 + channel] = (int16_t)(mixed < -32768 ? -32768 : mixed > 32767 ? 32767 : mixed);
        }
    }
}
#endif
