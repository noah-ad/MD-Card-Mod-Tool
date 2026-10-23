# YgoMaster field music metadata

Project: https://github.com/pixeltris/YgoMaster
Source: `YgoMaster/Data/Bgm.json`, commit `c24aa153f78c72006e904174043b1c0c553680da`.

`data/bgm-fields-v1.tsv` adapts the field item IDs and track associations from this
community-maintained data. Entries whose source explicitly marks the association
as uncertain, TODO, or a temporary substitute are excluded. This is reference
metadata, not a binding table extracted from the current official game. Shared
tracks affect every field that uses the same audio resource. Field names and the
item-to-model mapping are read separately from the user's installed game.

No game audio, field graphics, runtime injection, or YgoMaster executables are
included. Missing associations remain explicitly unknown.

MIT License

Copyright (c) 2022 pixeltris

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
