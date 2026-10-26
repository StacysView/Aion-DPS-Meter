# Aion-2-Daevanion-Bretter

`nodes.json` — Knoten-ID -> `[Brett, Zeile, Spalte, Grad, Typ, Stat-Token bzw. Skill-ID, Wert bzw. Level]`
und Brett-ID -> `[Name, Klasse]`. Nur Fakten, abgeleitet aus den Spieldaten, die der installierte
AionFlex-Client mitbringt (`data/daevanion_boards.json`, Stand 2026-10-01). Genutzt von
`Aion2DaevanionCatalog`, um die im Login-Paket 0x26e2 gesendeten freigeschalteten Knoten
(Brett + Knoten-IDs) in Boni zu uebersetzen.

Stand 2026-10-26: `nodes.json` ist die Tabelle von SkeeveAN (MIT, https://github.com/SkeeveAN/Aion-DPS-Meter, Stand 2026-10-21, Commit 6a246b1b): Bretter von 11 mal 11
Feldern mit dem Start in der Mitte (Zeilen und Spalten 3 bis 13), das neue Ariel-Brett, alle Werte aus der Spieltabelle. Die alte Tabelle (Stand 2026-10-01, 15 mal 15
Felder) passte nicht mehr zu den echten Brettern: 16 bis 25 freigeschaltete Knoten pro Brett waren darin unbekannt.
