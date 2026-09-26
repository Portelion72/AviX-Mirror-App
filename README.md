# AviX Mirror — rétroviseur VoCore pour Le Mans Ultimate

Application Windows qui affiche le rétroviseur de **Le Mans Ultimate (LMU)** sur un écran
**VoCore 7,8″ (1280×400)** monté comme un vrai rétroviseur, **sans que le rétro virtuel reste
affiché sur l'écran principal**.

## Compatible Easy Anti-Cheat

- **Aucun pilote d'écran VoCore n'est nécessaire.** AviX Mirror envoie l'image au VoCore **en USB**,
  exactement comme SimHub, avec le pilote USB déjà installé par SimHub. Le pilote
  d’« écran Windows » du VoCore est **à désinstaller** : c’est la cause la plus probable de l’erreur
  Easy Anti-Cheat **30007** (« Driver Signature Enforcement »).
- AviX Mirror ne lit ni n'écrit jamais la mémoire du jeu et n'injecte rien. Il attend
  **30 s** après l'apparition de LMU (réglable) avant de toucher à sa fenêtre, pour laisser
  Easy Anti-Cheat démarrer.
- Le mode **Radar** ne touche pas du tout au jeu : il lit seulement la télémétrie partagée,
  comme SimHub ou CrewChief.

### Désinstaller le pilote d'écran VoCore (si vous l'aviez installé)

1. *Gestionnaire de périphériques* → *Cartes graphiques* (ou *Écrans*) → entrée **VoCore** →
   clic droit → **Désinstaller l'appareil** → cochez « Tenter de supprimer le pilote » → OK.
2. Redémarrez.
3. Rebranchez le VoCore et vérifiez qu'il fonctionne toujours dans SimHub : il utilise alors son
   pilote USB normal. AviX Mirror utilise ce même pilote.

## Principe

SimHub copie une zone de l'**écran** : le rétro virtuel doit donc être visible sur l'écran
principal. AviX Mirror capture directement la **fenêtre** du jeu avec Windows.Graphics.Capture,
y compris les parties **hors de l'écran**. L'appli agrandit la fenêtre de LMU d'une bande de
400 px qui dépasse de l'écran principal. On place le rétro virtuel dans cette bande (invisible
sur l'écran), et AviX Mirror l'envoie au VoCore en USB.

```
            ┌──────────── bande de 400 px, hors de l'écran (invisible) ─────────────┐
            │                  [ rétro virtuel LMU ]  ──► capture ──► USB ──► VoCore │
 ┌──────────┼───────────────────────────────────────────────────────────────────────┼──┐
 │          │               écran principal : vue du jeu, sans rétro                │  │
 │          └───────────────────────────────────────────────────────────────────────┘  │
 └──────────────────────────────────────────────────────────────────────────────────────┘
```

## Les modes

| Mode | Principe |
|------|----------|
| **Capture** (par défaut) | La vraie image du rétro virtuel de LMU, prise dans la bande hors écran. |
| **Radar** | Rétro synthétique dessiné depuis la télémétrie : voitures derrière vous en perspective, couleur par catégorie, distance, vitesse de rapprochement, alerte de voiture à côté. Ne touche pas au jeu. |
| **Direct** | Ancienne méthode, qui nécessite le pilote d'écran VoCore. **Déconseillée** (incompatible avec Easy Anti-Cheat). |

## Pré-requis

1. Windows 10 version 2004 ou plus récent (Windows 11 conseillé : pas de cadre jaune autour du jeu capturé).
2. Le VoCore **branché en USB et fonctionnel dans SimHub** (pilote USB de SimHub).
   **Fermez SimHub, ou désactivez-y le VoCore, pendant qu'AviX Mirror tourne** : un seul
   logiciel à la fois peut piloter l'écran.
3. Mode **Radar** uniquement : le plugin rF2 Shared Memory Map actif dans LMU (SimHub l'installe :
   `Le Mans Ultimate\Plugins\rFactor2SharedMemoryMapPlugin64.dll`).

## Installation

Téléchargez l'artefact `AviXMirror-win-x64` depuis l'onglet **Actions** du dépôt GitHub (ou
**Releases**). Dézippez-le dans un dossier : **gardez `libusb-1.0.dll` à côté de `AviXMirror.exe`**.
Les réglages sont enregistrés dans `AviXMirror.settings.json`, dans le même dossier.

## Réglage pas à pas (mode Capture)

1. **LMU** : *Paramètres > Affichage* → **Fenêtré** ou **Sans bordure**, pas le plein écran exclusif.
2. **AviX Mirror** (réglages par défaut) :
   - *Mode* = `Capture`, *Sortie* = `VoCoreUsb`
   - *Étendre la fenêtre de LMU* = `True`, *Hauteur de la bande* = `400`, *Côté* = `Haut`
   - Cliquez **Démarrer**. Le statut doit indiquer « VoCore USB connecté ».
3. Lancez LMU. Après le délai de 30 s, la fenêtre du jeu déborde de 400 px au-dessus de l'écran.
4. Cliquez **Calibrer la zone**. La fenêtre de calibrage montre **toute** l'image du jeu,
   bande invisible comprise. Dans l'éditeur de HUD de LMU, déplacez le **rétro virtuel** tout en
   haut, dans la bande, en suivant sa position dans la fenêtre de calibrage.
5. Tracez un rectangle autour du rétro, puis **Valider**. Le rétro s'affiche sur le VoCore.
6. Si l'image est à l'envers, réglez *Rotation* = `Rotation180`. Si elle est brouillée, réglez
   la largeur et la hauteur natives du VoCore (400 × 1280 pour le 7,8″ ; essayez 1280 × 400 sinon).
7. Cochez *Démarrage automatique* si vous le souhaitez.

### Conséquences sur la vue du jeu

La fenêtre étant plus haute que l'écran, le centre de la vue 3D est décalé de 200 px vers le haut
et le champ de vision vertical couvre aussi la bande. Compensez dans LMU :
**abaissez le siège / l'inclinaison de la vue** et **augmentez un peu le FOV**. Réduisez la hauteur
de la bande à la taille réelle du rétro virtuel pour limiter ce décalage.

## Mode Radar

*Mode* = `Radar`. Aucune autre configuration.
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
| Easy Anti-Cheat erreur 30007 | Désinstallez le pilote d'**écran** VoCore (voir plus haut) et redémarrez. |
| « VoCore USB : écran introuvable ou occupé » | Fermez SimHub (ou désactivez-y le VoCore). Vérifiez dans le Gestionnaire de périphériques que l'ID matériel est `USB\VID_C872&PID_1004` ; sinon, reportez-le dans *VID* / *PID*. |
| Image brouillée ou en biais sur le VoCore | Mauvaise résolution native : réglez *largeur/hauteur native* (400 × 1280 ou 1280 × 400). |
| « En attente de Le Mans Ultimate » | Vérifiez *Processus du jeu* (`Le Mans Ultimate`). |
| Image noire en mode Capture | LMU est en plein écran exclusif : passez en Fenêtré / Sans bordure. |
| La fenêtre du jeu reprend sa taille | LMU la redimensionne : l'appli la ré-étend chaque seconde. Réglez dans LMU la résolution fenêtrée sur *largeur × (hauteur + 400)*. |
| Image étirée dans le jeu | Même cause que ci-dessus : régler la résolution fenêtrée de LMU sur la taille de la fenêtre étendue. |
| Cadre jaune autour du jeu | Limitation de Windows 10. Il disparaît sous Windows 11. |
| Radar : « En attente de LMU » | Le plugin rF2 Shared Memory Map n'est pas activé. |
| Fenêtre de LMU non étendue | Windows peut limiter une fenêtre à la taille du bureau. Réduisez la *Hauteur de la bande*, ou placez le rétro virtuel en bord d'écran et utilisez le mode Radar. |

Quand on clique **Arrêter**, la fenêtre de LMU retrouve sa taille et son style d'origine.

## Structure du code

```
src/AviXMirror/
  Program.cs              point d'entrée
  MainForm.cs             fenêtre de réglages
  MirrorEngine.cs         orchestration (recherche du jeu, capture/radar, sortie)
  MirrorForm.cs           fenêtre plein écran (sortie « EcranWindows »)
  CalibrationForm.cs      sélection de la zone du rétro à la souris
  Settings.cs             réglages (JSON)
  Capture/                Windows.Graphics.Capture + Direct3D 11
  Output/                 envoi USB au VoCore (libusb, protocole du pilote officiel Vonger/mpro_drm)
  Radar/                  mémoire partagée rF2 + rendu du rétro synthétique
  Util/                   Win32, extension de fenêtre, tampon d'image, serveur MJPEG
```
