# AviX Mirror — rétroviseur VoCore pour Le Mans Ultimate

Application Windows qui affiche le rétroviseur de **Le Mans Ultimate (LMU)** sur un écran
**VoCore 7,8″ (1280×400)** monté comme un vrai rétroviseur, **sans que le rétro virtuel reste
affiché sur l'écran principal**.

## Pourquoi SimHub ne suffit pas

SimHub copie une zone de l'**écran** : le rétro virtuel doit donc être visible sur l'écran
principal pour être recopié. AviX Mirror capture directement la **fenêtre** du jeu avec
Windows.Graphics.Capture, y compris les parties **hors écran**. On agrandit la fenêtre de LMU
d'une bande de 400 px qui dépasse de l'écran principal, on place le rétro virtuel dans cette
bande, et AviX Mirror la recopie sur le VoCore.

```
            ┌──────────── bande de 400 px (hors écran ou sur le VoCore) ───────────┐
            │                     [ rétro virtuel LMU ]  ──► capturé ──► VoCore      │
 ┌──────────┼───────────────────────────────────────────────────────────────────────┼──┐
 │          │                 écran principal : vue du jeu, sans rétro               │  │
 │          └───────────────────────────────────────────────────────────────────────┘  │
 └──────────────────────────────────────────────────────────────────────────────────────┘
```

## Les 3 modes

| Mode | Principe | Quand l'utiliser |
|------|----------|------------------|
| **Capture** (recommandé) | Recopie la zone du rétro virtuel de la fenêtre LMU sur le VoCore (mise à l'échelle, rotation, miroir). | Vraie image du jeu. |
| **Direct** | Pas de fenêtre AviX : la fenêtre de LMU est étendue *sur* le VoCore, et le jeu y dessine lui-même le rétro. | Latence nulle, mais le rétro doit être placé au pixel près dans le HUD. |
| **Radar** | Rétroviseur synthétique dessiné à partir de la télémétrie : voitures derrière vous en perspective, couleur par catégorie, distance, vitesse de rapprochement, alerte de voiture à côté. | Ne dépend d'aucune capture. C'est la solution de secours si la capture ne convient pas. |

## Pré-requis

1. **Windows 10 version 2004 ou plus récent** (Windows 11 conseillé : pas de cadre jaune autour de la fenêtre capturée).
2. **Le VoCore doit être un écran Windows.** Installez le pilote d'affichage Windows du VoCore
   (il apparaît alors dans *Paramètres > Système > Écran*). Mettez-le en orientation
   **Paysage** (sinon utilisez l'option *Rotation* de l'appli).
   > Si SimHub pilote actuellement le VoCore avec son propre pilote, désactivez l'appareil VoCore dans SimHub.
3. Mode **Radar** uniquement : le plugin **rF2 Shared Memory Map** doit être actif dans LMU
   (SimHub l'installe déjà pour LMU : `Le Mans Ultimate\Plugins\rFactor2SharedMemoryMapPlugin64.dll`,
   activé dans `UserData\player\CustomPluginVariables.JSON` avec `" Enabled": 1`).

## Installation

Téléchargez `AviXMirror.exe` depuis l'onglet **Actions** du dépôt GitHub (artefact
`AviXMirror-win-x64`) ou depuis **Releases**. Aucun installateur : lancez l'exe.
Les réglages sont enregistrés dans `AviXMirror.settings.json` à côté de l'exe.

Compiler soi-même (Visual Studio 2022 ou SDK .NET 8) :

```
dotnet publish src/AviXMirror/AviXMirror.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Réglage pas à pas (mode Capture)

1. **Disposition des écrans Windows** : placez le VoCore **au-dessus** de l'écran principal
   (bords collés). Cliquez sur **Identifier les écrans** dans l'appli pour les reconnaître.
2. **LMU** : *Paramètres > Affichage* → mode **Fenêtré** ou **Sans bordure** (pas le plein écran exclusif).
3. **AviX Mirror** :
   - *Mode* = `Capture`, *Source* = `FenetreJeu`
   - *Écran de sortie* = le VoCore
   - *Étendre la fenêtre de LMU* = `True`, *Hauteur de la bande* = `400`, *Côté* = `Haut`
   - Cliquez **Démarrer**. La fenêtre du jeu déborde maintenant de 400 px au-dessus de l'écran principal.
4. **Dans LMU**, ouvrez l'éditeur de HUD et déplacez le **rétro virtuel** tout en haut, dans la
   bande étendue (elle n'est plus visible sur l'écran principal).
5. Dans AviX Mirror, cliquez **Calibrer la zone**, tracez un rectangle autour du rétro, **Valider**.
   Le rétro remplit le VoCore.
6. Cochez *Démarrage automatique* si vous voulez qu'il démarre à l'ouverture de l'appli.

### Conséquences sur la vue du jeu

La fenêtre étant plus haute que l'écran, le centre de la vue 3D est décalé de 200 px vers le haut
et le champ de vision vertical couvre aussi la bande. Compensez dans LMU :
**abaissez la position du siège / l'inclinaison de la vue** et **augmentez légèrement le FOV**
(facteur ≈ (hauteur écran + 400) / hauteur écran). Réduisez la hauteur de la bande à la taille
réelle du rétro virtuel pour limiter ce décalage.

## Mode Direct

Même réglage que ci-dessus, avec *Mode* = `Direct`. La bande de la fenêtre LMU tombe
physiquement sur le VoCore (placé au-dessus de l'écran principal dans Windows). Placez le rétro
virtuel dans le HUD de façon à ce qu'il remplisse le VoCore.

## Mode Radar

*Mode* = `Radar`, *Écran de sortie* = le VoCore. Aucune autre configuration.
Couleurs : rouge = Hypercar, bleu = LMP2, violet = LMP3, vert = GT. Un bandeau orange sur un bord
signale une voiture à côté de vous. Si les voitures apparaissent du mauvais côté, activez
*Inverser gauche/droite*.

## Flux MJPEG (optionnel)

*Flux MJPEG (port)* = `8765` diffuse le rétro sur `http://localhost:8765/` (page plein écran),
`/stream` (flux MJPEG) et `/snapshot.jpg`. Utile pour un navigateur, une tablette ou un
composant web d'un tableau de bord SimHub.

## Dépannage

| Symptôme | Solution |
|----------|----------|
| « En attente de Le Mans Ultimate » | Vérifiez *Processus du jeu* (`Le Mans Ultimate`). |
| Image noire en mode Capture | LMU est en plein écran exclusif : passez en Fenêtré / Sans bordure. |
| La fenêtre du jeu reprend sa taille | LMU la redimensionne : l'appli la ré-étend chaque seconde. Réglez dans LMU la résolution fenêtrée sur *largeur × (hauteur + 400)*. |
| La fenêtre ne dépasse pas autant que demandé | Windows limite la taille d'une fenêtre à celle du bureau : le VoCore doit être placé au-dessus de l'écran principal pour agrandir le bureau. |
| Image étirée dans le jeu | Même cause que ci-dessus : régler la résolution fenêtrée de LMU sur la taille de la fenêtre étendue. |
| Cadre jaune autour du jeu | Limitation de Windows 10. Il disparaît sous Windows 11. |
| Radar : « En attente de LMU » | Le plugin rF2 Shared Memory Map n'est pas activé. |

Quand on clique **Arrêter**, la fenêtre de LMU retrouve sa taille et son style d'origine.

## Structure du code

```
src/AviXMirror/
  Program.cs              point d'entrée
  MainForm.cs             fenêtre de réglages
  MirrorEngine.cs         orchestration (recherche du jeu, capture/radar, sortie)
  MirrorForm.cs           fenêtre plein écran sur le VoCore
  CalibrationForm.cs      sélection de la zone du rétro à la souris
  Settings.cs             réglages (JSON)
  Capture/                Windows.Graphics.Capture + Direct3D 11
  Radar/                  mémoire partagée rF2 + rendu du rétro synthétique
  Util/                   Win32, extension de fenêtre, tampon d'image, serveur MJPEG
```
