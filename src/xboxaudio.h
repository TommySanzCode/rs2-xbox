#pragma once
#include <stdbool.h>
#include <stdint.h>
#include <stddef.h>

/* Call these under the platform audio lock once playback has started.
 * Render never allocates, frees, reads files, or takes another lock. */
bool xbox_audio_init(const char *soundfont);
void xbox_audio_shutdown(void);
bool xbox_audio_music(const void *midi, int length);
void xbox_audio_stop_music(void);
bool xbox_audio_wave(const void *wav, size_t length);
void xbox_audio_volumes(float music, int wave);
void xbox_audio_render(int16_t *stereo, int frames);
