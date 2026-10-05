# Update button in the compact overlay

Date: 2026-10-05. Fork: StacysView/Aion-DPS-Meter. Chosen by the user ("Bouton qui apparaît").

## Problem

The meter checks GitHub at startup and every five minutes and downloads a newer version in the
background, but its "update ready" notice and restart card are drawn in the full window, which the
overlay-only meter never shows. Updating meant opening Settings and clicking "Update". The startup
check was also "announced": up to date or offline, it put a message box over the overlay.

## Decisions

- A small green download arrow appears in the overlay header (left of the history clock) once a
  version is downloaded; its tooltip names the version. It is hidden otherwise.
- One click installs and restarts: no confirmation card (the click is the consent). Not clicked,
  the downloaded version still applies at the next start.
- Before any update restart (this button, the full window's card, Settings' "Update"), the meter
  keeps what closing it would: the fight on screen goes to the history (it was lost on every
  update) and the window position to the settings. A position that cannot be saved does not stop
  the update; a restart that fails leaves the meter running, its error in the button's tooltip.
- Both automatic checks are silent; the button is the notice. The "Check for updates" menu item
  and Settings' button still answer out loud.
- Not fully automatic: a restart between two pulls in a dungeon is the user's call.

## Checks

- Headless replay of the 22:33 dummy capture: the button hidden before an update, shown with
  "Mise à jour 0.9.51 prête…" after; a click files the 0:38 fight in a scratch history, empties
  the overlay, saves the position, and survives the (not installed) restart failing.
- Self-test and smoke test of the real window.
