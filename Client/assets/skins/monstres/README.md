# Skin « Monstres » : où remplacer les images

Les 4 créatures perchées sur le bord de la console (elles sautillent à tour de rôle, et scintillent de temps en temps) :

| Fichier | Taille d'origine | Place |
|---|---|---|
| `creature_electrique.png` | 66 × 65 | en haut à gauche |
| `creature_eau.png` | 90 × 70 | en haut, 2e |
| `creature_plante.png` | 78 × 75 | en haut, 3e |
| `creature_feu.png` | 90 × 73 | en haut à droite |

Pour changer une créature : remplace le fichier **en gardant le même nom**, en PNG à fond transparent, en pixel art, de taille proche de celle du tableau. Le saut et le scintillement
suivent le fichier, rien d'autre à régler. Les pastilles de type devant les noms (`badge_feu.png`, `badge_eau.png`, `badge_plante.png`, `badge_electrique.png`, environ 30 × 30) et la capsule
(`boule.png`) se remplacent de la même façon.

Pour essayer sans toucher au dépôt : mets le fichier du même nom dans `%APPDATA%\Aion DPS Meter (Stacy)\skins\monstres\` (il passe avant celui du thème, et reste sur ton PC).

Les pièces du cadre (`frame_*.png`) et les voyants sont générés par `Tools/skin-pack` : ne pas les modifier à la main.

## Plusieurs images par créature (une par jour)

Pour qu'une créature change chaque jour, ajoute des images du même nom avec `_2`, `_3`... avant l'extension : `creature_feu.png`, `creature_feu_2.png`, `creature_feu_3.png` (jusqu'à 30).
Le meter en choisit une selon le jour de l'année, avec un pas différent pour chacune des quatre créatures. Les images supplémentaires peuvent être dans ce dossier, ou dans
`%APPDATA%\Aion DPS Meter (Stacy)\skins\monstres\` pour rester sur ton PC.

## Les badges de rang (barre d'expérience du dresseur)

`rang_1.png` à `rang_30.png` (66 × 66, pixel art, fond transparent ; dessinés par Gemini) : un badge par niveau, du 1er au 30e. Ils se lisent en grille de 5 colonnes × 6 lignes : la forme change de gauche à droite (rond, losange, étoile, bouclier, flamme), la matière de haut en bas (bronze, argent, or, platine, cristal, arc-en-ciel). Le niveau N est la ligne (N-1)/5, colonne (N-1)%5. Au-delà du niveau 30, le 30e badge reste. Le 30e brille tout seul (halo doré et étoile qui scintille). Pour les remplacer : même nom de fichier.
