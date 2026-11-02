// WARIO (diagnostico, WARIO_SUB_WATCH en el entorno; solo Windows): quien escribe la posicion del submarino.
//
// En los niveles del submarino el 16:9 completo deja limites de 4:3 (el submarino no llega a los lados, los
// enemigos aparecen y los torpedos desaparecen en el borde de 4:3). Para encontrar el codigo que limita el
// submarino, la pagina del host (vista de invitado, la que usa el codigo traducido) con su posicion se protege
// contra escritura; cada escritura en x/y (+0x54/+0x58) del submarino se registra una vez por cadena de
// llamadas: RVA del host (func_80xxxxxx con _local/tools/rva2sym.py), LR del invitado, valor y distancia a la
// camara. Ademas, cada segundo, el rango de x/y del submarino respecto a la camara en ese segundo.
#include "memory.h"
#include "memory_access.h"
#include "runtime_log.h"
#include "isa/ppc_isa_context.h"

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>

namespace {

constexpr uint32_t kGameManagerVtable = 0x8049F528u;
constexpr uint32_t kSubVptr = 0x804AD398u;  // Submarine (RTTI 0x8061AA98)

uint32_t Rd(uint32_t a) { uint32_t v = 0; return Memory::TryRead32(a, v) ? v : 0u; }
float RdF(uint32_t a) { uint32_t v = Rd(a); float f; std::memcpy(&f, &v, 4); return f; }
bool InRam(uint32_t a) { return (a >= 0x80000000u && a < 0x81800000u) || (a >= 0x90000000u && a < 0x98000000u); }

float CameraX() {
    const uint32_t m = Rd(0x8061DB68u), c = m ? Rd(m + 0xA8u) : 0u;
    return InRam(c) ? RdF(c + 0x38u) : 0.f;
}
float CameraY() {
    const uint32_t m = Rd(0x8061DB68u), c = m ? Rd(m + 0xA8u) : 0u;
    return InRam(c) ? RdF(c + 0x3Cu) : 0.f;
}

// ---- vigilancia ----
uint32_t g_watchGuest = 0;          // direccion del x del submarino (+0x54); se vigilan 8 bytes (x, y)
uintptr_t g_watchPage = 0;
thread_local bool t_step = false;
thread_local uint32_t t_addr = 0;
thread_local uint32_t t_old = 0;
thread_local uintptr_t t_chain[8] = {};
thread_local int t_depth = 0;
thread_local uint32_t t_lr = 0;
constexpr int kMaxKeys = 400;
uint64_t g_keys[kMaxKeys];
int g_keyCount = 0;
int g_lines = 0;

void Protect(bool on) {
    DWORD old = 0;
    VirtualProtect(reinterpret_cast<void*>(g_watchPage), 0x1000, on ? PAGE_READONLY : PAGE_READWRITE, &old);
}

LONG CALLBACK Handler(EXCEPTION_POINTERS* info) {
    const DWORD code = info->ExceptionRecord->ExceptionCode;
    if (code == EXCEPTION_ACCESS_VIOLATION && g_watchPage != 0 && info->ExceptionRecord->NumberParameters >= 2 &&
        info->ExceptionRecord->ExceptionInformation[0] == 1) {
        const uintptr_t fault = static_cast<uintptr_t>(info->ExceptionRecord->ExceptionInformation[1]);
        if ((fault & ~uintptr_t(0xFFF)) != g_watchPage) return EXCEPTION_CONTINUE_SEARCH;
        const uintptr_t base = reinterpret_cast<uintptr_t>(MKW_FLAT_GUEST_BASE);
        const uint32_t guest = static_cast<uint32_t>(fault - base);
        t_addr = (guest >= g_watchGuest && guest < g_watchGuest + 8) ? (guest & ~3u) : 0u;
        if (t_addr != 0) {
            t_old = Rd(t_addr);
            const CpuContext* cpu = TryGetCpuContext();
            t_lr = cpu ? cpu->lr : 0u;
            // Pila del host: RVA de cada marco (funciones traducidas func_80xxxxxx).
            CONTEXT ctx = *info->ContextRecord;
            const uintptr_t mod = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
            t_depth = 0;
            for (int i = 0; i < 8; ++i) {
                t_chain[t_depth++] = ctx.Rip - mod;
                DWORD64 imageBase = 0;
                PRUNTIME_FUNCTION fn = RtlLookupFunctionEntry(ctx.Rip, &imageBase, nullptr);
                if (!fn) break;
                void* handlerData = nullptr;
                DWORD64 frame = 0;
                RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, ctx.Rip, fn, &ctx, &handlerData, &frame, nullptr);
                if (ctx.Rip == 0) break;
            }
        }
        Protect(false);
        info->ContextRecord->EFlags |= 0x100;  // un paso y se vuelve a proteger
        t_step = true;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    if (code == EXCEPTION_SINGLE_STEP && t_step) {
        t_step = false;
        Protect(true);
        if (t_addr != 0) {
            uint64_t key = 1469598103934665603ull;
            for (int i = 0; i < t_depth && i < 4; ++i) key = (key ^ t_chain[i]) * 1099511628211ull;
            key ^= t_addr - g_watchGuest;
            bool seen = false;
            for (int i = 0; i < g_keyCount; ++i) seen = seen || g_keys[i] == key;
            if (!seen && g_keyCount < kMaxKeys) {
                g_keys[g_keyCount++] = key;
                const uint32_t nv = Rd(t_addr);
                float of, nf;
                std::memcpy(&of, &t_old, 4);
                std::memcpy(&nf, &nv, 4);
                const bool isX = t_addr == g_watchGuest;
                const float cam = isX ? CameraX() : CameraY();
                char chain[160] = {};
                int len = 0;
                for (int i = 0; i < t_depth && len < 140; ++i) len += std::snprintf(chain + len, sizeof chain - len, " %llX", (unsigned long long)t_chain[i]);
                RT_LOGF(RT_TAG_RUNTIME, "[subw] %s %.3f -> %.3f (camara %.3f, dif %.3f) lr %08X rva%s\n", isX ? "x" : "y", of, nf, cam,
                        nf - cam, t_lr, chain);
            }
        }
        t_addr = 0;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}

void Watch(uint32_t guestX) {
    static bool installed = false;
    if (!installed) { AddVectoredExceptionHandler(1, Handler); installed = true; }
    if (g_watchPage != 0) Protect(false);
    g_watchGuest = guestX;
    g_watchPage = (reinterpret_cast<uintptr_t>(MKW_FLAT_GUEST_BASE) + guestX) & ~uintptr_t(0xFFF);
    Protect(true);
    RT_LOGF(RT_TAG_RUNTIME, "[subw] vigilando el submarino: x en 0x%08X\n", guestX);
}

}  // namespace

// Cada lectura de la entrada del jugador 1 (wario_fixes.cpp).
void WarioSubDiagTick() {
    static const bool on = std::getenv("WARIO_SUB_WATCH") != nullptr;
    if (!on) return;
    static uint32_t gmGlobal = 0;
    uint32_t gm = gmGlobal ? Rd(gmGlobal) : 0u;
    if (!InRam(gm) || Rd(gm) != kGameManagerVtable) {
        gmGlobal = 0;
        for (uint32_t a = 0x8061A6A0u; a < 0x80629040u && !gmGlobal; a += 4) {
            const uint32_t p = Rd(a);
            if (InRam(p) && Rd(p) == kGameManagerVtable) gmGlobal = a;
        }
        return;
    }
    // El submarino: el jugador de GameManager+0x150, o cualquier puntero de GameManager con su vtable.
    static uint32_t sub = 0;
    if (sub == 0 || Rd(sub) != kSubVptr) {
        sub = 0;
        for (uint32_t off = 0; off < 0x400 && !sub; off += 4) {
            const uint32_t p = Rd(gm + off);
            if (InRam(p) && Rd(p) == kSubVptr) sub = p;
        }
        if (sub != 0) {
            RT_LOGF(RT_TAG_RUNTIME, "[subw] submarino en 0x%08X\n", sub);
            Watch(sub + 0x54u);
        } else {
            static int wait = 0;
            if (++wait % 300 == 0) {
                const uint32_t p = Rd(gm + 0x150u);
                RT_LOGF(RT_TAG_RUNTIME, "[subw] submarino no encontrado (GameManager 0x%08X, +0x150 0x%08X vtable 0x%08X)\n", gm, p, Rd(p));
            }
        }
        return;
    }
    // Rango de posiciones respecto a la camara, cada 60 cuadros.
    static float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
    static int frames = 0;
    const float dx = RdF(sub + 0x54u) - CameraX(), dy = RdF(sub + 0x58u) - CameraY();
    if (dx < minX) minX = dx;
    if (dx > maxX) maxX = dx;
    if (dy < minY) minY = dy;
    if (dy > maxY) maxY = dy;
    if (++frames >= 60 && g_lines < 600) {
        ++g_lines;
        RT_LOGF(RT_TAG_RUNTIME, "[subr] x-camara %.3f .. %.3f  y-camara %.3f .. %.3f  camara %.3f %.3f\n", minX, maxX, minY, maxY,
                CameraX(), CameraY());
        frames = 0;
        minX = minY = 1e9f;
        maxX = maxY = -1e9f;
    }
}
#else
void WarioSubDiagTick() {}
#endif
