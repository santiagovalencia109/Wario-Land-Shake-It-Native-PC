// Wario Land: Shake It! full 16:9 ([video] full_widescreen in Config.toml, 16:9 only).
//
// The game's own widescreen mode draws levels with a 16:9 stage camera (+-5.33 units) but clips
// them to a 4:3 window and covers the sides with a frame layout, and everything that decides what
// the level shows still works in the 4:3 +-4 units. Full 16:9 changes, per run:
//   1. the stage camera's scissor (80, 0, 480 x 480 in the 640x480 anamorphic EFB) opens to the
//      whole EFB (gx_transform.cpp, GXSetScissor from the camera at 0x80369C04);
//   2. the frame layout's panes (WideWindowL/R, PIC_wpaper*, P_InGameRoot with the treasure tabs)
//      and the seven mission-icon layouts are hidden right before each layout draw
//      (gx_texture.cpp WarioWideLayoutDraw, called from gx_pixel.cpp);
//   3. WarioWideApplyGlobals (below, same moment) widens by 4/3 the stage object culling factor
//      and the view-rect widths the camera uses to stop at a room's edges;
//   4. the screen-edge queries fn_800CCC60 / fn_800CCCE8 answer +-16/3 x zoom (below);
//   5. the "is it on screen" test fn_80090424 (objects appearing and disappearing, e.g. the submarine's
//      torpedoes and enemies) stops narrowing the widescreen projection back to 4:3 (factor 1.33 at
//      0x80620C9C, read only there, set to 1.0);
//   6. submarine levels (CamGameSub, BgSubmarineStopper): the sub is kept on screen by two invisible
//      walls that fn_802896A4 places once, from the screen width at 0x80628BB4, at +-(width + 2) / 2 - 0.25
//      from the camera (results cached at 0x8061F070 / 0x8061F078, guards 0x8061F074 / 0x8061F07C). That
//      width is widened with the others and the guards are cleared so the walls follow it. Without this
//      the camera, which stops 4/3 earlier at the end of a section, left the exit out of the sub's reach.
// WARIO_HIDE_PANES in the environment turns it on as well (debugging).
#include "gx_internal.h"
#include "hle_stubs.h"
#include "runtime_config.h"
#include "display_settings.h"

#include <SDL3/SDL_keyboard.h>

#include <cstdlib>

// WARIO (diagnostico, WARIO_WIDE_DEBUG en el entorno): apagar en vivo cada parte del 16:9 completo para
// encontrar la que bloquea el final de los niveles del submarino. F5 = ampliacion de la zona de objetos
// (culling), F6 = anchos de vista de la camara, F7 = bordes de pantalla (fn_800CCC60/E8) y prueba "en pantalla" (fn_80090424), F8 = registrar el
// estado (camara, valores). Cada cambio queda en el registro como [wide].
static bool g_wideCull = true, g_wideView = true, g_wideEdge = true;
static void WideDebugKeys() {
    static const bool on = std::getenv("WARIO_WIDE_DEBUG") != nullptr;
    if (!on) return;
    static bool was[4] = {};
    int n = 0;
    const bool* ks = SDL_GetKeyboardState(&n);
    if (ks == nullptr) return;
    const SDL_Scancode keys[4] = {SDL_SCANCODE_F5, SDL_SCANCODE_F6, SDL_SCANCODE_F7, SDL_SCANCODE_F8};
    for (int i = 0; i < 4; ++i) {
        const bool down = keys[i] < n && ks[keys[i]];
        const bool pressed = down && !was[i];
        was[i] = down;
        if (!pressed) continue;
        if (i == 0) g_wideCull = !g_wideCull;
        if (i == 1) g_wideView = !g_wideView;
        if (i == 2) g_wideEdge = !g_wideEdge;
        const uint32_t manager = Memory::Read32(0x8061DB68u);
        const uint32_t camera = (manager != 0 && Memory::Contains(manager + 0xA8u, 0x60)) ? Memory::Read32(manager + 0xA8u) : 0u;
        const bool camOk = camera != 0 && Memory::Contains(camera + 0x38u, 0x20);
        RT_LOGF(RT_TAG_GX, "[wide] F%d: culling %s, vista %s, bordes %s | camara x %.3f y %.3f zoom %.3f | cull %.3f vistas %.3f %.3f %.3f %.3f\n",
                5 + i, g_wideCull ? "16:9" : "4:3", g_wideView ? "16:9" : "4:3", g_wideEdge ? "16:9" : "4:3",
                camOk ? Memory::ReadFloat32(camera + 0x38u) : 0.f, camOk ? Memory::ReadFloat32(camera + 0x3Cu) : 0.f,
                camOk ? Memory::ReadFloat32(camera + 0x54u) : 0.f, Memory::ReadFloat32(0x8061AA90u),
                Memory::ReadFloat32(0x80628BACu), Memory::ReadFloat32(0x806282DCu), Memory::ReadFloat32(0x80627D2Cu),
                Memory::ReadFloat32(0x8062890Cu));
    }
}

bool WarioFullWide() {
    static const bool configured =
        RuntimeConfigFile::FullWidescreen(false) || std::getenv("WARIO_HIDE_PANES") != nullptr;
    // GameWidescreen (what SCGetAspectRatio reports) is asked every time rather than cached, so a
    // call before the display settings are in place can never pin the wrong answer.
    return configured && DisplaySettings::GameWidescreen();
}

// Called right before each layout draw (gx_pixel.cpp), so it also runs after the static
// constructors that fill the view-rect globals.
void WarioWideOnScreenFactor(bool wide);

void WarioWideApplyGlobals() {
    if (!WarioFullWide()) return;
    WideDebugKeys();
    WarioWideOnScreenFactor(g_wideEdge);
    // fn_80051E28 culls the stage's objects against +-4 x +-3 units times this factor (1.0, read
    // only there).
    constexpr uint32_t kStageCullScale = 0x8061AA90u;
    const float cullWant = g_wideCull ? 4.0f / 3.0f : 1.0f;
    if (Memory::ReadFloat32(kStageCullScale) != cullWant) Memory::WriteFloat32(kStageCullScale, cullWant);
    // "Screen width" globals (k x 4.0, set by static constructors) that the camera code passes to
    // fn_8004C654 to find where it must stop at a room's edges: fn_802864F8 & co. (0x80628BAC),
    // fn_8011D8C8 (0x806282DC), fn_8004E7E4 (0x80627D2C), fn_80215E68 (0x8062890C). The 4:3 value
    // is remembered on first sight so the write never compounds.
    // 0x80628BB4: the submarine's screen walls (fn_802896A4, see 6. above).
    static const uint32_t kViewWidths[] = {0x80628BACu, 0x806282DCu, 0x80627D2Cu, 0x8062890Cu, 0x80628BB4u};
    static float baseWidth[5] = {0.f, 0.f, 0.f, 0.f, 0.f};
    for (int i = 0; i < 5; ++i) {
        const float cur = Memory::ReadFloat32(kViewWidths[i]);
        if (baseWidth[i] == 0.f && cur > 0.f) baseWidth[i] = cur;
        if (baseWidth[i] <= 0.f) continue;
        const float want = g_wideView ? baseWidth[i] * 4.0f / 3.0f : baseWidth[i];
        if ((cur == baseWidth[i] || cur == baseWidth[i] * 4.0f / 3.0f) && cur != want) {
            Memory::WriteFloat32(kViewWidths[i], want);
            if (kViewWidths[i] == 0x80628BB4u) {  // the cached wall offsets are recomputed on next use
                Memory::Write8(0x8061F074u, 0);
                Memory::Write8(0x8061F07Cu, 0);
            }
        }
    }
}

// The on-screen test's widescreen factor (fn_80090424, 5. above): 1.33 narrows to 4:3, 1.0 keeps 16:9.
void WarioWideOnScreenFactor(bool wide) {
    constexpr uint32_t kOnScreenScale = 0x80620C9Cu;
    constexpr float kOriginal = 1.33f;
    const float want = wide ? 1.0f : kOriginal;
    if (Memory::ReadFloat32(kOnScreenScale) != want) Memory::WriteFloat32(kOnScreenScale, want);
}

namespace {

// fn_800CCC60 / fn_800CCCE8: the camera's x plus / minus 4 x zoom (constants 4.0 at 0x80620B2C,
// -4.0 at 0x80620B28). The camera is CameraManager(0x8061DB68)+0xA8, its position at +0x38 and its
// zoom at +0x54 (fn_80085094); without a camera the object's (r3) x at +0x54 is used.
float ScreenEdgeX(uint32_t obj, uint32_t halfConstAddr) {
    const uint32_t manager = Memory::Read32(0x8061DB68u);
    const uint32_t camera = (manager != 0 && Memory::Contains(manager + 0xA8u, 4)) ? Memory::Read32(manager + 0xA8u) : 0u;
    const float zoom = (camera != 0 && Memory::Contains(camera + 0x54u, 4)) ? Memory::ReadFloat32(camera + 0x54u) : 1.0f;
    const float x = camera != 0 ? Memory::ReadFloat32(camera + 0x38u) : Memory::ReadFloat32(obj + 0x54u);
    float half = Memory::ReadFloat32(halfConstAddr);
    if (WarioFullWide() && g_wideEdge) half *= 4.0f / 3.0f;
    return half * zoom + x;
}

}  // namespace

extern "C" float Wario_ScreenRightX_HLE(uint32_t obj) { return ScreenEdgeX(obj, 0x80620B2Cu); }
PPC_NATIVE_OVERRIDE(800CCC60, Wario_ScreenRightX_HLE, float, (uint32_t obj), (obj));

extern "C" float Wario_ScreenLeftX_HLE(uint32_t obj) { return ScreenEdgeX(obj, 0x80620B28u); }
PPC_NATIVE_OVERRIDE(800CCCE8, Wario_ScreenLeftX_HLE, float, (uint32_t obj), (obj));
