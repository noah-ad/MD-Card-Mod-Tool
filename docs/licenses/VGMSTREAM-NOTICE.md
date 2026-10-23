# vgmstream r2117

The Windows distribution includes the unmodified official `vgmstream-win64.zip`
tools in `data/tools/vgmstream/`. They run as a separate local process to decode
Master Duel FSB5 audio into WAV for preview. The application does not link to them.

- Project and corresponding source: https://github.com/vgmstream/vgmstream/tree/r2117
- Original binary: https://github.com/vgmstream/vgmstream/releases/tag/r2117
- Archive SHA-256: `6c4a8a3813864fefed081bbd337dbc0ad93bf88e0b92f5db98d7ab258b22dc6c`
- Reproduce the dependency folder: `tools/GetDuelAudioTools.ps1`.

The original `COPYING`, README and usage guide accompany the binaries. Copyright
and permission notices for vgmstream and its supporting libraries are retained
beside the tools, including the LGPL notices for FFmpeg and mpg123 and the notices
for Vorbis, Ogg, CELT, Opus, Speex and LibAtrac9. Source for the supporting libraries
and Windows build inputs is referenced by the tagged vgmstream source tree and
its `ext_libs` and `cmake/dependencies` directories. These components are not
modified by MD Card Mod Tool. Replaceable DLLs remain separate from the program.

No FMOD SDK or proprietary FMOD runtime is redistributed. Generated custom music
uses an uncompressed PCM FSB5 container and still requires game compatibility
testing; local decode validation is not an in-game playback acceptance test.
