// Wario Land: Shake It! runtime fixes for game behaviour that differs from the Wii.
//
// Rope re-catch (BgRopeTest): jumping off a rope, Wario grabbed the same rope again 6-7 frames later.
// The rope (vtable 0x804B7A28) arms "don't catch" (byte +0x21D) when it lets go, and its per-frame
// update fn_8013AB28 only clears it once byte +0x21E ("Wario is touching me", set every frame by the
// rope's hit callback fn_8013CCD8) stays 0: the catch must wait until Wario has left the rope. In
// this port the hit callback stops reaching the rope ~5 frames after the jump while Wario is still
// close enough for the catch distance test (fn_8013AC10 / fn_8013C0BC), so he hangs on again. Until
// the hit-test difference is found, hold +0x21E for the first kRopeReleaseFrames frames after a
// release (state +0x1E4 == 0, frames since release in +0x1EC).
//
// Wario is GameManager+0x150 (vtable 0x8049DFE0); GameManager (vtable 0x8049F528) is reached from
// a global in the small-data sections, found once by scanning them.
#include "memory.h"

#include <array>
#include <cstdint>

namespace {

constexpr uint32_t kGameManagerVtable = 0x8049F528u;
constexpr uint32_t kWarioVtable = 0x8049DFE0u;
constexpr uint32_t kRopeVtable = 0x804B7A28u;
constexpr int kRopeReleaseFrames = 30;

uint32_t ReadOr0(uint32_t addr) {
    uint32_t v = 0;
    return Memory::TryRead32(addr, v) ? v : 0u;
}

bool InRam(uint32_t a) { return (a >= 0x80500000u && a < 0x81800000u) || (a >= 0x90000000u && a < 0x98000000u); }

uint32_t FindGameManagerGlobal() {
    for (uint32_t a = 0x8061A6A0u; a < 0x80629040u; a += 4) {
        const uint32_t p = ReadOr0(a);
        if (InRam(p) && ReadOr0(p) == kGameManagerVtable) return a;
    }
    return 0;
}
}  // namespace

// Called once per input read of player 1 (VirtualRemote::Sample), i.e. every game frame.
void WarioRuntimeFixesTick() {
    static uint32_t gmGlobal = 0;
    static int rescanWait = 0;
    static std::array<uint32_t, 4> ropes{};
    static size_t nextRope = 0;

    uint32_t gm = gmGlobal != 0 ? ReadOr0(gmGlobal) : 0u;
    if (!InRam(gm) || ReadOr0(gm) != kGameManagerVtable) {
        if (rescanWait > 0) { --rescanWait; return; }
        gmGlobal = FindGameManagerGlobal();
        rescanWait = 120;  // not in a level: look again in about two seconds
        gm = gmGlobal != 0 ? ReadOr0(gmGlobal) : 0u;
        if (!InRam(gm)) return;
    }
    const uint32_t wario = ReadOr0(gm + 0x150u);
    if (!InRam(wario) || ReadOr0(wario) != kWarioVtable) return;

    // Remember the ropes Wario hangs from.
    const uint32_t held = ReadOr0(wario + 0x24u);
    if (InRam(held) && ReadOr0(held) == kRopeVtable) {
        bool known = false;
        for (const uint32_t r : ropes) known = known || r == held;
        if (!known) { ropes[nextRope] = held; nextRope = (nextRope + 1) % ropes.size(); }
    }
    for (uint32_t& rope : ropes) {
        if (rope == 0) continue;
        if (ReadOr0(rope) != kRopeVtable) { rope = 0; continue; }  // freed with its level
        uint32_t state = 0, sinceRelease = 0;
        if (!Memory::TryRead32(rope + 0x1E4u, state) || !Memory::TryRead32(rope + 0x1ECu, sinceRelease)) continue;
        if (state == 0 && Memory::Read8(rope + 0x21Du) != 0 && sinceRelease < static_cast<uint32_t>(kRopeReleaseFrames)) {
            Memory::Write8(rope + 0x21Eu, 1);
        }
    }
}
