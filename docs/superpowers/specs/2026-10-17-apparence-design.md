# Apparence : thème, taille de police, langue, opacité (17/10/2026)

## Problème
Settings > Overlay > Apparence propose Thème (un seul choix « Sombre »), Taille de police,
Langue et Opacité. Depuis la refonte « classique moderne » (15-16/10), l'overlay, Paramètres,
Sorts (PlayerDetailsWindow) et Historique (FightHistoryWindow) écrivent leurs couleurs en dur
(#141C24→#0B1016, cartes #1A2430/#2A3846, violet #7B5CF0) et presque toutes leurs tailles de
texte en dur : le thème et la taille ne changent rien de visible. Langue et opacité marchaient déjà.

## Ce qu'on reprend de Skeeve (origin/main, MIT)
Son ThemeManager (déjà dans le fork) : un dictionnaire par thème (Ui/Themes/*.xaml) échangé à
chaud, fenêtres en {DynamicResource Brush.X}, contrôle « chaque thème définit chaque jeton » au
selftest. Son idée des tailles de texte de l'overlay en ressources mises à l'échelle
(AddOverlayFontSizes : taille × base/12). Ses thèmes Midnight/Elyos/Asmodian (noms, ton général).
Pas repris : son éditeur de thème, ses fichiers de thème perso, son thème clair (nos fenêtres
« classique moderne » posent des voiles blancs semi-transparents, pensés pour le sombre).

## Décisions
- 4 thèmes, tous variantes sombres du classique moderne : **Sombre** (identique à aujourd'hui,
  au pixel près), **Nuit** (bleu nuit, accent bleu), **Élyséen** (ardoise chaude, accent or),
  **Asmodien** (prune/bordeaux, accent rouge). Light et HighContrast retirés (jamais proposés
  dans ce fork) ; un ancien réglage inconnu retombe sur Sombre.
- Nouveaux jetons de couleur (Color.X + Brush.X) dans chaque thème : SurfaceTop/SurfaceBottom
  (dégradé des fenêtres et de l'overlay), Card, Frame, FrameHover, FrameStrong, Field, Divider,
  WindowEdge, Button, ButtonEdge, Thumb, SideLine, Heading, Body, BodySoft, Label, Muted, Dim,
  Faint, NavText, Brand, BrandEdge, BrandLight, BrandText, BrandSelect, BrandNav, OnBrand,
  OverlayFrame, ModeSwitch. Les voiles blancs/noirs translucides et les couleurs « qui veulent
  dire quelque chose » (barre de PV rouge, point vert/orange, couleurs de classe, croix rouge de
  fermeture) restent en dur.
- Taille de police : Petite 11 / Moyenne 12 / Grande 14 (comme Skeeve). Chaque taille écrite en
  dur devient {DynamicResource Fs.N} où Fs.N = N × base/12 (ThemeManager.FontScaleKeys).
  Overlay : les hauteurs de ligne fixes (20 px) passent en MinHeight pour que la Grande taille
  ne coupe rien. Le zoom de l'overlay (poignée) reste indépendant.
- Halloween (20/10→4/11) garde la main sur l'overlay : ses surcharges locales (fond, accent,
  onglets violets, interrupteur) passent par-dessus le thème ; hors Halloween tout revient au thème.
- Tout s'applique à l'Enregistrer (ThemeManager.Apply puis RefreshCharacterSettings qui
  reconstruit le fond de l'overlay avec les couleurs du thème + l'opacité).

## Tests
Selftest : chaque thème a chaque jeton, chaque clé Fs.N existe et grandit avec la taille,
le fond de l'overlay prend l'opacité et les couleurs du thème. Harnais : vraies fenêtres
(overlay, Paramètres, Sorts, Historique) affichées dans les 4 thèmes × 3 tailles, captures PNG.
