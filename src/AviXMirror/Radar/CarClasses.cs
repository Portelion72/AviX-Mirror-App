namespace AviXMirror.Radar;

public enum CarKind { Hypercar, Lmp2, Lmp3, Gte, Gt3, Formula, Other }

/// <summary>Catégorie d'une voiture, commune au radar et aux LEDs du spotter.</summary>
public static class CarClasses
{
    /// <summary>
    /// Catégorie à partir de la classe LMU (« Hyper », « LMP2 »…) ou de l'identifiant de voiture
    /// Assetto Corsa (« ks_ferrari_488_gt3 », « ks_porsche_919_hybrid_2016 »…).
    /// </summary>
    public static CarKind Classify(string cls)
    {
        var c = cls.ToUpperInvariant();
        static bool Any(string text, params string[] keys) => keys.Any(text.Contains);
        if (Any(c, "FORMULA", "OPEN WHEEL", "OPENWHEEL", "INDY", "SUPER FORMULA", "F4 ", "FR3.5"))
            return CarKind.Formula;
        if (Any(c, "HYPER", "LMH", "LMDH", "LMP1", "919", "TS050", "R18", "499P", "963", "9X8", "GR010", "V-SERIES", "VSERIES"))
            return CarKind.Hypercar;
        if (Any(c, "LMP2", "ORECA")) return CarKind.Lmp2;
        if (Any(c, "LMP3", "JS_P3", "JS P3")) return CarKind.Lmp3;
        if (Any(c, "GTE", "GTLM", "GT2")) return CarKind.Gte;
        if (Any(c, "GT3", "GTM", "GT4", "GT")) return CarKind.Gt3;
        return CarKind.Other;
    }
}
