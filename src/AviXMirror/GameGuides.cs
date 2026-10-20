namespace AviXMirror;

/// <summary>Configuration conseillée d'un jeu : étapes dans le jeu, étapes dans AviX Mirror et réglages de base.</summary>
public sealed record GameGuide(RadarGame Game, string Summary, string[] InGame, string[] InAviX, (string Name, object Value, string Label)[] Recommended);

/// <summary>
/// Guides de configuration par jeu. Les réglages conseillés sont des points de départ : la zone de capture
/// et l'alignement des flèches dépendent de l'écran et de la position du rétro dans le jeu
/// (« Calibrer la zone » puis « Aligner les flèches »).
/// </summary>
public static class GameGuides
{
    const string Borderless = "Affichage en « Fenêtré sans bordure » (ou fenêtré) : la capture lit la fenêtre du jeu.";
    const string CaptureSteps = "AviX Mirror : mode Capture, DÉMARRER, puis au volant « Calibrer la zone » (cadre sur le rétro virtuel).";
    const string AlignSteps = "« Aligner les flèches » : figez l'image et glissez les flèches au-dessus des voitures (2 voitures = champ de vision calculé).";

    public static readonly GameGuide[] All =
    {
        new(RadarGame.LeMansUltimate,
            "Capture du rétro virtuel conseillée (vraie image), Radar en secours.",
            new[]
            {
                Borderless,
                "Affichez le rétro virtuel (option du HUD) et placez-le où il vous gêne le moins : il sera caché par AviX Mirror.",
                "Télémétrie (ATH, LEDs, Radar) : plugin rF2 Shared Memory Map actif (SimHub l'installe : Plugins\\rFactor2SharedMemoryMapPlugin64.dll).",
            },
            new[]
            {
                CaptureSteps + " Attendez la fin du délai Easy Anti-Cheat (10 s).",
                "Cache : points de couleur autour du cadre pour qu'il se fonde dans le décor.",
                AlignSteps,
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Capture, "Mode Capture"),
                (nameof(Settings.GameStartDelaySeconds), 10, "Délai anti-triche : 10 s"),
            }),

        new(RadarGame.AssettoCorsa,
            "Caméra AC conseillée (vraie vue arrière rendue hors écran), Radar possible.",
            new[]
            {
                "Content Manager + Custom Shaders Patch installés.",
                "Bouton « Installer l'app Assetto Corsa » (à refaire après chaque mise à jour d'AviX Mirror) : l'app démarre toute seule avec le jeu.",
            },
            new[]
            {
                "AviX Mirror : mode Caméra Assetto Corsa, DÉMARRER.",
                "Onglet Caméra AC : champ de vision 50–60°, recul 2,4 m ; exposition et gamma en direct si l'image est trop sombre ou trop claire.",
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.CameraAssettoCorsa, "Mode Caméra AC"),
                (nameof(Settings.GameStartDelaySeconds), 0, "Pas de délai anti-triche"),
            }),

        new(RadarGame.RFactor2,
            "Radar conseillé (beta).",
            new[]
            {
                "Installez le plugin rF2 Shared Memory Map (rFactor2SharedMemoryMapPlugin64.dll dans Bin64\\Plugins, SimHub peut le faire).",
                "Activez-le dans UserData\\player\\CustomPluginVariables.json (« Enabled »: 1), puis relancez le jeu.",
            },
            new[]
            {
                "AviX Mirror : mode Radar, DÉMARRER. Le tracé du circuit s'apprend pendant les premiers tours.",
                "Voitures du mauvais côté : Général → « LMU / rF2 : inverser gauche/droite ».",
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Radar, "Mode Radar"),
                (nameof(Settings.GameStartDelaySeconds), 0, "Pas de délai"),
            }),

        new(RadarGame.AssettoCorsaCompetizione,
            "Capture du rétro virtuel (beta), ATH et LEDs par la télémétrie officielle.",
            new[]
            {
                Borderless,
                "Affichez le rétro virtuel dans le HUD.",
                "Télémétrie : rien à configurer (mémoire partagée officielle toujours active).",
            },
            new[]
            {
                CaptureSteps,
                AlignSteps + " Sur ACC l'horizon du rétro est plus haut que sur LMU (point de départ : 40 %).",
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Capture, "Mode Capture"),
                (nameof(Settings.GameStartDelaySeconds), 3, "Délai : 3 s"),
                (nameof(Settings.HudCaptureHorizon), 40.0, "Horizon de l'ATH : 40 % (à affiner)"),
            }),

        new(RadarGame.AssettoCorsaEvo,
            "Capture du rétro virtuel (beta), ATH et LEDs par la télémétrie officielle.",
            new[]
            {
                Borderless,
                "Affichez le rétro virtuel dans le HUD.",
                "Télémétrie : rien à configurer (mémoire partagée officielle).",
            },
            new[]
            {
                CaptureSteps,
                AlignSteps,
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Capture, "Mode Capture"),
                (nameof(Settings.GameStartDelaySeconds), 3, "Délai : 3 s"),
            }),

        new(RadarGame.Automobilista2,
            "Capture du rétro virtuel (beta), ATH et LEDs par la mémoire partagée.",
            new[]
            {
                "Options → Système → Mémoire partagée = « Project CARS 2 » (indispensable), puis relancez le jeu.",
                Borderless,
                "Affichez le rétro virtuel dans le HUD.",
            },
            new[]
            {
                CaptureSteps,
                AlignSteps,
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Capture, "Mode Capture"),
                (nameof(Settings.GameStartDelaySeconds), 3, "Délai : 3 s"),
            }),

        new(RadarGame.IRacing,
            "Radar conseillé (beta) : iRacing ne donne pas la position des autres voitures, seulement la distance et le spotter.",
            new[]
            {
                "Rien à configurer : la télémétrie officielle (SDK) est toujours active.",
                "Capture possible (rétro virtuel du jeu en « Fenêtré sans bordure »), mais les flèches de l'ATH ne peuvent pas être placées exactement sur les voitures.",
            },
            new[]
            {
                "AviX Mirror : mode Radar, DÉMARRER.",
                "Les LEDs utilisent le spotter d'iRacing (voiture à gauche / à droite).",
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Radar, "Mode Radar"),
                (nameof(Settings.GameStartDelaySeconds), 10, "Délai anti-triche : 10 s"),
            }),

        new(RadarGame.F1,
            "Capture du rétro (beta) avec cache à la forme du rétro F1, ATH et LEDs par la télémétrie UDP.",
            new[]
            {
                "Réglages → Paramètres de télémétrie : Télémétrie UDP activée, Diffusion désactivée, IP 127.0.0.1, port 20777, fréquence 60 Hz, format UDP 2023 ou plus récent.",
                "SimHub écoute déjà le port ? Utilisez son renvoi UDP vers un autre port et reportez-le dans Général → « F1 : port de télémétrie UDP ».",
                Borderless + " Affichez le rétro dans le HUD.",
            },
            new[]
            {
                CaptureSteps,
                "Dans « Calibrer la zone », « Tracer la forme du cache » : cliquez tout le long du bord du rétro F1, cochez « Forme arrondie ».",
                AlignSteps,
            },
            new (string, object, string)[]
            {
                (nameof(Settings.Mode), MirrorMode.Capture, "Mode Capture"),
                (nameof(Settings.GameStartDelaySeconds), 10, "Délai anti-triche : 10 s"),
                (nameof(Settings.MaskShapeSmooth), true, "Cache à forme arrondie"),
            }),
    };

    public static GameGuide Get(RadarGame game) => All.First(g => g.Game == game);
}
