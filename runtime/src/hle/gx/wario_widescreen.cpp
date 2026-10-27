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
//   4. the screen-edge queries fn_800CCC60 / fn_800CCCE8 answer +-16/3 x zoom (below).
// WARIO_HIDE_PANES in the environment turns it on as well (debugging).
#include "gx_internal.h"
#include "hle_stubs.h"
#include "runtime_config.h"
#include "display_settings.h"

#include <cstdlib>

bool WarioFullWide() {
    static const bool configured =
        RuntimeConfigFile::FullWidescreen(false) || std::getenv("WARIO_HIDE_PANES") != nullptr;
    // GameWidescreen (what SCGetAspectRatio reports) is asked every time rather than cached, so a
    // call before the display settings are in place can never pin the wrong answer.
    return configured && DisplaySettings::GameWidescreen();
}

// Called right before each layout draw (gx_pixel.cpp), so it also runs after the static
// constructors that fill the view-rect globals.
void WarioWideApplyGlobals() {
    if (!WarioFullWide()) return;
    // fn_80051E28 culls the stage's objects against +-4 x +-3 units times this factor (1.0, read
    // only there).
    constexpr uint32_t kStageCullScale = 0x8061AA90u;
    if (Memory::ReadFloat32(kStageCullScale) != 4.0f / 3.0f) Memory::WriteFloat32(kStageCullScale, 4.0f / 3.0f);
    // "Screen width" globals (k x 4.0, set by static constructors) that the camera code passes to
    // fn_8004C654 to find where it must stop at a room's edges: fn_802864F8 & co. (0x80628BAC),
    // fn_8011D8C8 (0x806282DC), fn_8004E7E4 (0x80627D2C), fn_80215E68 (0x8062890C). The 4:3 value
    // is remembered on first sight so the write never compounds.
    static const uint32_t kViewWidths[] = {0x80628BACu, 0x806282DCu, 0x80627D2Cu, 0x8062890Cu};
    static float baseWidth[4] = {0.f, 0.f, 0.f, 0.f};
    for (int i = 0; i < 4; ++i) {
        const float cur = Memory::ReadFloat32(kViewWidths[i]);
        if (baseWidth[i] == 0.f && cur > 0.f) baseWidth[i] = cur;
        if (baseWidth[i] > 0.f && cur == baseWidth[i]) Memory::WriteFloat32(kViewWidths[i], baseWidth[i] * 4.0f / 3.0f);
    }
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
    if (WarioFullWide()) half *= 4.0f / 3.0f;
    return half * zoom + x;
}

}  // namespace

extern "C" float Wario_ScreenRightX_HLE(uint32_t obj) { return ScreenEdgeX(obj, 0x80620B2Cu); }
PPC_NATIVE_OVERRIDE(800CCC60, Wario_ScreenRightX_HLE, float, (uint32_t obj), (obj));

extern "C" float Wario_ScreenLeftX_HLE(uint32_t obj) { return ScreenEdgeX(obj, 0x80620B28u); }
PPC_NATIVE_OVERRIDE(800CCCE8, Wario_ScreenLeftX_HLE, float, (uint32_t obj), (obj));
