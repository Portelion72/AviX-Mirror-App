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

## Les modes

| Mode | Principe |
|------|----------|
| **Radar** (recommandé) | Rétro synthétique dessiné depuis la télémétrie : les voitures derrière vous en perspective, avec **la silhouette et la couleur de leur catégorie**, la distance, la vitesse de rapprochement et une alerte de voiture à côté. Ne touche pas au jeu, rien ne s'affiche sur l'écran principal. |
| **Capture** | La vraie image du rétro virtuel de LMU, lue dans la fenêtre du jeu et envoyée au VoCore. Un **cache noir** est posé sur le rétro de l'écran principal. |

Dans les deux cas, l'image part **en USB vers le VoCore**, avec son pilote USB : pas de second écran Windows.

## Pré-requis

1. Windows 10 version 2004 ou plus récent (Windows 11 conseillé : pas de cadre jaune autour du jeu capturé).
2. Le VoCore **branché en USB et fonctionnel dans SimHub** (pilote USB VoCore).
   **Fermez SimHub, ou désactivez-y le VoCore, pendant qu'AviX Mirror tourne** : un seul
   logiciel à la fois peut piloter l'écran.
3. Mode **Radar** : le plugin rF2 Shared Memory Map actif dans LMU (SimHub l'installe :
   `Le Mans Ultimate\Plugins\rFactor2SharedMemoryMapPlugin64.dll`).

## Installation

Téléchargez l'artefact `AviXMirror-win-x64` depuis l'onglet **Actions** du dépôt GitHub (ou
**Releases**). Dézippez-le dans un dossier : **gardez `libusb-1.0.dll` à côté de `AviXMirror.exe`**.
Les réglages sont enregistrés dans `AviXMirror.settings.json`, dans le même dossier.

## Mode Radar

*Mode* = `Radar`, puis **Démarrer**. Aucune autre configuration.

![Silhouettes du radar](docs/radar-silhouettes.png)

| Catégorie | Couleur | Silhouette (vue de face) |
|-----------|---------|--------------------------|
| Hypercar | rouge | proto large et bas, ailes très bombées, fines barres LED |
| LMP2 | bleu | proto, doubles petits phares rectangulaires dans les ailes |
| LMP3 | violet | proto plus petit, un phare rond par aile |
| GTE | orange | voiture de route, petite calandre, rétroviseurs |
| GT3 | vert | voiture de route plus haute, grande calandre trapézoïdale, rétroviseurs |

Phares allumés : halo lumineux. Contour rouge : voiture à moins de 10 m. Bandeau orange sur un
bord : voiture à côté de vous. Si les voitures apparaissent du mauvais côté, activez
*Inverser gauche/droite*.

## Mode Capture

1. **LMU** : *Paramètres > Affichage* → **Fenêtré** ou **Sans bordure** (pas le plein écran exclusif).
   Dans l'éditeur de HUD, placez le **rétro virtuel** dans un coin où il gêne peu (par exemple en haut, sur le toit).
2. **AviX Mirror** : *Mode* = `Capture`, *Masquer le rétro sur l'écran* = `True`, puis **Démarrer**.
3. Après le délai de 30 s qui suit le lancement de LMU, cliquez **Calibrer la zone**, tracez un
   rectangle autour du rétro, puis **Valider**.
4. Le rétro s'affiche sur le VoCore. Un cache noir le recouvre sur l'écran principal ; la capture
   n'est pas affectée, car elle lit la fenêtre du jeu et non l'écran.
5. Si l'image est à l'envers, réglez *Rotation* = `Rotation180`.

*Étendre la fenêtre de LMU* reste disponible (expérimental) mais **déforme l'image du jeu** si LMU
ne rend pas à la taille de la fenêtre : laissez-le désactivé.

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
| Cadre jaune autour du jeu | Limitation de Windows 10. Il disparaît sous Windows 11. |
| Radar : « En attente de LMU » | Le plugin rF2 Shared Memory Map n'est pas activé. |
| Image du jeu déformée | Désactivez *Étendre la fenêtre de LMU* et cliquez **Arrêter** puis **Démarrer** : la fenêtre du jeu reprend sa taille. |
| Le cache noir est décalé | Refaites **Calibrer la zone** après avoir placé le rétro dans le HUD. |

Quand on clique **Arrêter**, la fenêtre de LMU retrouve sa taille et son style d'origine.

## Structure du code

```
src/AviXMirror/
  Program.cs              point d'entrée
  MainForm.cs             fenêtre de réglages
  MirrorEngine.cs         orchestration (recherche du jeu, capture/radar, sortie)
  MaskForm.cs             cache noir sur le rétro de l'écran principal
  CalibrationForm.cs      sélection de la zone du rétro à la souris
  Settings.cs             réglages (JSON)
  Capture/                Windows.Graphics.Capture + Direct3D 11
  Output/                 envoi USB au VoCore (libusb, protocole du pilote officiel Vonger/mpro_drm)
  Radar/                  mémoire partagée rF2 + rendu du rétro synthétique
  Util/                   Win32, extension de fenêtre, tampon d'image, serveur MJPEG
```
