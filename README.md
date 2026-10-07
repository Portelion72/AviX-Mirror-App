# AviX Mirror — rétroviseur VoCore pour Le Mans Ultimate et Assetto Corsa

Application Windows qui affiche le rétroviseur de **Le Mans Ultimate (LMU)** et d'**Assetto Corsa** sur un écran
**VoCore 7,8″ (1280×400)** monté comme un vrai rétroviseur, **sans que le rétro virtuel reste
affiché sur l'écran principal**.

## Interface

Fenêtre sombre aux couleurs d'AVIX_3D : choix du mode par tuiles, gros bouton **DÉMARRER**, aperçu en
direct de l'image envoyée au VoCore, état détaillé et **réglages rangés par onglets** (Général, Capture LMU,
Radar, Caméra AC, ATH, LEDs, Écran VoCore, Apparence) ; choisir un mode ouvre son onglet. La **couleur
d'accent** et la **police** se changent dans l'onglet *Apparence* (effet immédiat).

Au premier démarrage, un **tutoriel de prise en main** illustré explique chaque étape (branchement du
VoCore, choix du mode, Radar, Assetto Corsa, Capture LMU, ATH et LEDs, veille et mises à jour) ; on peut
y choisir son mode et installer l'app Assetto Corsa directement. Il se rouvre avec le bouton
**Tutoriel de prise en main**.

## Profils par jeu et jauges

Au-dessus des onglets, **Profil** permet de régler chaque jeu séparément : choisissez « Tous les jeux »
pour les réglages communs, ou un jeu pour lui donner ses propres valeurs. Les onglets **Capture,
Radar, Caméra AC et ATH**, le **mode** (ex. LMU en Capture, Assetto Corsa en Caméra AC) et les alertes
LEDs sont propres à chaque jeu ; les autres onglets (écran VoCore, LEDs, apparence…) restent communs.
Un réglage non modifié pour un jeu reprend la valeur commune ; **Reprendre les réglages communs**
efface les réglages propres au jeu affiché. Quand le rétroviseur tourne, le profil du jeu lancé
s'applique tout seul (et s'affiche dans la fenêtre) ; la zone de **Calibrer la zone** s'enregistre
dans le profil du jeu capturé.

Les valeurs numériques s'affichent avec une **jauge** ; la flèche ouvre un curseur à faire glisser
(molette et flèches du clavier aussi), avec **aperçu en direct** sur le VoCore. Les couleurs s'ouvrent
dans le sélecteur de couleurs de Windows.

## Écran de veille et extinction

Le VoCore n'affiche l'image du jeu que lorsque vous êtes réellement au volant :

| Situation | VoCore | Cache sur l'écran principal | LEDs |
|-----------|--------|-----------------------------|------|
| Au volant | rétro | posé | spotter |
| Jeu en pause ou dans les menus (télémétrie arrêtée) | animation AVIX | retiré | éteintes |
| Retour sur le bureau (autre fenêtre au premier plan) | logo AVIX fixe | retiré | éteintes |
| Jeu non lancé | animation AVIX | — | éteintes |
| Arrêt d'AviX Mirror, extinction ou mise en veille du PC | **écran totalement éteint** | — | éteintes |

**Écran de veille personnalisé** : onglet *Apparence › Écran de veille : image ou animation* — une
image (PNG, JPG, BMP) ou une animation (**GIF animé**) remplace l'animation AVIX ; le **logo AVIX reste
affiché en petit en bas à droite**. *Remplir l'écran* choisit entre image entière et plein écran.

Après une mise en veille, le rétro redémarre tout seul au réveil. Pour Assetto Corsa, la détection de
la pause demande la dernière version de l'app Lua : recliquez **Installer l'app Assetto Corsa**.

## ATH façon caméra de recul (Bosch)

Dans les trois modes (Radar, Caméra AC, Capture LMU), un ATH inspiré des caméras de recul Bosch
Motorsport s'affiche par-dessus l'image :

- **Flèche au-dessus de chaque voiture derrière**, colorée selon l'écart en temps : vert au-delà
  d'1 s, orange entre 0,5 et 1 s, rouge en dessous. Taille réglable (*Taille des flèches (%)*).
- **Échelle de distance à gauche et de temps à droite**, fixes, sur toute la hauteur de l'écran et
  découpées en 10 graduations égales (par défaut 0–100 m par pas de 10 m et 0–2 s par pas de 0,2 s,
  réglables), à l'endroit par défaut (réglage « Échelles en miroir » pour les retourner). Un repère coloré montre l'écart de chaque voiture sur les deux échelles.

En Caméra AC, l'ATH utilise exactement le point de vue de la caméra. En Capture LMU, le point de vue du
rétro virtuel du jeu est approché : ajustez l'onglet *ATH* (champ de vision, hauteur, position)
si les flèches sont décalées, et *Inverser gauche/droite des flèches* si elles sont du mauvais côté.

**Placer les flèches sur les voitures en Capture** (réglages de l'onglet *ATH*, propres à chaque jeu) :
1. Activez *Capture : repères de réglage* : la ligne d'horizon et des repères au sol (10, 20, 40 et
   80 m, bords de voie) s'affichent sur le VoCore.
2. *Capture : hauteur de l'horizon* : amenez la ligne HORIZON là où la route disparaît dans le rétro
   (flèches sous les voitures → baissez la valeur ; au-dessus → augmentez-la).
3. *Capture : champ de vision vertical* : resserrez ou écartez les repères jusqu'à ce que la voie suive
   la route ; *Capture : centre horizontal* si tout est décalé à gauche ou à droite.
4. Désactivez les repères. Les jauges appliquent chaque valeur en direct pendant le réglage.

## LEDs spotter (WS2812B sur la carte MPro)

Deux barrettes de 8 LEDs WS2812B, une de chaque côté de l'écran, branchées en série sur la carte MPro
du VoCore (ordre et sens de chaque barrette réglables). AviX Mirror les pilote par le même câble USB que l'image, avec le
protocole I2C de VoCore (contrôleur de LEDs à l'adresse `0x74`, compatible IS31FL3731,
cf. [Vonger/V7B_WS2812B](https://github.com/Vonger/V7B_WS2812B)).

| Situation | LEDs du côté concerné |
|-----------|------------------------|
| Voiture qui arrive derrière, à moins de 25 m | de plus en plus de LEDs allumées, **couleur de sa catégorie** (Hypercar rouge, LMP2 bleu, LMP3 violet, GTE orange, GT3 vert) |
| Voiture à côté de vous | toutes allumées, couleur de sa catégorie |
| **Dive bomb** : voiture qui arrive très vite de derrière, déjà décalée d'un côté | **clignotement rapide** de ce côté, avant qu'elle ne soit à votre hauteur |
| Voitures des deux côtés (sandwich) | rouge clignotant des deux côtés |

Les couleurs par catégorie se changent dans l'onglet *LEDs* (ou se désactivent : jaune → orange quand
une voiture arrive, rouge à côté). Le dive bomb se déclenche quand une voiture arrive avec au moins
30 km/h d'écart et sera à votre hauteur en moins d'1 s (réglable).

Fonctionne dans tous les modes (Radar, Caméra AC, Capture), avec LMU et Assetto Corsa. Au branchement,
les LEDs s'allument une par une dans l'ordre de la chaîne (couleur AVIX) : vérifiez que la droite
s'allume en premier, et utilisez *Inverser le sens* si une barrette se remplit à l'envers. Réglages
dans l'onglet *LEDs* (luminosité, distance d'alerte, ordre de câblage, protocole). L'aperçu de la
fenêtre montre l'état des LEDs de chaque côté du rétro.

## Compatible Easy Anti-Cheat

- **Aucun pilote d'écran VoCore n'est nécessaire.** AviX Mirror envoie l'image au VoCore **en USB**,
  exactement comme SimHub, avec le pilote USB déjà installé par SimHub. Le pilote
  d’« écran Windows » du VoCore est **à désinstaller** : c’est la cause la plus probable de l’erreur
  Easy Anti-Cheat **30007** (« Driver Signature Enforcement »).
- AviX Mirror ne lit ni n'écrit jamais la mémoire du jeu et n'injecte rien. En mode Capture, il
  attend **10 s** après l'apparition de LMU (réglable) avant de capturer sa fenêtre, pour laisser
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
| **Radar** (recommandé) | Rétro synthétique dessiné depuis la télémétrie (tous les jeux, voir ci-dessous) : les voitures derrière vous en perspective, avec **la silhouette et la couleur de leur catégorie**, la distance, la vitesse de rapprochement et une alerte de voiture à côté. Ne touche pas au jeu, rien ne s'affiche sur l'écran principal. |
| **Caméra Assetto Corsa** | Vraie vue arrière rendue hors écran par Assetto Corsa (app Lua CSP). Rien ne s'affiche sur l'écran du jeu. |
| **Capture LMU** | La vraie image du rétro virtuel de LMU, lue dans la fenêtre du jeu et envoyée au VoCore. Un **cache** est posé sur le rétro de l'écran principal. |

Dans tous les cas, l'image part **en USB vers le VoCore**, avec son pilote USB : pas de second écran Windows.

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

## Jeux pris en charge

Le jeu se choisit dans l'onglet *Général* (*Jeu*) ; en **Auto**, AviX Mirror suit le jeu lancé. Le
Radar, l'ATH, les LEDs du spotter et l'écran de veille fonctionnent avec tous ces jeux ; la Capture du
rétro virtuel aussi (le jeu doit afficher un rétro virtuel, en fenêtré ou sans bordure).

| Jeu | État | Télémétrie utilisée | À configurer |
|-----|------|---------------------|--------------|
| Le Mans Ultimate | complet | plugin rF2 Shared Memory Map | plugin actif (SimHub l'installe) |
| Assetto Corsa | complet | app Lua AviX Mirror (CSP) | bouton *Installer l'app Assetto Corsa* |
| rFactor 2 | **beta** | plugin rF2 Shared Memory Map | plugin actif |
| Assetto Corsa Competizione | **beta** | mémoire partagée officielle | rien |
| Assetto Corsa EVO | **beta** | mémoire partagée officielle (`acevo_pmf_*`) | rien |
| Automobilista 2 / Project CARS 2 | **beta** | mémoire partagée « Project CARS 2 » | *Options → Système → Mémoire partagée* = Project CARS 2 |
| iRacing | **beta**, radar simplifié | SDK officiel (mémoire partagée) | rien |
| F1 23 / 24 / 25 | **beta** | télémétrie UDP officielle | *Réglages → Télémétrie* : UDP activée, port 20777, format 2023 ou plus |

Particularités des jeux beta :
- **ACC** ne donne que la position des adversaires (ni vitesse, ni catégorie) : vitesses estimées,
  toutes les voitures dessinées en GT3 ; le tracé du circuit s'apprend avec vos propres tours.
- **AC EVO** : comme ACC, seules les positions des adversaires sont connues (vitesses estimées,
  catégorie inconnue) ; le tracé s'apprend avec vos propres tours.
- **AMS2 / PC2** : la catégorie des adversaires n'est pas lue ; vitesses estimées.
- **iRacing** ne donne pas la position des autres voitures dans le monde : le radar montre la
  **distance exacte** derrière vous sur une route droite, et place les voitures à votre hauteur à
  gauche ou à droite d'après le **spotter d'iRacing** (les LEDs fonctionnent donc aussi).
- **F1** : voitures dessinées en monoplace ; un seul logiciel peut écouter le port UDP (dans SimHub,
  utilisez le renvoi UDP vers un autre port, ou changez *F1 : port de télémétrie UDP*).
- Si les voitures apparaissent du mauvais côté dans un jeu beta, activez *Jeux beta : inverser
  gauche/droite* (onglet *Radar*).

## Mode Radar

Mode **Radar**, puis **Démarrer**. Aucune autre configuration.

![Silhouettes du radar](docs/radar-silhouettes.png)

| Catégorie | Couleur | Silhouette (vue de face) |
|-----------|---------|--------------------------|
| Hypercar | rouge | proto large et bas, ailes très bombées, fines barres LED |
| LMP2 | bleu | proto, doubles petits phares rectangulaires dans les ailes |
| LMP3 | violet | proto plus petit, un phare rond par aile |
| GTE | orange | voiture de route, petite calandre, rétroviseurs |
| GT3 | vert | voiture de route plus haute, grande calandre trapézoïdale, rétroviseurs |

**Face avant par voiture (LMU).** Dans Le Mans Ultimate, chaque voiture reconnue a sa propre face
avant (phares, calandre, entrées d'air), la couleur restant celle de sa catégorie : Toyota GR010,
Ferrari 499P, Porsche 963, Peugeot 9X8, Cadillac V-Series.R, BMW M Hybrid V8, Alpine A424,
Lamborghini SC63, Isotta Fraschini Tipo 6, Glickenhaus 007, Vanwall 680, Aston Martin Valkyrie,
Oreca 07, Ligier JS P325, Ginetta G61, Duqueine D09, Ferrari 296 / 488, Porsche 911, BMW M4,
Aston Martin Vantage, Lexus RC F, McLaren 720S, Corvette, Ford Mustang, Lamborghini Huracán,
Mercedes-AMG. La voiture est reconnue par son nom dans LMU ; sinon la silhouette de sa catégorie est
utilisée. Réglage : onglet *Radar › Face avant par voiture (LMU)*. Les formes sont décrites dans
`src/AviXMirror/Radar/CarFronts.txt`.

![Faces avant LMU](docs/radar-faces-lmu.png)

**La piste suit le circuit.** LMU ne fournit pas le tracé du circuit : AviX Mirror l'apprend
en direct à partir des positions de toutes les voitures (centre et largeur de la piste tous les 2 m).
Le statut indique la part du circuit déjà connue ; en course, quelques minutes suffisent. Le tracé
est ensuite **mémorisé par circuit** (`%LOCALAPPDATA%\AviXMirror\circuits`) et disponible
immédiatement aux sessions suivantes. La piste du rétro tourne alors dans les virages et suit le
relief, avec vibreurs rouge/blanc et lignes de bord ; tant que l'endroit n'est pas connu, une route
droite s'affiche.

**Vibreurs aux couleurs du circuit**, dessinés seulement dans les virages : jaune/bleu au Mans,
vert/blanc/rouge à Monza et Imola, rouge/jaune à Spa, blanc/vert/jaune à Interlagos, bleu/rouge au
Paul Ricard, noir/blanc à Silverstone, rouge/blanc/noir à Lusail, rouge/blanc ailleurs. Les couleurs sont dans `AviXMirror.kerbs.v2.json` (créé à côté de l'exe au premier
lancement) : chaque ligne associe un mot du nom du circuit à une suite de couleurs, que vous pouvez
modifier ou compléter, par exemple `"interlagos": ["#FFD700", "#009C3B"]`. Tous les circuits de LMU y
sont listés, DLC compris (WEC, ELMS, US Track Pass), ainsi que les principaux circuits d'Assetto Corsa.

**Fluidité** : LMU ne donne la position des voitures que 5 fois par seconde. Entre deux relevés,
AviX Mirror prolonge le mouvement (vitesse et rotation de la voiture) et corrige les écarts en douceur :
la vue tourne sans à-coups dans les virages.

Phares allumés : halo lumineux. Contour rouge : voiture à moins de 10 m. Bandeau orange sur un
bord : voiture à côté de vous. Si les voitures apparaissent du mauvais côté, activez
*Inverser gauche/droite*.

## Assetto Corsa (avec Content Manager)

La mémoire partagée officielle d'Assetto Corsa ne donne que **votre** voiture. AviX Mirror utilise donc
une petite app Lua pour **Custom Shaders Patch** (CSP, installé avec Content Manager) qui exporte la
position, la direction, la vitesse, la progression sur le tour et le modèle de **toutes** les voitures.

1. Dans Content Manager, vérifiez que **Custom Shaders Patch** est installé (*Paramètres > Custom Shaders Patch*).
2. Dans AviX Mirror, cliquez **Installer l'app Assetto Corsa** : elle est copiée dans
   `assettocorsa\apps\lua\AviXMirror` (dossier trouvé automatiquement dans Steam). Pour une installation
   manuelle, copiez le dossier `AssettoCorsa-app-CSP\AviXMirror` de l'archive au même endroit.
3. Mode **Radar**, *Jeu* = `Auto` (ou `AssettoCorsa`), puis **Démarrer**.
4. Lancez une session depuis Content Manager. L'app démarre toute seule (fenêtre « AviX Mirror »
   facultative dans la barre d'apps CSP).

La catégorie de chaque voiture (silhouette et couleur) est déduite de son identifiant AC : `gt3`,
`gte`, `lmp2`, `lmp3`, `919`, `499p`, `963`… Les autres voitures sont dessinées en gris. Le tracé du
circuit s'apprend comme dans LMU, puis il est mémorisé. Si les voitures apparaissent du mauvais côté,
activez *Assetto Corsa : inverser gauche/droite*.

## Assetto Corsa : vraie vue arrière, sans rétro virtuel

Mode **Caméra Assetto Corsa**. L'app Lua (même installation que ci-dessus) demande à Assetto Corsa
de rendre une **caméra arrière hors écran**, avec la même technique que l'intégration OBS de CSP.
L'image est partagée directement sur la carte graphique avec AviX Mirror, qui la retourne en
miroir et l'envoie au VoCore. **Rien n'est affiché sur l'écran du jeu.**

- Après une mise à jour d'AviX Mirror, recliquez **Installer l'app Assetto Corsa** (nouvelle version de l'app).
- Réglages (onglet *Caméra AC*) : champ de vision, recul et hauteur de la caméra,
  résolution, images par seconde (30 par défaut), effet miroir, **exposition** et **gamma** (par défaut 0,7 et 2,2). Les réglages s'appliquent en direct pendant que vous roulez ;
  « Enregistrer les réglages » les conserve.
- Chaque image est un rendu supplémentaire de la scène : comptez une légère baisse de FPS, comme
  avec un rétroviseur en jeu. Baissez *Images par seconde* si besoin.
- Si l'arrière de votre voiture apparaît dans l'image, augmentez *Recul de la caméra*.
- L'image est rendue en HDR puis ramenée dans la plage de l'écran avec une **exposition automatique**
  et une courbe filmique : plus d'image blanche quand le ciel ou le soleil entre dans le champ.
  *Exposition* corrige cette exposition automatique (1 = neutre).
- La caméra s'arrête toute seule quand AviX Mirror est fermé.

## Mode Capture

1. **LMU** : *Paramètres > Affichage* → **Fenêtré** ou **Sans bordure** (pas le plein écran exclusif).
   Dans l'éditeur de HUD, placez le **rétro virtuel** dans un coin où il gêne peu (par exemple en haut, sur le toit).
2. **AviX Mirror** : mode **Capture LMU**, puis **Démarrer** (*Cacher le rétro du jeu* est activé par défaut).
3. Après le délai de 10 s qui suit le lancement de LMU, cliquez **Calibrer la zone**. Un cadre au
   format du VoCore (1280 × 400) couvre l'image : déplacez-le sur le rétro et réduisez-le par ses
   coins (le format est conservé), puis **Valider**. Comme dans Word, le cadre **s'aimante sur des
   repères** (ligne pointillée) : bords du rétro virtuel détectés dans l'image, milieu entre deux bords
   (pour centrer le cadre sur le rétro), bords et milieu de l'image. Maintenez **Alt** pour placer
   librement.
4. Le rétro s'affiche sur le VoCore. Un cache le recouvre sur l'écran principal ; la capture
   n'est pas affectée, car elle lit la fenêtre du jeu et non l'écran. Le cache peut déborder de la zone
   de capture (*Cache : marge à gauche / droite / en haut / en bas*, onglet *Capture LMU*).
5. **Couleur du cache** : il reprend la couleur du décor lue en des **points de couleur** autour de lui
   (par défaut 5 points, sur les côtés et en dessous). Ajoutez-en autant que vous voulez dans **Calibrer
   la zone** : double-clic autour du cadre pour ajouter un point, glisser pour le déplacer, clic droit
   pour le supprimer. Avec plusieurs points, le cache se remplit d'un dégradé entre leurs couleurs.
6. Si l'image est à l'envers, réglez *Rotation* = `Rotation180`.

## Dépannage

| Symptôme | Solution |
|----------|----------|
| Easy Anti-Cheat erreur 30007 | Désinstallez le pilote d'**écran** VoCore (voir plus haut) et redémarrez. |
| « VoCore USB : écran introuvable ou occupé » | Fermez SimHub (ou désactivez-y le VoCore). Vérifiez dans le Gestionnaire de périphériques que l'ID matériel est `USB\VID_C872&PID_1004` ; sinon, reportez-le dans `VoCoreVendorId` / `VoCoreProductId` de `AviXMirror.settings.json` (réglages experts, masqués dans la fenêtre). |
| Image brouillée ou en biais sur le VoCore | Mauvaise résolution native : réglez `VoCoreWidth` / `VoCoreHeight` dans `AviXMirror.settings.json` (400 × 1280 ou 1280 × 400). |
| « En attente de Le Mans Ultimate » | Vérifiez `GameProcessName` (`Le Mans Ultimate`) dans `AviXMirror.settings.json`. |
| Image noire en mode Capture | LMU est en plein écran exclusif : passez en Fenêtré / Sans bordure. |
| Cadre jaune autour du jeu | Limitation de Windows 10. Il disparaît sous Windows 11. |
| Radar : « En attente de LMU » | Le plugin rF2 Shared Memory Map n'est pas activé. |
| Le cache est décalé | Refaites **Calibrer la zone** après avoir placé le rétro dans le HUD. |

## Mises à jour

Au démarrage puis toutes les 6 heures, AviX Mirror vérifie s'il existe une nouvelle version sur la page
[Releases](https://github.com/Portelion72/AviX-Mirror-App/releases/latest). Si c'est le cas, un
**bandeau** apparaît en haut de la fenêtre (et l'icône clignote dans la barre des tâches si elle est
réduite) : un clic ouvre la page de téléchargement. Aucune donnée n'est envoyée. Désactivable dans
l'onglet *Général* (*Vérifier les mises à jour*).

## Licence

© 2026 AVIX_3D — **tous droits réservés**. AviX Mirror est un logiciel propriétaire : seul le
téléchargement des versions officielles pour un usage personnel et non commercial est autorisé.
Toute copie, modification, redistribution ou utilisation commerciale du code ou du programme est
interdite sans accord écrit d'AVIX_3D. Voir [`LICENSE`](LICENSE) ; composants tiers :
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Structure du code

```
src/AviXMirror/
  Program.cs              point d'entrée
  MainForm.cs             fenêtre principale (tuiles de mode, aperçu VoCore, réglages)
  Ui/                     thème AVIX_3D, contrôles dessinés, écran d'accueil du VoCore
  MirrorEngine.cs         orchestration (recherche du jeu, capture/radar, sortie)
  MaskForm.cs             cache sur le rétro de l'écran principal
  CalibrationForm.cs      sélection de la zone du rétro à la souris
  Settings.cs             réglages (JSON)
  Capture/                capture LMU (Windows.Graphics.Capture), caméra Assetto Corsa, exposition HDR
  Hud/                    ATH façon caméra de recul (flèches, échelles)
  Output/                 envoi USB au VoCore (libusb, protocole du pilote officiel Vonger/mpro_drm)
  Radar/                  télémétrie LMU (rF2) et Assetto Corsa, tracé du circuit, vibreurs, rendu,
                          faces avant des voitures (CarFronts.txt), spotter à LEDs
  Util/                   Win32, tampon d'image, recherche de la fenêtre du jeu, installation de l'app AC
integrations/AssettoCorsa/AviXMirror/   app Lua pour Custom Shaders Patch (export de toutes les voitures)
```
