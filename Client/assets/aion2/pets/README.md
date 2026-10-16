# Pet data

`pets.json` is a byte-exact copy of `Client/assets/aion2/pets/pets.json` from SkeeveTV's Aion 2 DPS meter
(https://github.com/SkeeveTV, MIT licence), taken on 2026-10-16.

Layout: `pets` (id -> key, category = species, names per language), `monsters` (npc id -> pet id or list of
pet ids), `souls` (soul item id -> pet id), `shop`.

To refresh: in a clone of Skeeve's repository run
`git show origin/main:Client/assets/aion2/pets/pets.json > pets.json`, then run `Aion DPS.dll selftest`.

## Licence (Skeeve's notice)

MIT License

Copyright (c) 2026 SkeeveTV

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
