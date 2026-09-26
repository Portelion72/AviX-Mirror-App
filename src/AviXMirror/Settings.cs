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
    /// <summary>Aucune fenêtre : le jeu dessine lui-même sur le VoCore (nécessite le pilote d'écran VoCore).</summary>
    Direct,
}

public enum OutputTarget
{
    /// <summary>Envoi direct au VoCore en USB (comme SimHub), sans pilote d'écran.</summary>
    VoCoreUsb,
    /// <summary>Fenêtre plein écran sur un écran Windows.</summary>
    EcranWindows,
}

public enum CaptureSource
{
    FenetreJeu,
    Ecran,
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
                 "Radar : rétro synthétique dessiné depuis la télémétrie (plugin rF2 Shared Memory).\n" +
                 "Direct : le jeu est étendu sur le VoCore (nécessite le pilote d'écran VoCore, déconseillé avec Easy Anti-Cheat).")]
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

    [Category("2. Sortie VoCore"), DisplayName("Sortie")]
    [Description("VoCoreUsb : l'image est envoyée directement au VoCore en USB, comme SimHub. Aucun pilote d'écran, " +
                 "compatible Easy Anti-Cheat. Fermez SimHub (ou désactivez-y le VoCore) pendant l'utilisation.\n" +
                 "EcranWindows : fenêtre plein écran sur un écran Windows.")]
    public OutputTarget Output { get; set; } = OutputTarget.VoCoreUsb;

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

    [Category("2. Sortie VoCore"), DisplayName("Écran de sortie")]
    [Description("Écran Windows utilisé si Sortie = EcranWindows.")]
    [TypeConverter(typeof(ScreenNameConverter))]
    public string OutputScreen { get; set; } = "";

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

    [Category("4. Fenêtre du jeu"), DisplayName("Étendre la fenêtre de LMU")]
    [Description("Agrandit la fenêtre de LMU (mode fenêtré/sans bordure) au-delà de l'écran principal.\n" +
                 "La bande supplémentaire tombe sur le VoCore ou hors de l'écran : on y place le rétro virtuel " +
                 "dans l'éditeur de HUD, il n'est donc plus visible sur l'écran principal.")]
    public bool ExtendGameWindow { get; set; } = true;

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

    [Category("5. Radar"), DisplayName("Portée (m)")]
    public double RadarRange { get; set; } = 80;

    [Category("5. Radar"), DisplayName("Champ de vision (°)")]
    public double RadarFov { get; set; } = 70;

    [Category("5. Radar"), DisplayName("Inverser gauche/droite")]
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
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), JsonOptions) ?? new Settings();
        }
        catch
        {
            // Fichier corrompu : on repart des valeurs par défaut.
        }
        return new Settings();
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
