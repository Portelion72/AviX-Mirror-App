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
                 "Radar : rétro synthétique dessiné depuis la télémétrie (plugin rF2 Shared Memory).")]
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
    [Description("Pose un cache noir sur la zone du rétro virtuel, sur l'écran principal. La capture n'est pas " +
                 "affectée : elle lit la fenêtre du jeu, pas l'écran. Placez un petit rétro dans un coin (ex. en haut, sur le toit).")]
    public bool HideMirrorOnScreen { get; set; } = true;

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

    [Category("5. Radar"), DisplayName("Largeur de rendu")]
    [Description("Résolution de l'image radar (utilisée pour le flux MJPEG). 0 = taille de la fenêtre.")]
    public int RadarWidth { get; set; } = 1280;

    [Category("5. Radar"), DisplayName("Hauteur de rendu")]
    public int RadarHeight { get; set; } = 400;

    // ---------- Persistance ----------

    const int CurrentVersion = 2;

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
