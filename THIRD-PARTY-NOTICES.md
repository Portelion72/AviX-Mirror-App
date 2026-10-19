# Composants tiers / Third-party notices

AviX Mirror est un logiciel propriétaire (voir `LICENSE`). Il utilise les composants suivants,
chacun soumis à sa propre licence, qui s'applique à ce seul composant.

| Composant | Usage | Licence |
|-----------|-------|---------|
| [libusb](https://libusb.info) 1.0.27 (`libusb-1.0.dll`, binaire officiel non modifié) | Communication USB avec l'écran VoCore | [GNU LGPL 2.1](https://github.com/libusb/libusb/blob/master/COPYING) — bibliothèque séparée, remplaçable par l'utilisateur |
| [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) (Vortice.Direct3D11) | Direct3D 11 (capture, caméra Assetto Corsa) | MIT |
| [.NET](https://github.com/dotnet/runtime) et Windows Forms | Environnement d'exécution | MIT |
| [CsWinRT](https://github.com/microsoft/CsWinRT) / Windows SDK projections | Windows.Graphics.Capture | MIT |

## ReShade — API d'add-ons (en-têtes)

L'add-on ETS2 / ATS (`AviXMirror.addon64`) est compilé avec les en-têtes de l'API d'add-ons de ReShade
(`integrations/TruckSim/reshade/`, version 6.3.3), © 2014 Patrick Mours, licence BSD à 3 clauses
(texte complet : `integrations/TruckSim/reshade/LICENSE-ReShade.md`). ReShade lui-même n'est pas fourni :
l'utilisateur l'installe depuis https://reshade.me.

