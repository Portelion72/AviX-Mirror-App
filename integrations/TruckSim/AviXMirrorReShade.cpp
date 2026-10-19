// AviX Mirror — add-on ReShade pour Euro Truck Simulator 2 / American Truck Simulator (solo).
// © AVIX_3D (avix3d.com) — tous droits réservés. API d'add-ons ReShade © Patrick Mours (licence BSD-3).
//
// Le jeu calcule l'image de ses rétroviseurs dans des textures de la carte graphique (« render targets »)
// avant de les afficher dans la cabine. Cet add-on, chargé par ReShade (version « add-ons »), repère ces
// textures, les recopie et les publie dans la mémoire partagée « Local\AviXMirror.ReShade.v1 » :
//   - des vignettes de toutes les textures candidates (pour choisir celle du rétro dans AviX Mirror),
//   - l'image complète des textures choisies (une ou deux : rétros gauche et droit), à chaque image du jeu.
// Rien n'est affiché ni modifié dans le jeu. Solo uniquement (pas TruckersMP).

#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include "reshade/reshade.hpp"

using namespace reshade::api;

extern "C" __declspec(dllexport) const char* NAME = "AviX Mirror";
extern "C" __declspec(dllexport) const char* DESCRIPTION =
    "Envoie l'image des rétroviseurs d'ETS2 / ATS à AviX Mirror (écran VoCore). Solo uniquement.";

// ---------- Mémoire partagée (disposition fixe, lue par AviX Mirror : Capture/ReShadeSource.cs) ----------

constexpr uint32_t Magic = 0x52585641; // 'AVXR'
constexpr uint32_t MaxCandidates = 32;
constexpr uint32_t ThumbW = 160, ThumbH = 100;
constexpr uint32_t MaxImage = 2048;
constexpr size_t CandidatesOffset = 256;
constexpr size_t ThumbsOffset = CandidatesOffset + MaxCandidates * 32;
constexpr size_t ImageOffset = 2u * 1024 * 1024;
constexpr size_t ImageSize = size_t(MaxImage) * MaxImage * 4;
constexpr size_t MappingSize = ImageOffset + 2 * ImageSize;

#pragma pack(push, 4)
struct Slot
{
    uint64_t selected;       // +0  écrit par AviX Mirror : texture choisie (0 = aucune)
    uint32_t imageSeq;       // +8  verrou de séquence : impair pendant l'écriture
    uint32_t imageWidth;     // +12
    uint32_t imageHeight;    // +16
    uint32_t imageFormat;    // +20 format d'origine (information)
};
struct Header
{
    uint32_t magic;          //  0
    uint32_t version;        //  4 = 2
    uint32_t frame;          //  8 images présentées
    uint32_t candidateCount; // 12
    uint32_t thumbsWanted;   // 16 écrit par AviX Mirror : 1 = produire les vignettes
    uint32_t thumbSeq;       // 20 incrémenté à chaque mise à jour des vignettes
    Slot slots[2];           // 24 et 48 : image 1 (rétro gauche) et image 2 (rétro droit)
};
struct Candidate
{
    uint64_t handle;         //  0
    uint32_t width;          //  8
    uint32_t height;         // 12
    uint32_t format;         // 16
    uint32_t samples;        // 20
    uint32_t binds;          // 24 nombre de rendus dans la dernière image
    uint32_t order;          // 28 ordre d'apparition (stable d'une partie à l'autre)
};
#pragma pack(pop)
static_assert(sizeof(Slot) == 24, "Slot");
static_assert(sizeof(Header) == 72, "Header");
static_assert(sizeof(Candidate) == 32, "Candidate");

static HANDLE g_mapping = nullptr;
static uint8_t* g_view = nullptr;

static Header* Shared() { return reinterpret_cast<Header*>(g_view); }

static void OpenShared()
{
    if (g_view)
        return;
    g_mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE,
        static_cast<DWORD>(uint64_t(MappingSize) >> 32), static_cast<DWORD>(MappingSize & 0xFFFFFFFF), L"Local\\AviXMirror.ReShade.v1");
    if (!g_mapping)
        return;
    g_view = static_cast<uint8_t*>(MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, MappingSize));
    if (!g_view)
    {
        CloseHandle(g_mapping);
        g_mapping = nullptr;
        return;
    }
    auto* h = Shared();
    std::memset(h, 0, sizeof(Header));
    h->magic = Magic;
    h->version = 2;
}

static void CloseShared()
{
    if (g_view)
        UnmapViewOfFile(g_view);
    if (g_mapping)
        CloseHandle(g_mapping);
    g_view = nullptr;
    g_mapping = nullptr;
}

// ---------- Textures candidates ----------

struct Tracked
{
    resource_desc desc;
    uint32_t order = 0;
    uint32_t bindsThisFrame = 0;
    uint32_t bindsLastFrame = 0;
    uint32_t lastFrame = 0;
    resource staging[2] = { {0}, {0} };   // copies lisibles par le processeur (double tampon)
    resource resolved = { 0 };            // texture intermédiaire pour l'antialiasing (MSAA)
    bool stagingFilled[2] = { false, false };
};

static std::unordered_map<uint64_t, Tracked> g_tracked;
static uint32_t g_nextOrder = 0;
static uint32_t g_frame = 0;
static uint32_t g_flip[2] = { 0, 0 };

static bool SupportedFormat(format f)
{
    switch (format_to_typeless(f))
    {
    case format::r8g8b8a8_typeless:
    case format::b8g8r8a8_typeless:
    case format::b8g8r8x8_typeless:
    case format::r10g10b10a2_typeless:
    case format::r16g16b16a16_typeless:
        return true;
    default:
        return f == format::r11g11b10_float;
    }
}

static void FreeTracked(device* dev, Tracked& t)
{
    for (auto& s : t.staging)
        if (s.handle != 0)
        {
            dev->destroy_resource(s);
            s = { 0 };
        }
    if (t.resolved.handle != 0)
    {
        dev->destroy_resource(t.resolved);
        t.resolved = { 0 };
    }
    t.stagingFilled[0] = t.stagingFilled[1] = false;
}

static void OnBindRenderTargets(command_list* cmd, uint32_t count, const resource_view* rtvs, resource_view)
{
    if (count == 0 || rtvs == nullptr)
        return;
    device* dev = cmd->get_device();
    for (uint32_t i = 0; i < count; i++)
    {
        if (rtvs[i].handle == 0)
            continue;
        resource res = dev->get_resource_from_view(rtvs[i]);
        if (res.handle == 0)
            continue;
        auto it = g_tracked.find(res.handle);
        if (it == g_tracked.end())
        {
            resource_desc desc = dev->get_resource_desc(res);
            if (desc.type != resource_type::texture_2d || desc.texture.width < 64 || desc.texture.height < 64 ||
                desc.texture.width > MaxImage || desc.texture.height > MaxImage || desc.texture.depth_or_layers != 1 ||
                !SupportedFormat(desc.texture.format))
                continue;
            Tracked t;
            t.desc = desc;
            t.order = g_nextOrder++;
            it = g_tracked.emplace(res.handle, t).first;
        }
        it->second.bindsThisFrame++;
        it->second.lastFrame = g_frame;
    }
}

static void OnDestroyResource(device* dev, resource res)
{
    auto it = g_tracked.find(res.handle);
    if (it == g_tracked.end())
        return;
    FreeTracked(dev, it->second);
    g_tracked.erase(it);
}

// ---------- Conversion des pixels en BGRA 8 bits ----------

static float HalfToFloat(uint16_t h)
{
    uint32_t sign = (h >> 15) & 1, exp = (h >> 10) & 0x1F, mant = h & 0x3FF;
    float v;
    if (exp == 0)
        v = std::ldexp(static_cast<float>(mant), -24);
    else if (exp == 31)
        v = 65504.0f;
    else
        v = std::ldexp(static_cast<float>(mant | 0x400), static_cast<int>(exp) - 25);
    return sign ? -v : v;
}

static float Small(uint32_t bits, uint32_t mantBits)
{
    // Flottant non signé à 5 bits d'exposant (formats 11 et 10 bits).
    uint32_t exp = bits >> mantBits, mant = bits & ((1u << mantBits) - 1);
    if (exp == 0)
        return std::ldexp(static_cast<float>(mant), -14 - static_cast<int>(mantBits));
    if (exp == 31)
        return 65504.0f;
    return std::ldexp(static_cast<float>(mant | (1u << mantBits)), static_cast<int>(exp) - 15 - static_cast<int>(mantBits));
}

static uint8_t ToneMap(float linear)
{
    // Image HDR (linéaire) : exposition douce puis courbe sRGB.
    float c = 1.0f - std::exp(-std::max(0.0f, linear) * 1.2f);
    float s = c <= 0.0031308f ? c * 12.92f : 1.055f * std::pow(c, 1.0f / 2.4f) - 0.055f;
    return static_cast<uint8_t>(std::clamp(s * 255.0f + 0.5f, 0.0f, 255.0f));
}

// Lit le pixel x d'une ligne au format donné, en B, G, R.
static void Pixel(const uint8_t* row, uint32_t x, format f, uint8_t& b, uint8_t& g, uint8_t& r)
{
    switch (format_to_typeless(f))
    {
    case format::r8g8b8a8_typeless:
        r = row[x * 4]; g = row[x * 4 + 1]; b = row[x * 4 + 2];
        return;
    case format::b8g8r8a8_typeless:
    case format::b8g8r8x8_typeless:
        b = row[x * 4]; g = row[x * 4 + 1]; r = row[x * 4 + 2];
        return;
    case format::r10g10b10a2_typeless:
    {
        uint32_t v; std::memcpy(&v, row + x * 4, 4);
        r = static_cast<uint8_t>((v & 0x3FF) >> 2); g = static_cast<uint8_t>(((v >> 10) & 0x3FF) >> 2); b = static_cast<uint8_t>(((v >> 20) & 0x3FF) >> 2);
        return;
    }
    case format::r16g16b16a16_typeless:
    {
        uint16_t c[4]; std::memcpy(c, row + x * 8, 8);
        r = ToneMap(HalfToFloat(c[0])); g = ToneMap(HalfToFloat(c[1])); b = ToneMap(HalfToFloat(c[2]));
        return;
    }
    default: // r11g11b10_float
    {
        uint32_t v; std::memcpy(&v, row + x * 4, 4);
        r = ToneMap(Small(v & 0x7FF, 6)); g = ToneMap(Small((v >> 11) & 0x7FF, 6)); b = ToneMap(Small((v >> 22) & 0x3FF, 5));
        return;
    }
    }
}

// ---------- Copie GPU -> processeur ----------

static bool EnsureStaging(device* dev, Tracked& t, int slot)
{
    if (t.staging[slot].handle != 0)
        return true;
    const auto& d = t.desc.texture;
    resource_desc desc(d.width, d.height, 1, 1, format_to_typeless(d.format), 1, memory_heap::gpu_to_cpu, resource_usage::copy_dest);
    if (!dev->create_resource(desc, nullptr, resource_usage::copy_dest, &t.staging[slot]))
    {
        t.staging[slot] = { 0 };
        return false;
    }
    return true;
}

// Copie la texture dans la copie lisible « slot » (avec résolution de l'antialiasing si besoin).
static bool CopyToStaging(command_list* cmd, device* dev, resource res, Tracked& t, int slot)
{
    if (!EnsureStaging(dev, t, slot))
        return false;
    resource source = res;
    if (t.desc.texture.samples > 1)
    {
        if (t.resolved.handle == 0)
        {
            const auto& d = t.desc.texture;
            resource_desc desc(d.width, d.height, 1, 1, d.format, 1, memory_heap::gpu_only, resource_usage::resolve_dest | resource_usage::copy_source);
            if (!dev->create_resource(desc, nullptr, resource_usage::resolve_dest, &t.resolved))
            {
                t.resolved = { 0 };
                return false;
            }
        }
        cmd->resolve_texture_region(res, 0, nullptr, t.resolved, 0, 0, 0, 0, t.desc.texture.format);
        source = t.resolved;
    }
    cmd->copy_texture_region(source, 0, nullptr, t.staging[slot], 0, nullptr);
    t.stagingFilled[slot] = true;
    return true;
}

// Lit une copie et l'écrit en BGRA (destination w x h, échantillonnage au plus proche).
static bool ReadStaging(device* dev, Tracked& t, int slot, uint8_t* dest, uint32_t w, uint32_t h, uint32_t destPitch)
{
    if (!t.stagingFilled[slot])
        return false;
    subresource_data data = {};
    if (!dev->map_texture_region(t.staging[slot], 0, nullptr, map_access::read_only, &data) || data.data == nullptr)
        return false;
    const uint32_t sw = t.desc.texture.width, sh = t.desc.texture.height;
    const auto* src = static_cast<const uint8_t*>(data.data);
    for (uint32_t y = 0; y < h; y++)
    {
        const uint8_t* row = src + size_t(y * sh / h) * data.row_pitch;
        uint8_t* out = dest + size_t(y) * destPitch;
        for (uint32_t x = 0; x < w; x++)
        {
            uint8_t b, g, r;
            Pixel(row, x * sw / w, t.desc.texture.format, b, g, r);
            out[x * 4] = b; out[x * 4 + 1] = g; out[x * 4 + 2] = r; out[x * 4 + 3] = 255;
        }
    }
    dev->unmap_texture_region(t.staging[slot], 0);
    return true;
}

// ---------- À chaque image présentée ----------

static void OnPresent(command_queue* queue, swapchain* swap, const rect*, const rect*, uint32_t, const rect*)
{
    OpenShared();
    if (!g_view)
        return;
    device* dev = queue->get_device();
    command_list* cmd = queue->get_immediate_command_list();
    auto* h = Shared();
    g_frame++;
    h->frame = g_frame;

    const resource backBuffer = swap->get_current_back_buffer();

    // Liste des candidates : textures rendues récemment, dans leur ordre d'apparition.
    std::vector<std::pair<uint64_t, Tracked*>> list;
    for (auto& [handle, t] : g_tracked)
    {
        t.bindsLastFrame = t.bindsThisFrame;
        t.bindsThisFrame = 0;
        if (handle != backBuffer.handle && g_frame - t.lastFrame < 120)
            list.emplace_back(handle, &t);
    }
    std::sort(list.begin(), list.end(), [](const auto& a, const auto& b) { return a.second->order < b.second->order; });
    if (list.size() > MaxCandidates)
        list.resize(MaxCandidates);

    auto* candidates = reinterpret_cast<Candidate*>(g_view + CandidatesOffset);
    for (size_t i = 0; i < list.size(); i++)
    {
        const auto& t = *list[i].second;
        candidates[i] = { list[i].first, t.desc.texture.width, t.desc.texture.height,
            static_cast<uint32_t>(t.desc.texture.format), t.desc.texture.samples, t.bindsLastFrame, t.order };
    }
    h->candidateCount = static_cast<uint32_t>(list.size());

    // Vignettes (seulement pendant le choix dans AviX Mirror, deux fois par seconde environ).
    if (h->thumbsWanted && g_frame % 30 == 0)
    {
        for (size_t i = 0; i < list.size(); i++)
            CopyToStaging(cmd, dev, resource{ list[i].first }, *list[i].second, 0);
        queue->flush_immediate_command_list();
        for (size_t i = 0; i < list.size(); i++)
            ReadStaging(dev, *list[i].second, 0, g_view + ThumbsOffset + i * ThumbW * ThumbH * 4, ThumbW, ThumbH, ThumbW * 4);
        h->thumbSeq++;
    }

    // Image complète des textures choisies : copie de cette image, lecture de la précédente (pas d'attente du GPU).
    for (int k = 0; k < 2; k++)
    {
        Slot& slot = h->slots[k];
        uint64_t selected = slot.selected;
        auto it = selected ? g_tracked.find(selected) : g_tracked.end();
        if (it == g_tracked.end())
            continue;
        Tracked& t = it->second;
        int write = static_cast<int>(g_flip[k] & 1), read = 1 - write;
        CopyToStaging(cmd, dev, resource{ selected }, t, write);
        const uint32_t w = t.desc.texture.width, hgt = t.desc.texture.height;
        if (t.stagingFilled[read])
        {
            slot.imageSeq++; // impair : écriture en cours
            MemoryBarrier();
            slot.imageWidth = w;
            slot.imageHeight = hgt;
            slot.imageFormat = static_cast<uint32_t>(t.desc.texture.format);
            ReadStaging(dev, t, read, g_view + ImageOffset + k * ImageSize, w, hgt, w * 4);
            MemoryBarrier();
            slot.imageSeq++; // pair : image complète
        }
        g_flip[k]++;
    }
}

static void OnDestroyDevice(device* dev)
{
    for (auto& [handle, t] : g_tracked)
        FreeTracked(dev, t);
    g_tracked.clear();
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
    switch (reason)
    {
    case DLL_PROCESS_ATTACH:
        if (!reshade::register_addon(module))
            return FALSE;
        reshade::register_event<reshade::addon_event::bind_render_targets_and_depth_stencil>(OnBindRenderTargets);
        reshade::register_event<reshade::addon_event::destroy_resource>(OnDestroyResource);
        reshade::register_event<reshade::addon_event::destroy_device>(OnDestroyDevice);
        reshade::register_event<reshade::addon_event::present>(OnPresent);
        break;
    case DLL_PROCESS_DETACH:
        reshade::unregister_addon(module);
        CloseShared();
        break;
    }
    return TRUE;
}
