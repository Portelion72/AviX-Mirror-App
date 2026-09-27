namespace AviXMirror.Radar;

/// <summary>Une voiture, dans un format commun à tous les jeux.</summary>
public sealed class RadarVehicle
{
    public int Id;
    public bool IsPlayer;

    /// <summary>Position et vitesse dans le repère du monde (y = haut), en m et m/s.</summary>
    public RF2Vec3 Position, Velocity;

    /// <summary>Vecteur « gauche » de la voiture dans le monde (pour l'apprentissage du tracé).</summary>
    public RF2Vec3 Left;

    /// <summary>
    /// Orientation au format rF2 (lignes de la matrice local -> monde ; local : x = gauche, y = haut, z = arrière).
    /// Obligatoire pour le joueur.
    /// </summary>
    public RF2Vec3[]? Orientation;

    /// <summary>Vitesse de rotation (rad/s) dans le repère local, pour prolonger l'orientation entre deux relevés.</summary>
    public RF2Vec3 LocalRotation;

    /// <summary>Distance parcourue sur le tour (m), NaN si inconnue.</summary>
    public double LapDist = double.NaN;

    /// <summary>Décalage latéral par rapport au centre de piste et distance au bord (m), 0 si inconnus.</summary>
    public double PathLateral, TrackEdge;

    public string Class = "", Name = "";

    /// <summary>Nom de la voiture (LMU : modèle et équipe), pour la face avant par marque.</summary>
    public string Model = "";
    public int Place;
    public bool Headlights, InPits;
}

/// <summary>État de la course à un instant donné.</summary>
public sealed class RadarWorld
{
    public string Game = "";
    public string TrackName = "";
    public double TrackLength;

    /// <summary>Horloge du jeu : change à chaque nouveau relevé.</summary>
    public double Time;

    public List<RadarVehicle> Vehicles = new();
    public RadarVehicle? Player;

    /// <summary>Vrai si la gauche et la droite doivent être inversées (réglage par jeu).</summary>
    public bool InvertLateral;
}

/// <summary>Source de télémétrie d'un jeu.</summary>
public interface IRadarTelemetry : IDisposable
{
    string GameName { get; }

    /// <summary>Lit l'état courant. Retourne faux (avec un message) si le jeu ou son plugin ne répond pas.</summary>
    bool TryRead(Settings settings, out RadarWorld? world, out string status);
}
