using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AviXMirror;

public enum MirrorMode
{
    /// <summary>Capture de l'image réelle du rétroviseur virtuel de LMU.</summary>
    Capture,
    /// <summary>Rétroviseur synthétique dessiné à partir de la télémétrie (aucune capture).</summary>
    Radar,
    /// <summary>Vraie caméra arrière d'Assetto Corsa rendue hors écran par l'app Lua (CSP).</summary>
    CameraAssettoCorsa,
}

/// <summary>Jeu suivi (les jeux marqués « beta » sont pris en charge mais peu testés).</summary>
public enum RadarGame
{
    Auto,
    LeMansUltimate,
    AssettoCorsa,
    RFactor2,
    AssettoCorsaCompetizione,
    AssettoCorsaEvo,
    Automobilista2,
    IRacing,
    F1,
}

public enum LedChainOrder
{
    DroiteGauche,
    GaucheDroite,
}

public enum LedProtocol
{
    Is31Compatible,
    Complet,
}

public enum OutputRotation
{
    Aucune = 0,
    Rotation90 = 90,
    Rotation180 = 180,
    Rotation270 = 270,
}

/// <summary>Onglets de réglages de la fenêtre principale (= catégories de la grille).</summary>
public static class Tabs
{
    public const string General = "Général";
    public const string Screen = "Écran VoCore";
    public const string Capture = "Capture";
    public const string Radar = "Radar";
    public const string Camera = "Caméra AC";
    public const string Hud = "ATH";
    public const string Leds = "LEDs";
    public const string Appearance = "Apparence";

    public static readonly string[] All = { General, Capture, Radar, Camera, Hud, Leds, Screen, Appearance };

    /// <summary>Onglets affichés pour un mode : celui du mode (Capture, Radar ou Caméra AC) et les onglets communs.</summary>
    public static string[] For(MirrorMode mode) => new[]
    {
        General,
        mode switch { MirrorMode.Capture => Capture, MirrorMode.Radar => Radar, _ => Camera },
        Hud, Leds, Screen, Appearance,
    };
}

public enum MaskSide
{
    Haut,
    Bas,
    Gauche,
    Droite,
}

/// <summary>Point lu autour du cache pour reprendre la couleur du décor.</summary>
public sealed class MaskSample
{
    [DisplayName("Côté"), Description("Côté du cache près duquel la couleur est lue.")]
    public MaskSide Side { get; set; } = MaskSide.Haut;

    [DisplayName("Position (%)"), Description("Position le long du côté : 0 = début (gauche ou haut), 100 = fin.")]
    public double Position { get; set; } = 50;

    [DisplayName("Distance (px)"), Description("Distance au bord du cache (1 = le pixel juste à côté).")]
    public int Distance { get; set; } = 1;

    public override string ToString() => $"{Side} {Position:0} % ({Distance} px)";
}

/// <summary>Sommet de la forme du cache, en % de la zone de capture (en dehors de 0..100 : dans les marges).</summary>
public sealed class MaskPoint
{
    [DisplayName("X (%)"), Description("0 = bord gauche de la zone de capture, 100 = bord droit.")]
    public double X { get; set; }

    [DisplayName("Y (%)"), Description("0 = bord haut de la zone de capture, 100 = bord bas.")]
    public double Y { get; set; }

    public override string ToString() => $"{X:0.#} ; {Y:0.#}";
}

/// <summary>Couleur des LEDs pour une catégorie de voiture choisie par l'utilisateur.</summary>
public sealed class LedClassColor
{
    [DisplayName("Catégorie"), Description("Nom de la catégorie tel que le jeu le donne (ex. « GT3 », « LMGT3 », « Hypercar »).")]
    public string Class { get; set; } = "";

    [DisplayName("Couleur"), Description("Couleur au format #RRVVBB.")]
    public string Color { get; set; } = "#FFFFFF";

    public override string ToString() => $"{Class} {Color}";
}

public sealed class Settings
{
    // ---------- Général ----------

    [Category(Tabs.General), DisplayName("Mode")]
    [Description("Capture : recopie le rétro virtuel du jeu sur le VoCore.\n" +
                 "Radar : rétro synthétique dessiné depuis la télémétrie (LMU ou Assetto Corsa).\n" +
                 "CameraAssettoCorsa : vraie image de ce qui est derrière, rendue par AC hors écran (app Lua CSP).")]
    public MirrorMode Mode { get; set; } = MirrorMode.Capture;

    [Category(Tabs.General), DisplayName("Démarrage automatique")]
    [Description("Démarre le rétroviseur dès l'ouverture de l'application.")]
    public bool AutoStart { get; set; } = true;

    [Category(Tabs.General), DisplayName("Lancer avec Windows")]
    [Description("AviX Mirror s'ouvre tout seul à l'ouverture de session Windows (avec « Démarrage automatique », " +
                 "le rétro démarre aussi tout seul, fenêtre réduite).")]
    public bool StartWithWindows { get; set; }

    [Browsable(false)]
    [Category(Tabs.General), DisplayName("Tutoriel au démarrage")]
    public bool ShowTutorial { get; set; }

    [Category(Tabs.General), DisplayName("Jeu")]
    [Description("Auto : détecte le jeu lancé. Le Mans Ultimate et Assetto Corsa (app Lua CSP) sont complets ; " +
                 "rFactor 2, Assetto Corsa Competizione, Assetto Corsa EVO, Automobilista 2 / Project CARS 2, iRacing (radar simplifié) " +
                 "et F1 23/24/25 (télémétrie UDP) sont en beta. Utilisé par tous les modes (radar, ATH, LEDs, capture).")]
    public RadarGame RadarGame { get; set; } = RadarGame.Auto;

    [Category(Tabs.General), DisplayName("Vérifier les mises à jour")]
    [Description("Au démarrage puis toutes les 6 heures, un bandeau signale une nouvelle version d'AviX Mirror.")]
    public bool CheckUpdates { get; set; } = true;

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.General), DisplayName("Processus du jeu")]
    [Description("Nom du processus de Le Mans Ultimate (sans .exe).")]
    public string GameProcessName { get; set; } = "Le Mans Ultimate";

    [Category(Tabs.General), DisplayName("Délai après lancement du jeu (s)")]
    [Description("Mode Capture : attente avant de capturer la fenêtre de LMU, pour laisser Easy Anti-Cheat démarrer tranquillement.")]
    [Gauge(0, 60, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int GameStartDelaySeconds { get; set; } = 10;

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.General), DisplayName("Processus d'Assetto Corsa")]
    public string AcProcessName { get; set; } = "acs";

    // ---------- Écran VoCore ----------

    [Category(Tabs.Screen), DisplayName("Luminosité")]
    [Description("1 à 255 (0 = maximum).")]
    [Gauge(0, 255, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int VoCoreBrightness { get; set; } = 255;

    [Category(Tabs.Screen), DisplayName("Rotation")]
    [Description("Rotation de l'image si l'écran est monté à l'envers ou en portrait.")]
    public OutputRotation Rotation { get; set; } = OutputRotation.Aucune;

    [Category(Tabs.Screen), DisplayName("Miroir horizontal")]
    [Description("Inverse l'image gauche/droite.")]
    public bool FlipHorizontal { get; set; }

    [Category(Tabs.Screen), DisplayName("Étirer pour remplir")]
    [Description("Vrai : l'image remplit tout l'écran. Faux : les proportions sont conservées (bandes noires).")]
    public bool Stretch { get; set; } = true;

    [Category(Tabs.Screen), DisplayName("Images par seconde")]
    [Description("Fréquence maximale de rafraîchissement du rétroviseur.")]
    [Gauge(10, 60, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int TargetFps { get; set; } = 60;

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.Screen), DisplayName("VoCore USB : VID")]
    [Description("Identifiant fabricant USB (hexadécimal). VoCore = C872.")]
    public string VoCoreVendorId { get; set; } = "C872";

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.Screen), DisplayName("VoCore USB : PID")]
    [Description("Identifiant produit USB (hexadécimal). VoCore = 1004.")]
    public string VoCoreProductId { get; set; } = "1004";

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.Screen), DisplayName("VoCore USB : largeur native")]
    [Description("0 = automatique (400 pour le 7,8\" portrait). À régler si l'image est brouillée.")]
    public int VoCoreWidth { get; set; }

    [Browsable(false)] // réglage expert, modifiable dans le fichier de réglages
    [Category(Tabs.Screen), DisplayName("VoCore USB : hauteur native")]
    [Description("0 = automatique (1280 pour le 7,8\" portrait).")]
    public int VoCoreHeight { get; set; }

    // ---------- Capture ----------

    [Category(Tabs.Capture), DisplayName("Zone X")]
    public int CropX { get; set; } = 788;

    [Category(Tabs.Capture), DisplayName("Zone Y")]
    public int CropY { get; set; } = 42;

    [Category(Tabs.Capture), DisplayName("Zone largeur")]
    [Description("0 = image entière. Utiliser le bouton « Calibrer la zone » pour la sélectionner à la souris.")]
    public int CropWidth { get; set; } = 346;

    [Category(Tabs.Capture), DisplayName("Zone hauteur")]
    public int CropHeight { get; set; } = 108;

    [Category(Tabs.Capture), DisplayName("Cacher le rétro du jeu")]
    [Description("Pose un cache sur le rétro virtuel de LMU, sur l'écran principal. La capture n'est pas " +
                 "affectée : elle lit la fenêtre du jeu, pas l'écran.")]
    public bool HideMirrorOnScreen { get; set; } = true;

    [Category(Tabs.Capture), DisplayName("Cache : couleur du décor")]
    [Description("Vrai : le cache prend la couleur du décor autour de lui (voir « Points de couleur »), " +
                 "mise à jour en continu, pour se fondre dans le décor. Faux : cache noir.")]
    public bool MaskMatchColor { get; set; } = true;

    [Category(Tabs.Capture), DisplayName("Cache : points de couleur")]
    [Description("Points lus autour du cache pour reprendre la couleur du décor. Avec plusieurs points, le cache " +
                 "fait un dégradé entre leurs couleurs. Ajoutez-les, déplacez-les (clic gauche) ou supprimez-les " +
                 "(clic droit) directement dans « Calibrer la zone », ou ici.")]
    public List<MaskSample> MaskSamples { get; set; } = new()
    {
        new MaskSample { Side = MaskSide.Gauche, Position = 26, Distance = 1 },
        new MaskSample { Side = MaskSide.Gauche, Position = 75, Distance = 1 },
        new MaskSample { Side = MaskSide.Droite, Position = 38.1, Distance = 1 },
        new MaskSample { Side = MaskSide.Droite, Position = 75, Distance = 1 },
        new MaskSample { Side = MaskSide.Bas, Position = 50, Distance = 3 },
    };

    [Category(Tabs.Capture), DisplayName("Cache : forme")]
    [Description("Vide = rectangle (zone + marges). Sinon, contour libre qui reprend la forme du rétro du jeu (F1…) : " +
                 "tracez-le dans « Calibrer la zone » → « Tracer la forme du cache ». Les marges ne servent plus.")]
    public List<MaskPoint> MaskShape { get; set; } = new();

    [Category(Tabs.Capture), DisplayName("Cache : forme arrondie")]
    [Description("Vrai : le contour passe en douceur par les points (bords courbes) ; faux : lignes droites.")]
    public bool MaskShapeSmooth { get; set; }

    [Category(Tabs.Capture), DisplayName("Cache : marge à gauche (px)")]
    [Description("Agrandit le cache rectangulaire au-delà de la zone de capture (cadre du rétro, bord flou…). N'agrandit pas la capture.")]
    [Gauge(0, 600, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int MaskMarginLeft { get; set; } = 240;

    [Category(Tabs.Capture), DisplayName("Cache : marge à droite (px)")]
    [Gauge(0, 600, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int MaskMarginRight { get; set; } = 240;

    [Category(Tabs.Capture), DisplayName("Cache : marge en haut (px)")]
    [Gauge(0, 300, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int MaskMarginTop { get; set; } = 10;

    [Category(Tabs.Capture), DisplayName("Cache : marge en bas (px)")]
    [Gauge(0, 300, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int MaskMarginBottom { get; set; } = 10;

    // ---------- Radar ----------


    [Category(Tabs.Radar), DisplayName("Portée (m)")]
    [Gauge(20, 200, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double RadarRange { get; set; } = 80;

    [Category(Tabs.Radar), DisplayName("Champ de vision (°)")]
    [Gauge(20, 150, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double RadarFov { get; set; } = 70;

    [Category(Tabs.Radar), DisplayName("Afficher les noms")]
    public bool RadarShowNames { get; set; } = true;

    [Category(Tabs.Radar), DisplayName("Face avant par voiture (LMU)")]
    [Description("Le Mans Ultimate : chaque voiture a sa face avant (phares, calandre) selon sa marque et son modèle. " +
                 "La couleur reste celle de la classe.")]
    public bool RadarBrandFronts { get; set; } = true;

    [Category(Tabs.General), DisplayName("LMU / rF2 : inverser gauche/droite")]
    [Description("À activer si les voitures apparaissent du mauvais côté.")]
    public bool RadarInvertLateral { get; set; } = true;

    [Category(Tabs.General), DisplayName("Assetto Corsa : inverser gauche/droite")]
    [Description("À activer si, dans Assetto Corsa, les voitures apparaissent du mauvais côté.")]
    public bool AcInvertLateral { get; set; }

    [Category(Tabs.General), DisplayName("Jeux beta : inverser gauche/droite")]
    [Description("rFactor 2 excepté (réglage LMU) : à activer si, dans ACC, AC EVO, AMS2, iRacing ou F1, les voitures apparaissent du mauvais côté.")]
    public bool BetaInvertLateral { get; set; }

    [Category(Tabs.General), DisplayName("F1 : port de télémétrie UDP")]
    [Description("Port réglé dans F1 (Réglages → Télémétrie, UDP activée, format 2023 ou plus récent). " +
                 "Un seul logiciel peut l'écouter : dans SimHub, utilisez le renvoi UDP vers un autre port si besoin.")]
    public int F1UdpPort { get; set; } = 20777;

    // ---------- Caméra AC ----------

    [Category(Tabs.Camera), DisplayName("Champ de vision horizontal (°)")]
    [Description("Largeur de la vue arrière. Un vrai rétroviseur intérieur couvre environ 40 à 60°.")]
    [Gauge(10, 150, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double AcCamFov { get; set; } = 55;

    [Category(Tabs.Camera), DisplayName("Recul de la caméra (m)")]
    [Description("Distance derrière le centre de la voiture. Augmentez si l'arrière de votre voiture apparaît dans l'image.")]
    [Gauge(0, 10, 0.1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double AcCamBack { get; set; } = 2.4;

    [Category(Tabs.Camera), DisplayName("Hauteur de la caméra (m)")]
    [Gauge(0, 3, 0.05), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double AcCamUp { get; set; } = 1.0;

    [Category(Tabs.Camera), DisplayName("Largeur de rendu")]
    [Gauge(320, 2048, 16), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int AcCamResWidth { get; set; } = 1280;

    [Category(Tabs.Camera), DisplayName("Hauteur de rendu")]
    [Gauge(100, 1024, 8), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int AcCamResHeight { get; set; } = 400;

    [Category(Tabs.Camera), DisplayName("Images par seconde")]
    [Description("Chaque image est un rendu supplémentaire de la scène par AC : 30 est un bon compromis.")]
    [Gauge(5, 60, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int AcCamFps { get; set; } = 30;

    [Category(Tabs.Camera), DisplayName("Effet miroir")]
    [Description("Inverse gauche/droite comme un vrai rétroviseur.")]
    public bool AcCamMirror { get; set; } = true;

    [Category(Tabs.Camera), DisplayName("Exposition")]
    [Description("Correction de l'exposition automatique : 1 = neutre, 1,5 = plus clair, 0,7 = plus sombre. S'applique en direct.")]
    [Gauge(0.1, 4, 0.05), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double AcCamExposure { get; set; } = 0.7;

    [Category(Tabs.Camera), DisplayName("Gamma")]
    [Description("Éclaircit les zones sombres sans brûler les zones claires : 1 = inchangé, 1.5 à 2.2 = ombres plus claires. S'applique en direct.")]
    [Gauge(0.5, 3, 0.05), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double AcCamGamma { get; set; } = 2.2;

    // ---------- ATH ----------

    [Category(Tabs.Hud), DisplayName("Activé")]
    [Description("ATH façon caméra de recul Bosch : flèche colorée au-dessus des voitures derrière (vert > 1 s, orange > 0,5 s, rouge), échelles de distance et de temps sur les côtés. Tous les modes.")]
    public bool HudEnabled { get; set; } = true;

    [Category(Tabs.Hud), DisplayName("Taille des flèches (%)")]
    [Description("100 = taille normale ; de 20 à 400 %. Les flèches restent plus grosses sur les voitures proches.")]
    [Gauge(20, 400, 5), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudArrowSize { get; set; } = 200;

    [Category(Tabs.Hud), DisplayName("Inverser gauche/droite des flèches")]
    [Description("Modes caméra et capture : à activer si les flèches apparaissent du mauvais côté des voitures.")]
    public bool HudInvertSide { get; set; } = true;

    [Category(Tabs.Hud), DisplayName("Échelles distance / temps")]
    public bool HudShowScales { get; set; } = true;

    [Category(Tabs.Hud), DisplayName("Échelles en miroir")]
    [Description("Retourne les graduations et les chiffres des deux échelles (effet miroir).")]
    public bool HudMirrorScales { get; set; } = false;

    [Category(Tabs.Hud), DisplayName("Échelle de distance : maximum (m)")]
    [Description("Haut de l'échelle de gauche (0 m en bas), découpée en 10 graduations égales.")]
    [Gauge(10, 300, 5), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudScaleDistance { get; set; } = 100;

    [Category(Tabs.Hud), DisplayName("Échelle de temps : maximum (s)")]
    [Description("Haut de l'échelle de droite (0 s en bas), découpée en 10 graduations égales.")]
    [Gauge(0.5, 5, 0.1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudScaleTime { get; set; } = 2;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : champ de vision vertical (°)")]
    [Description("Point de vue du rétro virtuel du jeu, pour placer les flèches sur les voitures (propre à chaque jeu). " +
                 "Calculé par « Aligner les flèches » avec deux voitures ou plus.")]
    [Gauge(2, 60, 0.5), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudCaptureFov { get; set; } = 14;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : hauteur de l'œil (m)")]
    [Gauge(0, 3, 0.05), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudCaptureHeight { get; set; } = 0.9;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : position du rétro vers l'avant (m)")]
    [Gauge(-3, 3, 0.05), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudCaptureForward { get; set; } = 0.6;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : hauteur de l'horizon (%)")]
    [Description("Hauteur, dans l'image du rétro, du point où la route disparaît à l'horizon (0 = en haut, 100 = en bas). " +
                 "Si les flèches sont sous les voitures, baissez cette valeur ; au-dessus, augmentez-la. Le plus simple : bouton " +
                 "« Aligner les flèches », qui le calcule en faisant glisser les flèches sur les voitures.")]
    [Gauge(0, 100, 0.5), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudCaptureHorizon { get; set; } = 50;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : centre horizontal (%)")]
    [Description("Position, dans l'image du rétro, de l'axe de votre voiture (50 = milieu). À décaler si les flèches sont " +
                 "toutes un peu à gauche ou à droite des voitures (calculé par « Aligner les flèches »).")]
    [Gauge(0, 100, 0.5), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double HudCaptureCenter { get; set; } = 50;

    [ModeOnly(MirrorMode.Capture)]
    [Category(Tabs.Hud), DisplayName("Capture : repères de réglage")]
    [Description("Affiche sur le VoCore la ligne d'horizon et des repères au sol (10, 20, 40 et 80 m derrière, bords de " +
                 "voie) : réglez l'horizon, le champ de vision et la hauteur jusqu'à ce qu'ils suivent la route, puis désactivez.")]
    public bool HudCaptureGuides { get; set; }

    // ---------- LEDs ----------

    [Category(Tabs.Leds), DisplayName("Activées")]
    [Description("LEDs WS2812B pilotées par la carte MPro du VoCore : s'allument quand une voiture arrive ou est à côté de vous.")]
    public bool LedsEnabled { get; set; } = true;

    [Category(Tabs.Leds), DisplayName("LEDs par côté")]
    [Gauge(1, 32, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int LedsPerSide { get; set; } = 8;

    [Category(Tabs.Leds), DisplayName("Ordre de câblage")]
    [Description("DroiteGauche : la barrette droite est la première de la chaîne, puis la gauche.")]
    public LedChainOrder LedOrder { get; set; } = LedChainOrder.GaucheDroite;

    [Category(Tabs.Leds), DisplayName("Inverser le sens (droite)")]
    [Description("Active si les LEDs de droite se remplissent dans le mauvais sens.")]
    public bool LedInvertRight { get; set; } = true;

    [Category(Tabs.Leds), DisplayName("Inverser le sens (gauche)")]
    public bool LedInvertLeft { get; set; } = true;

    [Category(Tabs.Leds), DisplayName("Luminosité")]
    [Description("0 à 255.")]
    [Gauge(0, 255, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public int LedBrightness { get; set; } = 50;

    [Category(Tabs.Leds), DisplayName("Distance d'alerte (m)")]
    [Description("Une voiture qui arrive sur un côté est signalée à partir de cette distance derrière vous.")]
    [Gauge(5, 80, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double LedWarnDistance { get; set; } = 20;

    // Protocole des LEDs : toujours Is31Compatible (celui de SimHub), le choix a été retiré de la fenêtre.
    [Browsable(false)]
    public LedProtocol LedProtocol { get; set; } = LedProtocol.Is31Compatible;

    [Category(Tabs.Leds), DisplayName("Couleur selon la catégorie")]
    [Description("Vrai : les LEDs prennent la couleur de la catégorie de la voiture qui arrive ou qui est à côté " +
                 "(couleurs ci-dessous). Faux : jaune → orange quand elle arrive, rouge à côté.")]
    public bool LedClassColors { get; set; } = true;

    [Category(Tabs.Leds), DisplayName("Couleur Hypercar")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorHypercar { get; set; } = "#F52727";

    [Category(Tabs.Leds), DisplayName("Couleur LMP2")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorLmp2 { get; set; } = "#2768F5";

    [Category(Tabs.Leds), DisplayName("Couleur LMP3")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorLmp3 { get; set; } = "#7D27F5";

    [Category(Tabs.Leds), DisplayName("Couleur GTE")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorGte { get; set; } = "#F5C827";

    [Category(Tabs.Leds), DisplayName("Couleur GT3")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorGt3 { get; set; } = "#38F527";

    [Category(Tabs.Leds), DisplayName("Couleur autres voitures")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedColorOther { get; set; } = "#FFB400";

    [Category(Tabs.Leds), DisplayName("Catégories personnalisées")]
    [Description("Couleur pour d'autres catégories de voitures (ou pour remplacer celle d'une catégorie ci-dessus). " +
                 "Le menu déroulant liste toutes les catégories déjà vues dans la télémétrie des jeux.")]
    public List<LedClassColor> LedCustomClasses { get; set; } = new();

    [Category(Tabs.Leds), DisplayName("Détecteur de dive bomb")]
    [Description("Une voiture qui arrive très vite de derrière, déjà décalée d'un côté, fait clignoter rapidement " +
                 "les LEDs de ce côté avant qu'elle ne soit à votre hauteur.")]
    public bool LedDiveBomb { get; set; } = true;

    [Category(Tabs.Leds), DisplayName("Dive bomb : vitesse de rapprochement (km/h)")]
    [Description("Différence de vitesse minimale avec la voiture qui arrive.")]
    [Gauge(5, 100, 1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double LedDiveBombSpeed { get; set; } = 30;

    [Category(Tabs.Leds), DisplayName("Dive bomb : délai d'alerte (s)")]
    [Description("L'alerte se déclenche si la voiture sera à votre hauteur dans moins de ce temps.")]
    [Gauge(0.2, 3, 0.1), Editor(typeof(Ui.GaugeEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public double LedDiveBombTime { get; set; } = 1.0;

    [Category(Tabs.Leds), DisplayName("Dive bomb : couleur")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string LedDiveBombColor { get; set; } = "#FF0000";

    // ---------- Apparence ----------

    [Category(Tabs.Appearance), DisplayName("Couleur d'accent")]
    [Description("Couleur de la marque au format #RRVVBB (boutons, logo, liserés, écran d'accueil du VoCore).")]
    [Editor(typeof(Ui.ColorHexEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string AccentColor { get; set; } = Ui.Theme.DefaultAccent;

    [Category(Tabs.Appearance), DisplayName("Police")]
    [Description("Nom d'une police installée sur Windows (par défaut Bahnschrift).")]
    public string UiFont { get; set; } = Ui.Theme.DefaultFont;

    [Category(Tabs.Appearance), DisplayName("Écran de veille : image ou animation")]
    [Description("Image (PNG, JPG, BMP) ou animation (GIF animé) affichée sur le VoCore à la place de l'animation " +
                 "AVIX quand le jeu est en pause, absent ou en arrière-plan. Le logo AVIX reste affiché en petit en bas à " +
                 "droite. Vide = animation AVIX.")]
    [Editor(typeof(Ui.ImageFileEditor), typeof(System.Drawing.Design.UITypeEditor))]
    public string StandbyImage { get; set; } = "";

    [Category(Tabs.Appearance), DisplayName("Écran de veille : remplir l'écran")]
    [Description("Vrai : l'image remplit tout l'écran (bords coupés si besoin). Faux : image entière, avec des bandes noires.")]
    public bool StandbyImageFill { get; set; }

    // ---------- Profils par jeu ----------

    /// <summary>
    /// Réglages propres à chaque jeu : pour chaque jeu, les valeurs qui diffèrent des réglages communs.
    /// Seuls les réglages « par jeu » (voir <see cref="IsPerGame"/>) peuvent y figurer.
    /// </summary>
    [Browsable(false)]
    public Dictionary<string, Dictionary<string, JsonElement>> GameProfiles { get; set; } = DefaultProfiles();

    /// <summary>Profils par défaut (réglages du propriétaire : ACC, AC EVO, F1, LMU), ressource intégrée.</summary>
    static Dictionary<string, Dictionary<string, JsonElement>> DefaultProfiles()
    {
        try
        {
            using var stream = typeof(Settings).Assembly.GetManifestResourceStream("Defaults.Profiles.json");
            if (stream != null)
                return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, JsonElement>>>(stream) ?? new();
        }
        catch
        {
            // Ressource illisible : pas de profil par défaut.
        }
        return new();
    }

    static readonly HashSet<string> PerGameNames = new()
    {
        nameof(Mode), nameof(GameStartDelaySeconds), nameof(LedWarnDistance),
        nameof(LedDiveBomb), nameof(LedDiveBombSpeed), nameof(LedDiveBombTime),
    };

    static readonly HashSet<string> PerGameTabs = new() { Tabs.Capture, Tabs.Radar, Tabs.Camera, Tabs.Hud };

    /// <summary>Vrai si le réglage peut être différent pour chaque jeu (sinon il est commun à tous).</summary>
    public static bool IsPerGame(string propertyName)
    {
        if (PerGameNames.Contains(propertyName))
            return true;
        var property = typeof(Settings).GetProperty(propertyName);
        var category = property?.GetCustomAttributes(typeof(CategoryAttribute), false).OfType<CategoryAttribute>().FirstOrDefault();
        return category != null && PerGameTabs.Contains(category.Category);
    }

    /// <summary>Réglages effectifs pour un jeu : réglages communs + valeurs propres à ce jeu.</summary>
    public Settings ForGame(RadarGame? game)
    {
        var result = Clone();
        if (game is not { } g || !GameProfiles.TryGetValue(g.ToString(), out var values))
            return result;
        foreach (var (name, element) in values)
        {
            var property = typeof(Settings).GetProperty(name);
            if (property == null || !property.CanWrite || !IsPerGame(name))
                continue;
            try
            {
                property.SetValue(result, element.Deserialize(property.PropertyType, JsonOptions));
            }
            catch
            {
                // Valeur illisible (ancienne version) : la valeur commune s'applique.
            }
        }
        return result;
    }

    /// <summary>
    /// Modifie un réglage : pour le jeu donné si le réglage est « par jeu », sinon pour tous les jeux.
    /// </summary>
    public void Set(RadarGame? game, string propertyName, object? value)
    {
        var property = typeof(Settings).GetProperty(propertyName);
        if (property == null)
            return;
        if (game is { } g && IsPerGame(propertyName))
        {
            var key = g.ToString();
            if (!GameProfiles.TryGetValue(key, out var values))
                GameProfiles[key] = values = new Dictionary<string, JsonElement>();
            values[propertyName] = JsonSerializer.SerializeToElement(value, property.PropertyType, JsonOptions);
        }
        else
        {
            property.SetValue(this, value);
        }
    }

    /// <summary>Nombre de réglages propres à un jeu.</summary>
    public int OverrideCount(RadarGame game) =>
        GameProfiles.TryGetValue(game.ToString(), out var values) ? values.Count : 0;

    /// <summary>Le jeu reprend tous les réglages communs.</summary>
    public void ClearOverrides(RadarGame game) => GameProfiles.Remove(game.ToString());

    // ---------- Persistance ----------

    const int CurrentVersion = 5;

    [Browsable(false)]
    public int SettingsVersion { get; set; }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "AviXMirror.settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                // Le mode « Direct » a été retiré en v2.
                var json = File.ReadAllText(FilePath).Replace("\"Direct\"", "\"Capture\"");
                var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
                if (loaded.SettingsVersion < 2)
                {
                    // v2 : l'extension de fenêtre (retirée depuis) déformait le jeu, remplacée par le cache.
                    loaded.HideMirrorOnScreen = true;
                    loaded.SettingsVersion = 2;
                }
                if (loaded.SettingsVersion < 3)
                {
                    // v3 : couleur de la marque AVIX (l'ancien orange par défaut est remplacé).
                    if (string.Equals(loaded.AccentColor, "#FF5A1F", StringComparison.OrdinalIgnoreCase))
                        loaded.AccentColor = Ui.Theme.DefaultAccent;
                    loaded.SettingsVersion = 3;
                }
                if (loaded.SettingsVersion < 4)
                {
                    // v4 : exposition automatique HDR ; les anciennes valeurs par défaut deviennent neutres.
                    if (Math.Abs(loaded.AcCamExposure - 1.8) < 1e-6) loaded.AcCamExposure = 1.0;
                    if (Math.Abs(loaded.AcCamGamma - 1.4) < 1e-6) loaded.AcCamGamma = 1.0;
                    loaded.SettingsVersion = 4;
                }
                loaded.SettingsVersion = CurrentVersion;
                loaded.LedProtocol = LedProtocol.Is31Compatible;
                return loaded;
            }
        }
        catch
        {
            // Fichier corrompu : on repart des valeurs par défaut.
        }
        FirstRun = true;
        return new Settings { SettingsVersion = CurrentVersion };
    }

    /// <summary>Vrai au tout premier lancement (pas encore de fichier de réglages).</summary>
    [JsonIgnore, Browsable(false)]
    public static bool FirstRun { get; private set; }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Dossier en lecture seule : les réglages ne seront pas conservés.
        }
    }

    public Settings Clone() =>
        JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
