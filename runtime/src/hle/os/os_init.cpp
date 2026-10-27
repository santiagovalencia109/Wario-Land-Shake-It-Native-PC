// Boot/init hooks, OSFatal, and assorted hardware-init stubs.

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>
#include <thread>

#include "abi_bridge.h"
#include "memory.h"
#include "hle_stubs.h"
#include "ppc_runtime.h"
#include "recomp_mod_loader.h"
#include "runtime_log.h"
#include "system_bridge.h"

extern "C" void func_8041DA1C(CpuContext* ctx);
#if 0  // NSMBW: MKW StaticR.rel prolog
extern "C" void func_8055531C(CpuContext* ctx);
#endif

extern "C" void OSInitAlarm_RecompModLateInit_801a961c(CpuContext* ctx) {
    func_8041DA1C(ctx);
}

REGISTER_NATIVE_FUNCTION_AS(0x8041DA1C, OSInitAlarm_RecompModLateInit_801a961c, "OSInitAlarm_RecompModLateInit_801a961c");

#if 0  // NSMBW: MKW StaticR.rel prolog wrapper (Retro Rewind mod init); NSMBW has no StaticR
extern "C" void StaticRProlog_RecompModInit_8055531c(CpuContext* ctx) {
    RecompMod::RunMemoryInitializers();
    func_8055531C(ctx);
    RecompMod::RunPostRelInitializers();
}
#endif

#if 0  // NSMBW: StaticRProlog_RecompModInit is not linked into NSMBW (was MKW-only)
#if 0
REGISTER_NATIVE_FUNCTION_AS(0x8055531C, StaticRProlog_RecompModInit_8055531c, "StaticRProlog_RecompModInit_8055531c");
#endif
#endif

namespace {
std::string ReadGuestCStringLimited(uint32_t address, size_t limit = 4096) {
    if (address == 0) {
        return "<null>";
    }

    std::string text;
    text.reserve(128);
    for (size_t i = 0; i < limit; ++i) {
        const uint8_t ch = Memory::Read8(address + static_cast<uint32_t>(i));
        if (ch == 0) {
            return text;
        }
        text.push_back(static_cast<char>(ch));
    }
    text += "<unterminated>";
    return text;
}
}

extern "C" void OSFatal_HLE_801a4ec4(CpuContext* ctx) {
    const uint32_t fg = ctx ? ctx->gpr[3] : 0;
    const uint32_t bg = ctx ? ctx->gpr[4] : 0;
    const uint32_t messagePtr = ctx ? ctx->gpr[5] : 0;
    RT_LOG(RT_TAG_OS) << "OS::Fatal called fg=0x" << std::hex << fg
              << " bg=0x" << bg
              << " message=0x" << messagePtr
              << std::dec << " '" << ReadGuestCStringLimited(messagePtr) << "'" << std::endl;
    if (ctx) {
        SystemBridge::DumpCpuState(ctx);
    }
    const std::string guestMessage = ReadGuestCStringLimited(messagePtr);
    const std::string details =
        guestMessage.empty() ? std::string("OS::Fatal was called without a message.") : guestMessage;
    // MarkFatalErrorReported below suppresses the atexit reporter, so this path
    // has to write its own artifacts or the run folder gets nothing.
    RuntimeCrash::WriteCrashArtifacts("osfatal", details);
    SetRuntimeExitCode(EXIT_FAILURE);
    ShowRuntimeFatalPopup("the guest operating system reported a fatal error", details);
    MarkFatalErrorReported();
    std::exit(EXIT_FAILURE);
}

REGISTER_NATIVE_FUNCTION_AS(0x80419E5C, OSFatal_HLE_801a4ec4, "OSFatal_HLE_801a4ec4");

extern "C" void GKI_delay_HLE_801301b4(CpuContext* ctx)
{
    const uint32_t delayMs = ctx ? static_cast<uint32_t>(ctx->gpr[3]) : 0;
    const uint32_t sleepMs = delayMs == 0 ? 1u : std::min(delayMs, 10u);
    std::this_thread::sleep_for(std::chrono::milliseconds(sleepMs));
}

extern "C" uint32_t BTM_IsDeviceUp_HLE_8013a300(CpuContext* ctx)
{
    // Force Bluetooth stack to "up" to avoid endless polling loops while we lack
    // real hardware bring-up.
    constexpr uint32_t kBtmCbBase = 0x80001850u /* WARIO-QUARANTINE: base 0x80557218 sin verificar */;  // btm_cb (NSMBW); BTM_IsDeviceUp reads +0x64E == 5
    constexpr uint32_t kDevStateOffset = 0x64Eu;
    try {
        ::Memory::Write8(kBtmCbBase + kDevStateOffset, 5u);
    } catch (const ::Memory::AccessViolation&) {
        // Ignore; best-effort write
    }

    if (ctx) {
        ctx->gpr[3] = 1;
    }
    return 1;
}

PPC_NATIVE_OVERRIDE_VOID(803C7F68, GKI_delay_HLE_801301b4, (CpuContext* ctx), (ctx));
PPC_NATIVE_OVERRIDE(803D298C, BTM_IsDeviceUp_HLE_8013a300, uint32_t, (CpuContext* ctx), (ctx));

// Serial Interface (SI) - GameCube controller ports; stubbed since we don't emulate the MMIO.

// SIInit (0x801baef0): skips MMIO setup at 0xCD006434 and controller detection.
extern "C" void SIInit_801b2de0()
{
    RT_LOG(RT_TAG_OS) << "SIInit_801b2de0 called: skipping MMIO register setup and controller detection" << std::endl;
}

// SISetSamplingRate (0x801bb840): ignored, we don't emulate SI polling timing.
extern "C" void HLE_SISetSamplingRate_801b3acc(uint32_t msec)
{
    RT_LOG(RT_TAG_OS) << "HLE_SISetSamplingRate_801b3acc called: msec=" << msec << ": Stubbed success." << std::endl;
}

// Video Interface (VI) - TV output.

// VIGetTvFormat (0x801be010): CRITICAL, must return 1 (VI_PAL) not 0, or PAL builds
// misbehave/panic.
extern "C" uint32_t HLE_VIGetTvFormat_801bacd8()
{
    // VI_NTSC = 0, VI_PAL = 1, VI_MPAL = 2
    RT_LOG(RT_TAG_OS) << "HLE_VIGetTvFormat_801bacd8 called: returning VI_PAL (1)" << std::endl;
    return 1;
}

REGISTER_NATIVE_FUNCTION(0x803A52F4, SIInit_801b2de0);
PPC_NATIVE_OVERRIDE_VOID(803A5C1C, HLE_SISetSamplingRate_801b3acc, (uint32_t msec), (msec));

// OS____InitMemoryProtection (0x801B2D50): real version touches MMU/MMIO we don't emulate;
// no-op and return success so boot doesn't stall.
extern "C" uint32_t OS____InitMemoryProtection_801a7dfc(uint32_t r3, uint32_t r4, uint32_t r5, uint32_t r6, uint32_t r7, uint32_t r8)
{
    RT_LOG(RT_TAG_OS) << "OS____InitMemoryProtection_801a7dfc called (stubbed): r3=0x" << std::hex << r3 << std::dec << std::endl;

    return 0;
}

PPC_NATIVE_OVERRIDE(8041C4FC, OS____InitMemoryProtection_801a7dfc, uint32_t, (uint32_t r3, uint32_t r4, uint32_t r5, uint32_t r6, uint32_t r7, uint32_t r8), (r3, r4, r5, r6, r7, r8));

// OSGetConsoleType (0x801a9ee0): standard Wii = 0x12, NDEV (expanded MEM2) = 0x10000012;
// MKWii uses the NDEV result to enable its extra-memory heap path.
extern "C" uint32_t OS__GetConsoleType_8019f33c(uint32_t /*r4*/, uint32_t /*r5*/, uint32_t /*r6*/,
                                                uint32_t /*r7*/, uint32_t /*r8*/, uint32_t /*r31*/)
{
    constexpr uint32_t kRetailMem2Size = 64u * 1024u * 1024u;
    const uint32_t physicalMem2Size = Memory::Read32(0x80003118u);
    const uint32_t consoleType =
        physicalMem2Size == kRetailMem2Size ? 0x00000012u : 0x10000012u;
    static std::atomic<bool> logged{false};
    if (!logged.exchange(true)) {
        RT_LOG(RT_TAG_OS) << "OSGetConsoleType: MEM2="
                  << (physicalMem2Size / (1024u * 1024u))
                  << " MB, returning 0x" << std::hex << consoleType << std::dec << std::endl;
    }
    return consoleType;
}

// Register the function
PPC_NATIVE_OVERRIDE(80414F20, OS__GetConsoleType_8019f33c, uint32_t, (uint32_t r4, uint32_t r5, uint32_t r6, uint32_t r7, uint32_t r8, uint32_t r31), (r4, r5, r6, r7, r8, r31));

// OSGetResetCode (0x801b3c90): real version reads MMIO 0xCC003024; we always report
// Cold Boot (0).
extern "C" uint32_t OSGetResetCode_801a8a50()
{
    // Log occasionally just to track boot flow
    static bool logged = false;
    if (!logged) {
        RT_LOG(RT_TAG_OS) << "OSGetResetCode_801a8a50 called: returning 0 (Cold Boot)" << std::endl;
        logged = true;
    }
    return 0; 
}

// Register the function
PPC_NATIVE_OVERRIDE(8041CEC0, OSGetResetCode_801a8a50, uint32_t, (), ());

// __OSInitSTM (0x801B6820): real version opens /dev/stm/* handles. We stub it by writing
// fake handles and the success flag into the SDA (r13) block so OSResetSystem's checks pass.
extern "C" uint32_t __OSInitSTM_HLE_801ab848(CpuContext* ctx)
{
    CpuContext* cpu = ctx ? ctx : &GetPersistentCpuContext();
    if (!cpu) return 0;

    RT_LOG(RT_TAG_OS) << "__OSInitSTM_HLE_801ab848 called: initializing STM state" << std::endl;

    // R13 (SDA2) holds the base for small data variables
    const uint32_t r13 = cpu->gpr[13];
    if (r13 == 0) {
         RT_LOG(RT_TAG_OS) << "__OSInitSTM: Warning - R13 is 0, cannot write state." << std::endl;
         return 0;
    }

    // WARIO: offsets de __OSInitSTM de Wario (0x8041F7B0): r13-0x25AC=StmReady, r13-0x25A8=StmImDesc
    // (/dev/stm/immediate), r13-0x25A4=StmEhDesc (/dev/stm/eventhook). Los de NSMBW (r13-0x735C/58/54)
    // escribian sobre datos del juego (0x8061B344..4C).
    try {
        // Mark STM as initialized
        ::Memory::Write32(r13 - 0x25acu, 1);

        // Fake non-zero handles so callers' zero-checks pass.
        ::Memory::Write32(r13 - 0x25a8u, 0x00535401);
        ::Memory::Write32(r13 - 0x25a4u, 0x00535402);

        // Default Power/Reset callback pointers are left unset; safe since we never fire
        // the STM hardware interrupt that would invoke them.
    } catch (const ::Memory::AccessViolation& e) {
        RT_LOG(RT_TAG_OS) << "__OSInitSTM: Failed to write STM state to SDA @ 0x"
                  << std::hex << e.address() << std::dec << " (" << e.reason() << ")" << std::endl;
        return 0; // Return failure
    }

    // Return 1 (success)
    return 1;
}

// Register the function
PPC_NATIVE_OVERRIDE(8041F7B0, __OSInitSTM_HLE_801ab848, uint32_t, (CpuContext* ctx), (ctx));


extern "C" void OS____PSInit_801a04a0()
{
    RT_LOG(RT_TAG_OS) << "OS____PSInit_801a04a0 called (stubbed)" << std::endl;
}

PPC_NATIVE_OVERRIDE_VOID(80415FE0, OS____PSInit_801a04a0, (), ());



extern "C" void __init_hardware_80006348()
{
    RT_LOG(RT_TAG_OS) << "__init_hardware_80006348 called (stubbed)" << std::endl;
}

PPC_NATIVE_OVERRIDE_VOID(800063C8,__init_hardware_80006348, (), ());


// PPC SPR (Special Purpose Register) access stubs; these registers don't exist on x86 so
// each one just logs and no-ops.

// Each PPC_NATIVE_OVERRIDE_VOID line stays written out per stub (not looped) because the
// translator text-scans them to decide which addresses to skip translating.
#define PPC_SPR_STUB_BODY(name, message) \
    extern "C" void name() { RT_LOG(RT_TAG_OS) << message << std::endl; }

PPC_SPR_STUB_BODY(PPCMfhid0_8012e574, "PPCMfhid0 called (stubbed) - Move From HID0")
#if 0 /* WARIO-TODO: falta la direccion de PPCMfhid0 (base 8026B304) */
PPC_NATIVE_OVERRIDE_VOID(8026b304, PPCMfhid0_8012e574, (), ());
#endif

PPC_SPR_STUB_BODY(PPCMthid0_8012e57c, "PPCMthid0 called (stubbed) - Move To HID0")
PPC_NATIVE_OVERRIDE_VOID(803C633C, PPCMthid0_8012e57c, (), ());

extern "C" void PPCMtdec_8012e594()
{
    static std::atomic<int> logCount{0};
    if (logCount.fetch_add(1) < 4) {
        RT_LOG(RT_TAG_OS) << "PPCMtdec called (stubbed) - Move To Decrementer" << std::endl;
    }
    // We don't simulate decrementer exceptions, so pump due alarms here instead. Capped at
    // 32, not 1: a single alarm let unrelated periodic alarms backlog and delay
    // AsyncDisplay's pacing alarm by multiple retraces.
    OS_HLE_ProcessAlarms(32);
}
PPC_NATIVE_OVERRIDE_VOID(803C6354, PPCMtdec_8012e594, (), ());

extern "C" void PPCSync_8012e59c()
{
    // PowerPC `sync`. Translated guest code runs on one host thread at a time
    // and the runtime's own cross-thread state uses C++ atomics, so there is no
    // guest-visible reordering for this barrier to prevent.
}
PPC_NATIVE_OVERRIDE_VOID(803C635C, PPCSync_8012e59c, (), ());

PPC_SPR_STUB_BODY(PPCMtmmcr0_8012e5b8, "PPCMtmmcr0 called (stubbed) - Move To MMCR0")
PPC_NATIVE_OVERRIDE_VOID(803C6378, PPCMtmmcr0_8012e5b8, (), ());

PPC_SPR_STUB_BODY(PPCMtmmcr1_8012e5c0, "PPCMtmmcr1 called (stubbed) - Move To MMCR1")
PPC_NATIVE_OVERRIDE_VOID(803C6380, PPCMtmmcr1_8012e5c0, (), ());

PPC_SPR_STUB_BODY(PPCMtpmc1_8012e5c8, "PPCMtpmc1 called (stubbed) - Move To PMC1")
PPC_NATIVE_OVERRIDE_VOID(803C6388, PPCMtpmc1_8012e5c8, (), ());

PPC_SPR_STUB_BODY(PPCMtpmc2_8012e5d0, "PPCMtpmc2 called (stubbed) - Move To PMC2")
PPC_NATIVE_OVERRIDE_VOID(803C6390, PPCMtpmc2_8012e5d0, (), ());

PPC_SPR_STUB_BODY(PPCMtpmc3_8012e5d8, "PPCMtpmc3 called (stubbed) - Move To PMC3")
PPC_NATIVE_OVERRIDE_VOID(803C6398, PPCMtpmc3_8012e5d8, (), ());

PPC_SPR_STUB_BODY(PPCMtpmc4_8012e5e0, "PPCMtpmc4 called (stubbed) - Move To PMC4")
PPC_NATIVE_OVERRIDE_VOID(803C63A0, PPCMtpmc4_8012e5e0, (), ());

extern "C" uint32_t PPCMfhid2_8012e630_impl()
{
    if (CpuContext* cpu = TryGetCpuContext()) {
        if (cpu->hid2 == 0) {
            cpu->hid2 = 0x10000000u;
        }
        return cpu->hid2;
    }
    return 0x10000000u;
}
// Register the function using a return-value stub
extern "C" void PPCMfhid2_HLE_8012e630(CpuContext* ctx)
{
    ctx->gpr[3] = PPCMfhid2_8012e630_impl();
}
REGISTER_TRANSLATED_FUNCTION(0x803C63F0, PPCMfhid2_HLE_8012e630);

extern "C" void PPCMthid2_8012e638(CpuContext* ctx)
{
    CpuContext* cpu = ctx ? ctx : &GetPersistentCpuContext();
    cpu->hid2 = cpu->gpr[3];

    static std::atomic<int> logCount{0};
    if (logCount.fetch_add(1) < 4) {
        RT_LOG(RT_TAG_OS) << "PPCMthid2 set HID2=0x" << std::hex << cpu->hid2 << std::dec << std::endl;
    }
}
PPC_NATIVE_OVERRIDE_VOID(803C63F8, PPCMthid2_8012e638, (CpuContext* ctx), (ctx));

PPC_SPR_STUB_BODY(PPCMfwpar_8012e640,
                  "PPCMfwpar called (stubbed) - Move From Write Pipe Address Register")
PPC_NATIVE_OVERRIDE_VOID(803C6400, PPCMfwpar_8012e640, (), ());

PPC_SPR_STUB_BODY(PPCMtwpar_8012e64c,
                  "PPCMtwpar called (stubbed) - Move To Write Pipe Address Register")
#if 0 /* WARIO-TODO: falta la direccion de PPCMtwpar (base 8026B3DC) */
PPC_NATIVE_OVERRIDE_VOID(8026b3dc, PPCMtwpar_8012e64c, (), ());
#endif

PPC_SPR_STUB_BODY(PPCDisableSpeculation_8012e654, "PPCDisableSpeculation called (stubbed)")
PPC_NATIVE_OVERRIDE_VOID(803C6408, PPCDisableSpeculation_8012e654, (), ());

PPC_SPR_STUB_BODY(PPCMthid4_8012e684, "PPCMthid4 called (stubbed) - Move To HID4")
PPC_NATIVE_OVERRIDE_VOID(803C6438, PPCMthid4_8012e684, (), ());
