# Experimental 128 MB Xbox build

This profile requires an original Xbox with 128 MB RAM and a BIOS that exposes
the expanded memory to homebrew. The XBE clears nxdk's default 64 MB limit and
checks the kernel memory report before loading the client. If only 64 MB is
available, it displays an explanation and exits. Keep the stock build installed
in its own folder.

## Improvements and limits

| Feature | Stock 64 MB | Enhanced 128 MB |
| --- | --- | --- |
| Original texture detail | Low-memory 64×64 textures | Original 128×128 textures where supplied |
| Texture animation | Disabled | Enabled by original high-detail mode |
| Output | 640×480 | 640×480 default; optional 1280×720 |
| Music and jingles | Silent | General MIDI instruments using TimGM6mb |
| Sound effects | Silent | Up to four mixed effects |
| Scene allocation arena | 16 MiB | 32 MiB in high-detail mode |

This enables the original client's detail settings; it does not replace models
or textures with new artwork. The logical interface and game viewport retain
their original dimensions. At 720p the entire interface fits without reducing
its resolution, with the existing aspect-preserving filter and cursor alignment.
It does not render additional world pixels or add a widescreen game viewport.

Music uses a 32-voice software synth at 24 kHz, converted to the Xbox backend's
48 kHz stereo output and mixed with resampled effects. There is no new reverb or
surround processing. Instrument timbres differ from some historical PC MIDI
devices. This is an experimental native audio implementation, not a claim of
hardware-verified audio quality or frame rate. The same 733 MHz CPU must perform
more graphics and audio work, so high detail can reduce frame rate.

## Install and configure

Preview 1 provides two enhanced downloads: `RS2-2004-Xbox-128MB-480.zip` and
`RS2-2004-Xbox-128MB-720p.zip`. They contain the **same executable and assets**;
only the requested output mode and its package metadata differ. Choose 480 for
the first test or 720p if your setup supports it. Both require expanded RAM.

Copy the entire extracted 128 MB folder to a separate directory on your Xbox
and launch its `default.xbe`. Keep `cache`, `Roboto`, and `TimGM6mb.sf2` beside it.
Use the same revision-225 server as the stock release; no server upgrade is
required. Keep your existing server, accounts, keys and saves. The two client
profiles can connect to that server at the same time using different accounts.
Set your local server/account fields in `config.ini`.

If you copy the server package's generated Xbox config, **set `lowmem = 0`** and
add the enhanced options below. The stock server launcher generates `lowmem = 1`.
Copy your server's RSA public fields as well if it uses a different key. Personal
addresses and login details must stay in your local copy.

```ini
lowmem = 0
xbox_video = 480
xbox_audio = 1
xbox_music = 1
```

- Set `xbox_video = 720` for a display/cable/BIOS configuration supporting 720p,
  with 720p enabled in the dashboard. An unavailable mode falls back to 480.
  If your display loses sync, change this file back to `480` on disk.
- Set `xbox_music = 0` to test effects alone, or `xbox_audio = 0` for silence.
- Set `lowmem = 1` for a graphics performance comparison. This also disables
  music/effects because the original client gates audio on its memory mode.
- Missing SoundFont or audio-device failure leaves the game usable. A missing
  SoundFont disables music; sound effects can still play.

Controls are unchanged: left stick cursor, right stick camera, A/B click, X run
modifier, Y performance display, Start configured login, Back+Start logout,
White fit size, Black+left stick precise cursor. Game settings control volume.

## Build

Use a separate checkout/build folder to keep both deliverables. Supply the
matching cache including `cache/client/songs`, then run:

```powershell
python scripts/prepare-xbox-audio.py
Copy-Item xbox-128-config.example.ini rom/config.ini
.\scripts\build-xbox.ps1 -RamMB 128
.\scripts\Package-Xbox.ps1 -PackageName RS2-2004-Xbox-128MB
# Or make a blank 720p preset from the same XBE:
.\scripts\Package-Xbox.ps1 -PackageName RS2-2004-Xbox-128MB-720p -VideoMode 720
```

Do not overwrite an already configured INI without saving it first. Profile
switches force recompilation of client objects. `-RamMB 64` selects the stock
profile. Both the XBE and ISO carry the same memory flag; the manifest records
the selected build profile. Default packaging uses a blank profile-specific INI.
`-VideoMode 720` only affects that package's blank configuration. It does not
alter the stock build or your local config. The public packages also include
their matching source as `Source.zip`, supplied through `-SourceArchive`.

## Validation before recommending it to other players

Host tests cover 480/720 output, all three fit sizes, thin strokes, cursor trails,
buffer guards, MIDI synthesis, WAV ownership/resampling/clipping, and bounded
decoding of every packaged music track. These checks cannot validate a real
Xbox's audio timing, memory headroom, video adapter, or BIOS.

On a 128 MB console, test title music, login, walking across regions, combat and
interface effects, level-up jingles, logout/login, and each White-button size.
Compare Y-display frame rates with music off and with lowmem enabled. Report
console revision/BIOS, output mode, FPS, any audio crackling, and any crash or
missing textures. Do not send your configured INI or credentials with reports.
