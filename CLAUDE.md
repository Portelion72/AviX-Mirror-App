# AviX Mirror — notes pour Claude Code

Application Windows (AVIX_3D, avix3d.com) qui affiche un rétroviseur sur un écran **VoCore 7,8″
(1280×400)** pour **Le Mans Ultimate** (LMU) et **Assetto Corsa** (Content Manager + CSP), sans rétro
virtuel sur l'écran principal. Logiciel **propriétaire** (voir `LICENSE`).

L'utilisateur (propriétaire) écrit en **français** : répondre en français, textes de l'interface,
commentaires et messages de commit en français. Il teste lui-même en jeu ; rien ne peut être testé
en jeu depuis Claude : le dire clairement dans chaque compte rendu.

## Construire / publier

- .NET 8 WinForms, `net8.0-windows10.0.22621.0`, x64 : `src/AviXMirror/AviXMirror.csproj`.
- Windows : `dotnet build src/AviXMirror -c Release` ; sous Linux la compilation WinForms est
  impossible → on vérifie avec la CI GitHub Actions (`.github/workflows/build.yml`, exe unique
  autonome, artefact `AviXMirror-win-x64`).
- **Release** : Actions → Build → *Run workflow* avec `release = vX.Y.Z` (crée le tag, la release et
  le zip ; le numéro est injecté dans l'exe via `-p:Version`). Un push de tag `v*` marche aussi.
  Pensez à monter `<Version>` dans le csproj pour les builds hors release.
- L'app vérifie la dernière release GitHub (`Util/UpdateChecker.cs`, dépôt `Portelion72/AviX-Mirror-App`)
  et affiche un bandeau : le dépôt doit rester public pour que cela fonctionne sans jeton.
- `libusb-1.0.dll` (MinGW64 officiel, LGPL) est livré à côté de l'exe (`src/AviXMirror/native`).

## Contraintes importantes

- **Easy Anti-Cheat (LMU)** : jamais de lecture/écriture de la mémoire du jeu, pas d'injection, pas
  de pilote non signé (le pilote « écran » VoCore provoquait l'erreur EAC 30007). Sortie vers le
  VoCore **en USB via libusb** (pas de second écran Windows). Délai (10 s par défaut) après le lancement de LMU
  avant de capturer sa fenêtre.
- CSP 0.1.79 (version de l'utilisateur) : l'app Lua doit tout protéger par `pcall` (membres absents,
  `sharedHandle` indisponible → mode compatibilité par `shot:encode()` DDS).

## Architecture (`src/AviXMirror`)

- `MainForm.cs` : tuiles de mode, Démarrer, aperçu VoCore, **onglets** de réglages (`TabStrip` +
  `Ui/SettingsPanel` construit par réflexion depuis `CategoryAttribute` = constantes `Tabs.*`,
  `Tabs.For(mode)` = onglets du mode seulement, `[ModeOnly]` pour les réglages propres à un mode ;
  curseur `Slider` + case numérique pour les `[Gauge]`, interrupteurs, couleurs), bouton « Guide du
  jeu » (`GameGuides.cs` + `Ui/GameGuideForm` : config dans le jeu + réglages conseillés appliqués au
  profil), bandeau de mise à jour, gestion veille/extinction du PC (`SystemEvents`).
- `MirrorEngine.cs` : orchestration (timer 250 ms) ; `GameActivity` décide de l'état
  **Active / Paused / Desktop / NoGame** → écran de veille (`Frames.Held` + `WriteStandby`,
  animation `Ui/Splash.DrawAnimated`, logo fixe sur le bureau), cache retiré, LEDs suspendues.
  Propriété `Calibrating` : image brute pendant la calibration (sans ATH ni veille).
- **Profils par jeu** : `Settings.GameProfiles` (jeu → valeurs propres, JSON), `IsPerGame` (onglets
  Capture/Radar/Caméra/ATH + Mode, délai, alertes LEDs), `ForGame`, `Set(jeu, nom, valeur)`. L'interface
  édite une vue `ForGame(profil)` et écrit dans `_settings` (maître) ; le moteur détecte le jeu lancé
  (`CurrentGame`) et applique son profil (redémarrage seulement si le mode change).
- Éditeurs (`Ui/Editors.cs`) : `GaugeAttribute`, `ColorHexEditor`, `ImageFileEditor` (repris par
  `SettingsPanel` ; `GaugeEditor` n'est plus utilisé par la fenêtre principale). Écran de veille
  personnalisé (`StandbyImage`, GIF animé) avec petit logo AVIX en bas à droite (`Splash.DrawCustom`).
- `Settings.cs` : tous les réglages (JSON à côté de l'exe, `SettingsVersion` + migrations),
  enums, `MaskSample`, `LedClassColor`, `Tabs`. Valeurs par défaut = réglages du propriétaire ; profils
  par défaut dans `Assets/DefaultProfiles.json` (ressource `Defaults.Profiles.json`). `Settings.FirstRun`
  (pas de fichier) → tutoriel, puis options raccourci bureau / lancement avec Windows
  (`Util/WindowsIntegration` : IShellLink, clé HKCU\...\Run). `LedProtocol` forcé à Is31Compatible (masqué).
- Catégories LEDs personnalisées : `LedCustomClasses` (nom exact du jeu → couleur, prioritaire),
  menu déroulant alimenté par `Radar/SeenClasses` (catégories vues dans `TelemetrySet.Read`,
  mémorisées dans `%LOCALAPPDATA%\AviXMirror\categories.txt`).
- Modes :
  - **Radar** (`Radar/RadarSource.cs`) : rendu synthétique en perspective depuis la télémétrie ;
    tracé appris (`TrackMap`, mémorisé dans `%LOCALAPPDATA%\AviXMirror\circuits`), vibreurs par
    circuit (`KerbPalette`, fichier `AviXMirror.kerbs.v2.json`), lissage (`MotionSmoother` : LMU ne
    donne que ~5 relevés/s), silhouettes par catégorie (`CarClasses`) et **faces avant par voiture
    LMU** (`CarFronts.cs` + mini-langage `CarFronts.txt`, ressource intégrée).
  - **Caméra AC** (`Capture/AcCameraSource.cs` + app Lua) : caméra arrière rendue hors écran par CSP
    (`ac.GeometryShot`), texture partagée ou DDS en mémoire partagée (seqlock), rendu HDR
    (`ToneMapper` : exposition auto, ACES, sRGB).
  - **Capture LMU** (`Capture/WgcCapture.cs`) : Windows.Graphics.Capture de la fenêtre du jeu, zone
    recadrée ; `MaskForm` = cache sur le rétro virtuel (couleur lue en N points autour, dégradé IDW) ;
    `CalibrationForm` = cadre au format 1280/400 (déplacement, coins seulement), repères aimantés
    (bords détectés dans l'image, milieux), points de couleur à la souris, **forme libre du cache**
    (`MaskShape`, sommets en % de la zone, `MaskShapeSmooth` ; `MaskForm` prend une `Region`).
    `HudAlignForm` (« Aligner les flèches ») : on fige l'image, on glisse les flèches sur les voitures ;
    `Hud/HudProjection.Fit` recalcule horizon/centre (1 point) et champ de vision (≥ 2 points, moindres carrés).
- Télémétrie : `Radar/Rf2Telemetry.cs` (mémoire partagée `$rFactor2SMMP_Scoring$`, structs Pack=4,
  repère local rF2 : x = gauche, y = haut, z = arrière) ; `Radar/AcTelemetry.cs` (mémoire partagée
  `AviXMirror.AC.v1` écrite par l'app Lua, 9104 octets, **disposition identique** au Lua).
- **Jeux** (`Radar/Games.cs`) : catalogue (processus, télémétrie, beta) ; `TelemetrySet` = une source
  par jeu, lue seulement si le processus tourne (Auto). Beta : rFactor 2 (`Rf2Telemetry`), ACC
  (`AccTelemetry`, pages `acpmf_*`), AC EVO (`AcEvoTelemetry`, pages `acevo_pmf_*`, offsets
  calculés d'après la doc Kunos ACE_SharedFileOut v1), AMS2/PC2 (`Ams2Telemetry`, `$pcars2$`, offsets SharedMemory.h
  v9+), iRacing (`IRacingTelemetry`, SDK, monde « déroulé » sur une ligne + CarLeftRight), F1 23-25
  (`F1Telemetry`, UDP Motion, réception partagée `F1UdpReceiver`). Repère F1 indirect : gauche = −droite.
  Rien n'a été testé en jeu pour ces titres.
- `Hud/` : ATH façon caméra de recul Bosch (flèches au-dessus des voitures, échelles distance/temps
  fixes en 10 graduations, en miroir) sur les 3 modes ; `FrameBuffer.PostProcess` / `UpscaleTo`
  (en capture, l'ATH est dessiné en pleine résolution par-dessus l'image agrandie).
- `Radar/Spotter.cs` : LEDs WS2812B (2 × 8 en série, ordre et sens réglables) via I2C de la carte MPro (adresse
  0x74, mode compatible IS31FL3731) ; couleurs par catégorie, dive bomb (clignotement rapide).
- `Output/VoCoreUsbOutput.cs` : protocole VoCore (commande 0x40/0xB0 `00 2C len…` puis RGB565 en
  bulk 0x02), rotation rapide, LEDs, **écran éteint** à la fermeture (image noire, rétroéclairage 0,
  commande 0x28).
- `integrations/AssettoCorsa/AviXMirror/` : app Lua CSP (installée par le bouton « Installer l'app
  Assetto Corsa ») ; le compteur `packetId` s'arrête en pause (détection de pause côté exe).

## Aperçus / tests hors Windows

- Faces avant : un script PIL qui relit `CarFronts.txt` sert d'aperçu (voir historique :
  `docs/radar-faces-lmu.png`).
- Les parties sans WinForms (parseur des faces avant, etc.) se testent dans un petit projet console
  `net8.0` qui inclut le fichier source.

## Historique des demandes (résumé)

Radar LMU → silhouettes par catégorie → tracé du circuit → vibreurs par circuit (tous DLC) → Assetto
Corsa (app Lua CSP) → caméra AC hors écran (corrections CSP 0.1.79, HDR, images blanches) → charte
AVIX (couleur #C4C007, police Bahnschrift, logo, page de veille) → LEDs WS2812B → fluidité radar →
zone de capture au format VoCore → ATH Bosch (flèches, échelles 10 graduations, miroir, taille des
flèches) → ATH net en capture → cache avec marges et couleur du décor → faces avant LMU → rangement
(retrait MJPEG, extension de fenêtre, capture d'écran entier) → veille/pause/bureau, écran éteint,
LEDs par catégorie, dive bomb, repères de calibration, onglets, points de couleur → licence
propriétaire + avis de mise à jour → v1.1.0 → tutoriel de prise en main → v1.2.0 → réglages par défaut du propriétaire → v1.2.1 → jeux beta (rF2, ACC, AC EVO, AMS2/PC2, iRacing, F1) → profils par jeu, jauges, écran de veille personnalisé → réglages d'horizon ATH, outil « Aligner les flèches » (tous jeux), forme libre du cache (rétro F1) → panneau de réglages maison (curseurs + cases), onglets par mode, guides et réglages conseillés par jeu → réglages du propriétaire par défaut (profils inclus), LEDs IS31 seulement, catégories LEDs personnalisées, raccourci bureau + lancement avec Windows → v1.3.0 → animation de veille nette (fond noir uni, reflet doux, logo bicubique).

## Git

Branche de travail actuelle : `claude/le-mans-rearview-app-6vvy2n` (c'est aussi la branche par
défaut du dépôt ; l'utilisateur envisage de la renommer `main`).
