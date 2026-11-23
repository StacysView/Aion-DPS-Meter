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
