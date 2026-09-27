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

public enum CaptureSource
{
    FenetreJeu,
    Ecran,
}

public enum RadarGame
{
    Auto,
    LeMansUltimate,
    AssettoCorsa,
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

public enum StripSide
{
    Haut,
    Bas,
}

public enum OutputRotation
{
    Aucune = 0,
    Rotation90 = 90,
    Rotation180 = 180,
    Rotation270 = 270,
}

public sealed class Settings
{
    // ---------- Général ----------

    [Category("1. Général"), DisplayName("Mode")]
    [Description("Capture : recopie le rétro virtuel du jeu sur le VoCore.\n" +
                 "Radar : rétro synthétique dessiné depuis la télémétrie (LMU ou Assetto Corsa).\n" +
                 "CameraAssettoCorsa : vraie image de ce qui est derrière, rendue par AC hors écran (app Lua CSP).")]
    public MirrorMode Mode { get; set; } = MirrorMode.Capture;

    [Category("1. Général"), DisplayName("Démarrage automatique")]
    [Description("Démarre le rétroviseur dès l'ouverture de l'application.")]
    public bool AutoStart { get; set; }

    [Category("1. Général"), DisplayName("Processus du jeu")]
    [Description("Nom du processus de Le Mans Ultimate (sans .exe).")]
    public string GameProcessName { get; set; } = "Le Mans Ultimate";

    [Category("1. Général"), DisplayName("Délai après lancement du jeu (s)")]
    [Description("Attente avant de capturer ou de modifier la fenêtre de LMU, pour laisser Easy Anti-Cheat démarrer tranquillement.")]
    public int GameStartDelaySeconds { get; set; } = 30;

    // ---------- Sortie (VoCore) ----------

    [Category("2. Sortie VoCore"), DisplayName("VoCore USB : VID")]
    [Description("Identifiant fabricant USB (hexadécimal). VoCore = C872.")]
    public string VoCoreVendorId { get; set; } = "C872";

    [Category("2. Sortie VoCore"), DisplayName("VoCore USB : PID")]
    [Description("Identifiant produit USB (hexadécimal). VoCore = 1004.")]
    public string VoCoreProductId { get; set; } = "1004";

    [Category("2. Sortie VoCore"), DisplayName("VoCore USB : largeur native")]
    [Description("0 = automatique (400 pour le 7,8\" portrait). À régler si l'image est brouillée.")]
    public int VoCoreWidth { get; set; }

    [Category("2. Sortie VoCore"), DisplayName("VoCore USB : hauteur native")]
    [Description("0 = automatique (1280 pour le 7,8\" portrait).")]
    public int VoCoreHeight { get; set; }

    [Category("2. Sortie VoCore"), DisplayName("VoCore USB : luminosité")]
    [Description("1 à 255. 0 = ne pas modifier.")]
    public int VoCoreBrightness { get; set; }

    [Category("2. Sortie VoCore"), DisplayName("Rotation")]
    [Description("Rotation de l'image si l'écran est monté à l'envers ou en portrait.")]
    public OutputRotation Rotation { get; set; } = OutputRotation.Aucune;

    [Category("2. Sortie VoCore"), DisplayName("Miroir horizontal")]
    [Description("Inverse l'image gauche/droite.")]
    public bool FlipHorizontal { get; set; }

    [Category("2. Sortie VoCore"), DisplayName("Étirer pour remplir")]
    [Description("Vrai : l'image remplit tout l'écran. Faux : les proportions sont conservées (bandes noires).")]
    public bool Stretch { get; set; } = true;

    [Category("2. Sortie VoCore"), DisplayName("Images par seconde")]
    [Description("Fréquence maximale de rafraîchissement du rétroviseur.")]
    public int TargetFps { get; set; } = 60;

    [Category("2. Sortie VoCore"), DisplayName("Flux MJPEG (port)")]
    [Description("0 = désactivé. Sinon diffuse le rétro sur http://localhost:PORT/ (navigateur, tablette, SimHub…).")]
    public int MjpegPort { get; set; }

    // ---------- Capture ----------

    [Category("3. Capture"), DisplayName("Source")]
    [Description("FenetreJeu : capture la fenêtre de LMU, même les parties hors écran ou cachées (recommandé).\n" +
                 "Ecran : capture un écran entier (comme SimHub).")]
    public CaptureSource Source { get; set; } = CaptureSource.FenetreJeu;

    [Category("3. Capture"), DisplayName("Écran source")]
    [Description("Écran capturé si Source = Ecran.")]
    [TypeConverter(typeof(ScreenNameConverter))]
    public string SourceScreen { get; set; } = "";

    [Category("3. Capture"), DisplayName("Zone X")]
    public int CropX { get; set; }

    [Category("3. Capture"), DisplayName("Zone Y")]
    public int CropY { get; set; }

    [Category("3. Capture"), DisplayName("Zone largeur")]
    [Description("0 = image entière. Utiliser le bouton « Calibrer la zone » pour la sélectionner à la souris.")]
    public int CropWidth { get; set; }

    [Category("3. Capture"), DisplayName("Zone hauteur")]
    public int CropHeight { get; set; }

    [Category("3. Capture"), DisplayName("Afficher le curseur")]
    public bool CaptureCursor { get; set; }

    // ---------- Fenêtre du jeu ----------

    [Category("4. Fenêtre du jeu"), DisplayName("Masquer le rétro sur l'écran")]
    [Description("Pose un cache sur la zone du rétro virtuel, sur l'écran principal (couleur du décor, marges réglables). La capture n'est pas " +
                 "affectée : elle lit la fenêtre du jeu, pas l'écran. Placez un petit rétro dans un coin (ex. en haut, sur le toit).")]
    public bool HideMirrorOnScreen { get; set; } = true;

    [Category("4. Fenêtre du jeu"), DisplayName("Cache : marge à gauche (px)")]
    [Description("Agrandit le cache au-delà de la zone de capture (cadre du rétro, bord flou…). N'agrandit pas la capture.")]
    public int MaskMarginLeft { get; set; }

    [Category("4. Fenêtre du jeu"), DisplayName("Cache : marge à droite (px)")]
    public int MaskMarginRight { get; set; }

    [Category("4. Fenêtre du jeu"), DisplayName("Cache : marge en haut (px)")]
    public int MaskMarginTop { get; set; }

    [Category("4. Fenêtre du jeu"), DisplayName("Cache : marge en bas (px)")]
    public int MaskMarginBottom { get; set; }

    [Category("4. Fenêtre du jeu"), DisplayName("Cache : couleur du décor")]
    [Description("Vrai : le cache prend la couleur de l'image juste en dessous de lui (1 px sous son bord), " +
                 "mise à jour en continu, pour se fondre dans le décor. Faux : cache noir.")]
    public bool MaskMatchColor { get; set; } = true;

    [Category("4. Fenêtre du jeu"), DisplayName("Étendre la fenêtre de LMU (expérimental)")]
    [Description("Agrandit la fenêtre de LMU au-delà de l'écran pour y cacher le rétro. " +
                 "ATTENTION : l'image du jeu est déformée si LMU ne rend pas à la taille de la fenêtre.")]
    public bool ExtendGameWindow { get; set; }

    [Category("4. Fenêtre du jeu"), DisplayName("Hauteur de la bande (px)")]
    [Description("Hauteur ajoutée à la fenêtre du jeu, où se place le rétro virtuel (hors de l'écran).")]
    public int ExtensionPixels { get; set; } = 400;

    [Category("4. Fenêtre du jeu"), DisplayName("Côté de la bande")]
    [Description("Côté de l'écran principal par lequel la fenêtre du jeu dépasse.")]
    public StripSide ExtensionSide { get; set; } = StripSide.Haut;

    [Category("4. Fenêtre du jeu"), DisplayName("Écran du jeu")]
    [Description("Écran principal sur lequel tourne LMU (vide = écran principal de Windows).")]
    [TypeConverter(typeof(ScreenNameConverter))]
    public string GameScreen { get; set; } = "";

    // ---------- Radar ----------

    // ---------- Caméra Assetto Corsa ----------

    [Category("6. Caméra Assetto Corsa"), DisplayName("Champ de vision horizontal (°)")]
    [Description("Largeur de la vue arrière. Un vrai rétroviseur intérieur couvre environ 40 à 60°.")]
    public double AcCamFov { get; set; } = 55;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Recul de la caméra (m)")]
    [Description("Distance derrière le centre de la voiture. Augmentez si l'arrière de votre voiture apparaît dans l'image.")]
    public double AcCamBack { get; set; } = 2.4;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Hauteur de la caméra (m)")]
    public double AcCamUp { get; set; } = 1.0;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Largeur de rendu")]
    public int AcCamResWidth { get; set; } = 1280;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Hauteur de rendu")]
    public int AcCamResHeight { get; set; } = 400;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Images par seconde")]
    [Description("Chaque image est un rendu supplémentaire de la scène par AC : 30 est un bon compromis.")]
    public int AcCamFps { get; set; } = 30;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Effet miroir")]
    [Description("Inverse gauche/droite comme un vrai rétroviseur.")]
    public bool AcCamMirror { get; set; } = true;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Exposition")]
    [Description("Correction de l'exposition automatique : 1 = neutre, 1,5 = plus clair, 0,7 = plus sombre. S'applique en direct.")]
    public double AcCamExposure { get; set; } = 1.0;

    [Category("6. Caméra Assetto Corsa"), DisplayName("Gamma")]
    [Description("Éclaircit les zones sombres sans brûler les zones claires : 1 = inchangé, 1.5 à 2.2 = ombres plus claires. S'applique en direct.")]
    public double AcCamGamma { get; set; } = 1.0;

    [Category("5. Radar"), DisplayName("Jeu")]
    [Description("Auto : détecte Le Mans Ultimate ou Assetto Corsa (app Lua « AviX Mirror » pour CSP requise).")]
    public RadarGame RadarGame { get; set; } = RadarGame.Auto;

    [Category("5. Radar"), DisplayName("Assetto Corsa : inverser gauche/droite")]
    [Description("À activer si, dans Assetto Corsa, les voitures apparaissent du mauvais côté.")]
    public bool AcInvertLateral { get; set; }

    [Category("5. Radar"), DisplayName("Portée (m)")]
    public double RadarRange { get; set; } = 80;

    [Category("5. Radar"), DisplayName("Champ de vision (°)")]
    public double RadarFov { get; set; } = 70;

    [Category("5. Radar"), DisplayName("LMU : inverser gauche/droite")]
    [Description("À activer si les voitures apparaissent du mauvais côté.")]
    public bool RadarInvertLateral { get; set; }

    [Category("5. Radar"), DisplayName("Afficher les noms")]
    public bool RadarShowNames { get; set; } = true;

    [Category("5. Radar"), DisplayName("Face avant par voiture (LMU)")]
    [Description("Le Mans Ultimate : chaque voiture a sa face avant (phares, calandre) selon sa marque et son modèle. " +
                 "La couleur reste celle de la classe.")]
    public bool RadarBrandFronts { get; set; } = true;

    [Category("5. Radar"), DisplayName("Largeur de rendu")]
    [Description("Résolution de l'image radar (utilisée pour le flux MJPEG). 0 = taille de la fenêtre.")]
    public int RadarWidth { get; set; } = 1280;

    [Category("5. Radar"), DisplayName("Hauteur de rendu")]
    public int RadarHeight { get; set; } = 400;

    // ---------- LEDs spotter ----------

    [Category("8. LEDs spotter"), DisplayName("Activées")]
    [Description("LEDs WS2812B pilotées par la carte MPro du VoCore : s'allument quand une voiture arrive ou est à côté de vous.")]
    public bool LedsEnabled { get; set; } = true;

    [Category("8. LEDs spotter"), DisplayName("LEDs par côté")]
    public int LedsPerSide { get; set; } = 8;

    [Category("8. LEDs spotter"), DisplayName("Ordre de câblage")]
    [Description("DroiteGauche : la barrette droite est la première de la chaîne, puis la gauche.")]
    public LedChainOrder LedOrder { get; set; } = LedChainOrder.DroiteGauche;

    [Category("8. LEDs spotter"), DisplayName("Inverser le sens (droite)")]
    [Description("Active si les LEDs de droite se remplissent dans le mauvais sens.")]
    public bool LedInvertRight { get; set; }

    [Category("8. LEDs spotter"), DisplayName("Inverser le sens (gauche)")]
    public bool LedInvertLeft { get; set; }

    [Category("8. LEDs spotter"), DisplayName("Luminosité")]
    [Description("0 à 255.")]
    public int LedBrightness { get; set; } = 140;

    [Category("8. LEDs spotter"), DisplayName("Distance d'alerte (m)")]
    [Description("Une voiture qui arrive sur un côté est signalée à partir de cette distance derrière vous.")]
    public double LedWarnDistance { get; set; } = 25;

    [Category("8. LEDs spotter"), DisplayName("Protocole")]
    [Description("Is31Compatible : protocole standard des LEDs VoCore (celui de SimHub). Complet : firmware « 512 LEDs ».")]
    public LedProtocol LedProtocol { get; set; } = LedProtocol.Is31Compatible;

    // ---------- ATH ----------

    [Category("9. ATH caméra de recul"), DisplayName("Activé")]
    [Description("ATH façon caméra de recul Bosch : flèche colorée au-dessus des voitures derrière (vert > 1 s, orange > 0,5 s, rouge), échelles de distance et de temps sur les côtés. Tous les modes.")]
    public bool HudEnabled { get; set; } = true;

    [Category("9. ATH caméra de recul"), DisplayName("Taille des flèches (%)")]
    [Description("100 = taille normale ; de 20 à 400 %. Les flèches restent plus grosses sur les voitures proches.")]
    public double HudArrowSize { get; set; } = 100;

    [Category("9. ATH caméra de recul"), DisplayName("Inverser gauche/droite des flèches")]
    [Description("Modes caméra et capture : à activer si les flèches apparaissent du mauvais côté des voitures.")]
    public bool HudInvertSide { get; set; }

    [Category("9. ATH caméra de recul"), DisplayName("Échelles distance / temps")]
    public bool HudShowScales { get; set; } = true;

    [Category("9. ATH caméra de recul"), DisplayName("Échelles en miroir")]
    [Description("Retourne les graduations et les chiffres des deux échelles (effet miroir).")]
    public bool HudMirrorScales { get; set; } = true;

    [Category("9. ATH caméra de recul"), DisplayName("Échelle de distance : maximum (m)")]
    [Description("Haut de l'échelle de gauche (0 m en bas), découpée en 10 graduations égales.")]
    public double HudScaleDistance { get; set; } = 100;

    [Category("9. ATH caméra de recul"), DisplayName("Échelle de temps : maximum (s)")]
    [Description("Haut de l'échelle de droite (0 s en bas), découpée en 10 graduations égales.")]
    public double HudScaleTime { get; set; } = 2;

    [Category("9. ATH caméra de recul"), DisplayName("Capture LMU : champ de vision vertical (°)")]
    [Description("Point de vue approché du rétro virtuel de LMU, pour placer les flèches sur les voitures. À ajuster si elles sont décalées.")]
    public double HudCaptureFov { get; set; } = 14;

    [Category("9. ATH caméra de recul"), DisplayName("Capture LMU : hauteur de l'œil (m)")]
    public double HudCaptureHeight { get; set; } = 0.9;

    [Category("9. ATH caméra de recul"), DisplayName("Capture LMU : position du rétro vers l'avant (m)")]
    public double HudCaptureForward { get; set; } = 0.6;

    // ---------- Apparence ----------

    [Category("7. Apparence"), DisplayName("Couleur d'accent")]
    [Description("Couleur de la marque au format #RRVVBB (boutons, logo, liserés, écran d'accueil du VoCore).")]
    public string AccentColor { get; set; } = Ui.Theme.DefaultAccent;

    [Category("7. Apparence"), DisplayName("Police")]
    [Description("Nom d'une police installée sur Windows (par défaut Bahnschrift).")]
    public string UiFont { get; set; } = Ui.Theme.DefaultFont;

    // ---------- Persistance ----------

    const int CurrentVersion = 4;

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
                    // v2 : l'extension de fenêtre déformait le jeu, elle est désactivée au profit du cache.
                    loaded.ExtendGameWindow = false;
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
                    loaded.SettingsVersion = CurrentVersion;
                }
                return loaded;
            }
        }
        catch
        {
            // Fichier corrompu : on repart des valeurs par défaut.
        }
        return new Settings { SettingsVersion = CurrentVersion };
    }

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

/// <summary>Liste déroulante des écrans dans la grille de propriétés.</summary>
public sealed class ScreenNameConverter : StringConverter
{
    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        new(Screen.AllScreens.Select(ScreenHelper.Describe).Prepend("").ToArray());
}

public static class ScreenHelper
{
    public static string Describe(Screen s) =>
        $"{s.DeviceName} ({s.Bounds.Width}x{s.Bounds.Height} @ {s.Bounds.X},{s.Bounds.Y}){(s.Primary ? " principal" : "")}";

    /// <summary>Retrouve un écran à partir de la valeur stockée ("\\.\DISPLAY2 (...)").</summary>
    public static Screen? Find(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var device = value.Split(' ')[0];
        return Screen.AllScreens.FirstOrDefault(s => s.DeviceName.Equals(device, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Premier écran secondaire, sinon l'écran principal.</summary>
    public static Screen DefaultOutput() =>
        Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;
}
