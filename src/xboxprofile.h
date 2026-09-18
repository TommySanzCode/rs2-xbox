#pragma once

/* Build-time capability, not a claim about the machine running this XBE. */
#ifndef XBOX_RAM_MB
#define XBOX_RAM_MB 64
#endif
#if XBOX_RAM_MB != 64 && XBOX_RAM_MB != 128
#error XBOX_RAM_MB must be 64 or 128
#endif
#define XBOX_ENHANCED (XBOX_RAM_MB == 128)
#define XBOX_DISPLAY_MAX_WIDTH (XBOX_ENHANCED ? 1280 : 640)
#define XBOX_DISPLAY_MAX_HEIGHT (XBOX_ENHANCED ? 720 : 480)
