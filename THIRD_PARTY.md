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
