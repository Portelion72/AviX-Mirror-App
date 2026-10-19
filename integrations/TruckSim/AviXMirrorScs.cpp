// AviX Mirror — plugin de télémétrie pour Euro Truck Simulator 2 et American Truck Simulator.
// © AVIX_3D (avix3d.com) — tous droits réservés.
//
// Plugin officiel du SDK de télémétrie SCS (aucune injection, aucune lecture de la mémoire du jeu) :
// le jeu charge la DLL depuis « bin\win_x64\plugins » et lui envoie la télémétrie de VOTRE camion
// (le SDK ne donne pas la position du trafic). Elle est recopiée dans la mémoire partagée
// « Local\AviXMirror.SCS.v1 », lue par AviX Mirror (pause, clignotants, vitesse, position).
//
// Seuls les types du SDK utilisés sont redéclarés ici (mêmes tailles et dispositions que
// scssdk*.h, SDK de télémétrie 1.x).

#include <cstdint>
#include <cstring>
#include <cmath>

#ifdef _WIN32
#include <windows.h>
#define AVIX_EXPORT extern "C" __declspec(dllexport)
#define SCSAPIFUNC __stdcall
#else
#define AVIX_EXPORT extern "C"
#define SCSAPIFUNC
#endif

// ---------- Types du SDK SCS (redéclarés) ----------

typedef uint32_t scs_u32_t;
typedef int32_t scs_result_t;
typedef const char* scs_string_t;
typedef void* scs_context_t;
typedef scs_u32_t scs_event_t;
typedef scs_u32_t scs_value_type_t;

static const scs_result_t SCS_RESULT_ok = 0;
static const scs_result_t SCS_RESULT_unsupported = -1;
static const scs_u32_t SCS_U32_NIL = 0xFFFFFFFFu;

static const scs_event_t SCS_TELEMETRY_EVENT_frame_start = 1;
static const scs_event_t SCS_TELEMETRY_EVENT_paused = 3;
static const scs_event_t SCS_TELEMETRY_EVENT_started = 4;

static const scs_value_type_t SCS_VALUE_TYPE_bool = 1;
static const scs_value_type_t SCS_VALUE_TYPE_float = 5;
static const scs_value_type_t SCS_VALUE_TYPE_fvector = 7;
static const scs_value_type_t SCS_VALUE_TYPE_dplacement = 11;

static const scs_u32_t SCS_TELEMETRY_CHANNEL_FLAG_none = 0;

struct scs_value_fvector_t { float x, y, z; };
struct scs_value_dvector_t { double x, y, z; };
struct scs_value_euler_t { float heading, pitch, roll; };
struct scs_value_dplacement_t { scs_value_dvector_t position; scs_value_euler_t orientation; scs_u32_t _padding; };

struct scs_value_t
{
    scs_value_type_t type;
    scs_u32_t _padding;
    union
    {
        uint8_t value_bool;
        float value_float;
        scs_value_fvector_t value_fvector;
        scs_value_dplacement_t value_dplacement;
    };
};
static_assert(sizeof(scs_value_dplacement_t) == 40, "dplacement");
static_assert(sizeof(scs_value_t) == 48, "scs_value_t");

struct scs_telemetry_frame_start_t
{
    scs_u32_t flags;
    scs_u32_t _padding;
    uint64_t render_time;
    uint64_t simulation_time;
    uint64_t paused_simulation_time;
};
static_assert(sizeof(scs_telemetry_frame_start_t) == 32, "frame_start");

typedef void (SCSAPIFUNC* scs_log_t)(int32_t type, scs_string_t message);
typedef void (SCSAPIFUNC* scs_telemetry_event_callback_t)(scs_event_t event, const void* event_info, scs_context_t context);
typedef scs_result_t (SCSAPIFUNC* scs_telemetry_register_for_event_t)(scs_event_t event, scs_telemetry_event_callback_t callback, scs_context_t context);
typedef scs_result_t (SCSAPIFUNC* scs_telemetry_unregister_from_event_t)(scs_event_t event);
typedef void (SCSAPIFUNC* scs_telemetry_channel_callback_t)(scs_string_t name, scs_u32_t index, const scs_value_t* value, scs_context_t context);
typedef scs_result_t (SCSAPIFUNC* scs_telemetry_register_for_channel_t)(scs_string_t name, scs_u32_t index, scs_value_type_t type, scs_u32_t flags, scs_telemetry_channel_callback_t callback, scs_context_t context);
typedef scs_result_t (SCSAPIFUNC* scs_telemetry_unregister_from_channel_t)(scs_string_t name, scs_u32_t index, scs_value_type_t type);

struct scs_telemetry_init_params_v100_t
{
    // scs_sdk_init_params_v100_t
    scs_string_t game_name;
    scs_string_t game_id;
    scs_u32_t game_version;
#if defined(_WIN64) || defined(__x86_64__)
    scs_u32_t _padding;
#endif
    scs_log_t log;
    // Télémétrie
    scs_telemetry_register_for_event_t register_for_event;
    scs_telemetry_unregister_from_event_t unregister_from_event;
    scs_telemetry_register_for_channel_t register_for_channel;
    scs_telemetry_unregister_from_channel_t unregister_from_channel;
};

// ---------- Mémoire partagée lue par AviX Mirror (disposition fixe, 88 octets) ----------

struct AviXScsShared
{
    uint32_t magic;          //  0 'AVXS'
    uint32_t version;        //  4 1
    uint32_t frame;          //  8 compteur d'images, arrêté pendant la pause
    uint32_t paused;         // 12 1 = jeu en pause (menus, carte…)
    double x, y, z;          // 16 position du camion (m, monde : x = est, y = haut, z = sud)
    float heading;           // 40 cap (0..1 = 0..360°, sens trigonométrique vu de dessus, 0 = nord)
    float pitch;             // 44 tangage (0..1 = 0..360°)
    float roll;              // 48 roulis
    float speed;             // 52 vitesse (m/s, négative en marche arrière)
    float vx, vy, vz;        // 56 vitesse dans le repère du camion (m/s)
    uint32_t leftBlinker;    // 68
    uint32_t rightBlinker;   // 72
    uint32_t game;           // 76 1 = ETS2, 2 = ATS
    uint64_t simulationTime; // 80 µs, s'arrête en pause
};
static_assert(sizeof(AviXScsShared) == 88, "AviXScsShared");

static AviXScsShared* g_shared = nullptr;
static AviXScsShared g_fallback; // si la mémoire partagée n'a pas pu être créée
#ifdef _WIN32
static HANDLE g_mapping = nullptr;
#endif
static scs_telemetry_unregister_from_event_t g_unregisterEvent = nullptr;
static scs_telemetry_unregister_from_channel_t g_unregisterChannel = nullptr;

static AviXScsShared* OpenShared()
{
#ifdef _WIN32
    g_mapping = CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(AviXScsShared), "Local\\AviXMirror.SCS.v1");
    if (g_mapping)
    {
        void* view = MapViewOfFile(g_mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AviXScsShared));
        if (view)
            return static_cast<AviXScsShared*>(view);
        CloseHandle(g_mapping);
        g_mapping = nullptr;
    }
#endif
    return &g_fallback;
}

static void CloseShared()
{
#ifdef _WIN32
    if (g_shared && g_shared != &g_fallback)
        UnmapViewOfFile(g_shared);
    if (g_mapping)
        CloseHandle(g_mapping);
    g_mapping = nullptr;
#endif
    g_shared = nullptr;
}

// ---------- Rappels du jeu ----------

static void SCSAPIFUNC OnEvent(scs_event_t event, const void* info, scs_context_t)
{
    if (!g_shared)
        return;
    if (event == SCS_TELEMETRY_EVENT_paused)
        g_shared->paused = 1;
    else if (event == SCS_TELEMETRY_EVENT_started)
        g_shared->paused = 0;
    else if (event == SCS_TELEMETRY_EVENT_frame_start && info)
    {
        const auto* frame = static_cast<const scs_telemetry_frame_start_t*>(info);
        g_shared->simulationTime = frame->paused_simulation_time;
        if (!g_shared->paused)
            g_shared->frame++;
    }
}

static void SCSAPIFUNC OnPlacement(scs_string_t, scs_u32_t, const scs_value_t* value, scs_context_t)
{
    if (!g_shared || !value || value->type != SCS_VALUE_TYPE_dplacement)
        return;
    const auto& p = value->value_dplacement;
    g_shared->x = p.position.x;
    g_shared->y = p.position.y;
    g_shared->z = p.position.z;
    g_shared->heading = p.orientation.heading;
    g_shared->pitch = p.orientation.pitch;
    g_shared->roll = p.orientation.roll;
}

static void SCSAPIFUNC OnSpeed(scs_string_t, scs_u32_t, const scs_value_t* value, scs_context_t)
{
    if (g_shared && value && value->type == SCS_VALUE_TYPE_float)
        g_shared->speed = value->value_float;
}

static void SCSAPIFUNC OnVelocity(scs_string_t, scs_u32_t, const scs_value_t* value, scs_context_t)
{
    if (!g_shared || !value || value->type != SCS_VALUE_TYPE_fvector)
        return;
    g_shared->vx = value->value_fvector.x;
    g_shared->vy = value->value_fvector.y;
    g_shared->vz = value->value_fvector.z;
}

static void SCSAPIFUNC OnLeftBlinker(scs_string_t, scs_u32_t, const scs_value_t* value, scs_context_t)
{
    if (g_shared && value && value->type == SCS_VALUE_TYPE_bool)
        g_shared->leftBlinker = value->value_bool ? 1 : 0;
}

static void SCSAPIFUNC OnRightBlinker(scs_string_t, scs_u32_t, const scs_value_t* value, scs_context_t)
{
    if (g_shared && value && value->type == SCS_VALUE_TYPE_bool)
        g_shared->rightBlinker = value->value_bool ? 1 : 0;
}

// ---------- Points d'entrée du SDK ----------

AVIX_EXPORT scs_result_t SCSAPIFUNC scs_telemetry_init(const scs_u32_t version, const void* const params)
{
    // Télémétrie 1.x (1.00 et 1.01) : même structure d'initialisation.
    if ((version >> 16) != 1 || !params)
        return SCS_RESULT_unsupported;
    const auto* init = static_cast<const scs_telemetry_init_params_v100_t*>(params);

    g_shared = OpenShared();
    std::memset(g_shared, 0, sizeof(AviXScsShared));
    g_shared->magic = 0x53585641; // 'AVXS'
    g_shared->version = 1;
    g_shared->paused = 1; // le jeu démarre en pause (menus)
    g_shared->game = init->game_id && std::strcmp(init->game_id, "ats") == 0 ? 2 : 1;

    g_unregisterEvent = init->unregister_from_event;
    g_unregisterChannel = init->unregister_from_channel;
    init->register_for_event(SCS_TELEMETRY_EVENT_frame_start, OnEvent, nullptr);
    init->register_for_event(SCS_TELEMETRY_EVENT_paused, OnEvent, nullptr);
    init->register_for_event(SCS_TELEMETRY_EVENT_started, OnEvent, nullptr);

    init->register_for_channel("truck.world.placement", SCS_U32_NIL, SCS_VALUE_TYPE_dplacement, SCS_TELEMETRY_CHANNEL_FLAG_none, OnPlacement, nullptr);
    init->register_for_channel("truck.speed", SCS_U32_NIL, SCS_VALUE_TYPE_float, SCS_TELEMETRY_CHANNEL_FLAG_none, OnSpeed, nullptr);
    init->register_for_channel("truck.local.velocity.linear", SCS_U32_NIL, SCS_VALUE_TYPE_fvector, SCS_TELEMETRY_CHANNEL_FLAG_none, OnVelocity, nullptr);
    init->register_for_channel("truck.lblinker", SCS_U32_NIL, SCS_VALUE_TYPE_bool, SCS_TELEMETRY_CHANNEL_FLAG_none, OnLeftBlinker, nullptr);
    init->register_for_channel("truck.rblinker", SCS_U32_NIL, SCS_VALUE_TYPE_bool, SCS_TELEMETRY_CHANNEL_FLAG_none, OnRightBlinker, nullptr);

    if (init->log)
        init->log(0, "[AviX Mirror] plugin de télémétrie chargé");
    return SCS_RESULT_ok;
}

AVIX_EXPORT void SCSAPIFUNC scs_telemetry_shutdown(void)
{
    // Le jeu retire lui-même les enregistrements à l'arrêt ; on libère la mémoire partagée.
    g_unregisterEvent = nullptr;
    g_unregisterChannel = nullptr;
    CloseShared();
}

#ifdef _WIN32
BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH)
        CloseShared();
    return TRUE;
}
#endif
