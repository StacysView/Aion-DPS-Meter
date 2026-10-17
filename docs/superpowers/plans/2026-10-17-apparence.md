# Plan apparence (17/10/2026)

1. ThemeManager : liste Dark/Midnight/Elyos/Asmodian, nouveaux jetons, clés Fs.N, OverlayBackground(opacité).
2. Thèmes : ajouter les jetons aux 4 dictionnaires (Sombre = valeurs actuelles), supprimer Light/HighContrast.
3. SettingsWindow.xaml : couleurs → DynamicResource, tailles → Fs.N, 4 thèmes dans la liste.
4. PlayerDetailsWindow.xaml, FightHistoryWindow.xaml (+ .cs) : idem.
5. MainWindow.xaml (overlay) : cadre, interrupteur, tailles → Fs.N ; MainWindow.xaml.cs : fond de l'overlay
   et onglets depuis le thème, Halloween par-dessus.
6. Textes : Settings.ThemeMidnight/Elyos/Asmodian (8 langues).
7. Selftest (SelfCheckThemes) + harnais de fenêtres réelles + captures.
