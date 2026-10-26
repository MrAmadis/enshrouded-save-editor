# Third-party components

Components used by the Enshrouded Save Editor, with their licenses. Entries are added as they are introduced.

## Kimikozi, Enshrouded Save Editor (MIT)

- **Source:** https://codeberg.org/Kimikozi/enshrouded-save-editor, commit `1f9699717b5fbc1ea35df94abebadc0c2a83a9da`.
- **What we use:** the approach of `lib/pc.js` (`parsePlayer`, `readRecord`, `readRun`), ported to C# in `src/EnshroudedSaveEditor.Core/Records/ItemRecord.cs` and `InventoryScanner.cs`. That covers the item record layout and field sizes, and the fallback search for the Equipped run and the `[type][n2][type][n3]` inventory header.
- **Reference data:** `data/itemdb2.json` and `data/progress.json` are included unmodified in `src/EnshroudedSaveEditor.GameData/Reference/` and embedded in the application as its item and skill-tree reference data (also used by the tests). The game data itself (item names, stats, the skill tree) belongs to Keen Games; Kimikozi's MIT license covers only their compilation and tooling.

```
MIT License

Copyright (c) 2026 the Enshrouded Save Editor author

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Cinzel and Source Sans 3 (SIL Open Font License 1.1)

- **Cinzel:** copyright 2020 The Cinzel Project Authors (https://github.com/NDISCOVER/Cinzel). License text: `src/EnshroudedSaveEditor.UI/wwwroot/fonts/OFL-Cinzel.txt`.
- **Source Sans 3:** copyright 2010-2020 Adobe, with Reserved Font Name "Source". License text: `src/EnshroudedSaveEditor.UI/wwwroot/fonts/OFL-SourceSans3.txt`.
- **What we use:** the latin-subset variable woff2 files as served by Google Fonts (`Cinzel.woff2`, `SourceSans3.woff2`), unmodified, bundled with the app so it needs no network. They are used for headings and body text.

## ZstdSharp.Port (MIT)

- **Source:** https://github.com/oleg-st/ZstdSharp, NuGet package `ZstdSharp.Port` 0.8.6, unmodified and bundled with the app. It is a C# port of the Zstandard (zstd) library.
- **What we use:** zstd compression and decompression of the save file's sections (`src/EnshroudedSaveEditor.Core/Container/KsdSection.cs`), and decompression of the game's resource chunks when the install's data is read (`src/EnshroudedSaveEditor.GameData/Kfc/KfcArchive.cs`).

```
MIT License

Copyright (c) 2021 Oleg Stepanischev

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## System.IO.Hashing (MIT)

- **Source:** Microsoft's .NET (https://github.com/dotnet/runtime), NuGet package `System.IO.Hashing` 10.0.0, unmodified and bundled with the app.
- **What we use:** XxHash3 for the save file's section checksums (`src/EnshroudedSaveEditor.Core/Container/KsdChecksum.cs`), and CRC-32 for the icon PNG files (`src/EnshroudedSaveEditor.GameData/Icons/Png.cs`).

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Enshrouded's game icon (Keen Games)

- **What we use:** `src/EnshroudedSaveEditor.App/app.ico`, the application's icon, is the icon of `enshrouded.exe` recoloured: the stone tablet in bronze and the Flame sigil in the editor's pale gold (owner's choice, 2026-10-26). Each size was recoloured from the game's own image at that size.
- **License:** the artwork belongs to Keen Games. It is used here only as the icon of this personal, unpublished tool, and is the one piece of game content committed to this repo (owner's decision, 2026-10-26).
