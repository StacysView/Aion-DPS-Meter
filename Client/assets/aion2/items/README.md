# Aion-2-Gegenstandsdaten

`item_info.json` — Gegenstands-ID -> `[englischer Name, Slot, Grad, Tier, Itemlevel]`. Nur Fakten
(IDs, Namen, Slot, Grad, Tier, Itemlevel), abgeleitet aus den Spieldaten, die der installierte
AionFlex-Client mitbringt (`data/items.json`, Stand 2026-10-01); keine Bilder, keine Texte, keine
Statistik-Tabellen. Genutzt von `Aion2ItemCatalog` fuer die Ansicht "Charakter".

`item_names_i18n.json` — Gegenstands-ID -> `{"fr","de","es","ru"}`-Namen. Erzeugt mit
`Tools/loot/build_item_names.py` aus `item_info.json` und der Namensuebersetzung von SkeeveAN
(MIT, https://github.com/SkeeveAN, Datei `item_names_i18n.json`, nach dem englischen Namen
nachgeschlagen). Nur Ids, deren englischer Name dort vorkommt. Genutzt von `Aion2ItemNames`.

`item_names.json` - Gegenstands-ID -> `[Grad, Name en, de, fr, es, ru, pt, ja, ko]`, aus dem Repository von SkeeveAN (MIT). Genutzt von `Aion2ItemTable` (Aion2Stock: Inventar des Spielers, u.a. die Schluessel der versteckten Wuerfel).
