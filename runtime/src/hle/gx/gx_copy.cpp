// gx_copy.cpp - Framebuffer Copy Operations
#include "gx_internal.h"
#include "runtime_log.h"

#include "settings_overlay.h"
extern "C" uint32_t WarioHostSkipButtons();
extern "C" void WarioIntroButtons(uint32_t hold);

#include <aurora/aurora.h>
#include <dolphin/gx/GXAurora.h>

#include <algorithm>
#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <string>
#include <map>
#include <mutex>
#include <vector>

namespace {
// Copy destinations stay GPU-only until an explicit/lazy readback. Track the
// guest ranges that own those results so a later data-cache flush over a reused
// allocation can retire the stale GPU texture before it is considered by a
// subsequent GXLoadTexObj. There is at most one live range per destination;
// rewriting the destination replaces its previous extent.
std::mutex g_efbCopyDestinationsMutex;
std::map<uint32_t, uint32_t> g_efbCopyDestinations;
uint32_t g_largestEfbCopyDestination = 0;

void RememberEfbCopyDestination(uint32_t addr, uint32_t size) {
    if (size == 0) {
        return;
    }
    std::lock_guard<std::mutex> guard(g_efbCopyDestinationsMutex);
    g_efbCopyDestinations[CanonicalizeGxMainRamAddress(addr)] = size;
    // A conservative monotonic maximum lets invalidation jump directly to
    // the only map interval that could overlap instead of walking every copy
    // destination on every DCStoreRange.
    g_largestEfbCopyDestination = std::max(g_largestEfbCopyDestination, size);
}

} // namespace

void DownloadEfbCopiesForRange(uint32_t addr, uint32_t size) {
    if (size == 0) {
        return;
    }
    const uint64_t start = CanonicalizeGxMainRamAddress(addr);
    const uint64_t end = start + size;
    std::vector<uint32_t> overlapping;
    {
        std::lock_guard<std::mutex> guard(g_efbCopyDestinationsMutex);
        const uint64_t earliest = start > g_largestEfbCopyDestination ? start - g_largestEfbCopyDestination : 0;
        for (auto it = g_efbCopyDestinations.lower_bound(static_cast<uint32_t>(earliest));
             it != g_efbCopyDestinations.end() && static_cast<uint64_t>(it->first) < end; ++it) {
            if (static_cast<uint64_t>(it->first) + it->second > start) overlapping.push_back(it->first);
        }
    }
    for (const uint32_t copyAddr : overlapping) {
        aurora_flush_efb_copy_to_ram(GuestToHostPtr(copyAddr));  // no-op unless still pending
    }
}

void InvalidateEfbCopyDestinationsForRange(uint32_t addr, uint32_t size) {
    if (size == 0) {
        return;
    }

    const uint64_t dirtyStart = CanonicalizeGxMainRamAddress(addr);
    const uint64_t dirtyEnd = dirtyStart + size;
    std::vector<uint32_t> retired;
    {
        std::lock_guard<std::mutex> guard(g_efbCopyDestinationsMutex);
        const uint64_t earliestCandidate =
            dirtyStart > g_largestEfbCopyDestination ? dirtyStart - g_largestEfbCopyDestination : 0;
        for (auto it = g_efbCopyDestinations.lower_bound(static_cast<uint32_t>(earliestCandidate));
             it != g_efbCopyDestinations.end() && static_cast<uint64_t>(it->first) < dirtyEnd;) {
            const uint64_t copyStart = it->first;
            const uint64_t copyEnd = copyStart + it->second;
            if (dirtyEnd <= copyStart || dirtyStart >= copyEnd) {
                ++it;
                continue;
            }
            retired.push_back(it->first);
            it = g_efbCopyDestinations.erase(it);
        }
    }

    // Preserve FIFO ordering: the destroy command is emitted before any later
    // texture load that can consume the freshly flushed RAM bytes.
    for (const uint32_t copyAddr : retired) {
        GXDestroyCopyTex(GuestToHostPtr(copyAddr));
    }
}

// ============================================================================
// Display Copy Source/Destination
// ============================================================================

extern "C" void GX__SetDispCopySrc_8016f438(uint32_t l, uint32_t t, uint32_t w, uint32_t h) {
    GXSetDispCopySrc((u16)l, (u16)t, (u16)w, (u16)h);
}
#if 0 /* WARIO-TODO: falta la direccion de GXSetDispCopySrc (base 8028D82C) */
PPC_NATIVE_OVERRIDE_VOID(8028d82c, GX__SetDispCopySrc_8016f438, (uint32_t l, uint32_t t, uint32_t w, uint32_t h), (l, t, w, h));
#endif

extern "C" void GX__SetDispCopyDst_8016f4b8(uint32_t w, uint32_t h) { GXSetDispCopyDst((u16)w, (u16)h); }
PPC_NATIVE_OVERRIDE_VOID(8040343C, GX__SetDispCopyDst_8016f4b8, (uint32_t w, uint32_t h), (w, h));

// ============================================================================
// Texture Copy Source/Destination
// ============================================================================

extern "C" void GX__SetTexCopySrc_8016f478(uint32_t l, uint32_t t, uint32_t w, uint32_t h) {
    GXSetTexCopySrc((u16)l, (u16)t, (u16)w, (u16)h);
    g_texCopyState.srcLeft=(u16)l; g_texCopyState.srcTop=(u16)t;
    g_texCopyState.srcWidth=(u16)w; g_texCopyState.srcHeight=(u16)h;
}
PPC_NATIVE_OVERRIDE_VOID(804033FC, GX__SetTexCopySrc_8016f478, (uint32_t l, uint32_t t, uint32_t w, uint32_t h), (l, t, w, h));

extern "C" void GX__SetTexCopyDst_8016f4dc(uint32_t w, uint32_t h, uint32_t f, uint32_t m) {
    GXSetTexCopyDst((u16)w, (u16)h, (GXTexFmt)f, (GXBool)m);
    g_texCopyState.dstWidth=(u16)w; g_texCopyState.dstHeight=(u16)h;
    g_texCopyState.dstFormat=f; g_texCopyState.dstMipmap=m;
}
PPC_NATIVE_OVERRIDE_VOID(80403460, GX__SetTexCopyDst_8016f4dc, (uint32_t w, uint32_t h, uint32_t f, uint32_t m), (w, h, f, m));

extern "C" void GX__SetCopyFilter_8016fa40(uint32_t aa, uint32_t spa, uint32_t vf, uint32_t vfa) {
    uint8_t sp[12][2]={}, vfb[7]={};
    if(spa) std::memcpy(sp, GuestToHostPtr(spa, 24), 24);
    if(vfa) std::memcpy(vfb, GuestToHostPtr(vfa, 7), 7);
    GXSetCopyFilter((GXBool)aa, sp, (GXBool)vf, vfb);
}
PPC_NATIVE_OVERRIDE_VOID(80403938, GX__SetCopyFilter_8016fa40, (uint32_t aa, uint32_t spa, uint32_t vf, uint32_t vfa), (aa, spa, vf, vfa));

extern "C" void GX__SetDispCopyGamma_8016fc24(uint32_t g) { GXSetDispCopyGamma((GXGamma)g); }
PPC_NATIVE_OVERRIDE_VOID(80403B1C, GX__SetDispCopyGamma_8016fc24, (uint32_t g), (g));

// ============================================================================
// Copy Execution
// ============================================================================

extern "C" void GX__CopyDisp_8016fc38(uint32_t da, uint32_t c) {
    EnsureAuroraFrameActive();
    // GX copies are FIFO-ordered on hardware. Drain submitted draws before
    // resolving the EFB so high-level copies see the same contents.
    GXDrawDone();
    GXCopyDisp(GuestToHostPtr(da), (GXBool)c);
    // No second GXDrawDone here: the frame-worker wait below is for the DONE
    // phase, which strictly subsumes the drain this call would perform.
    ++g_gxFrameCount;
    VI_HLE_SetXfbReady(da);
    // Present immediately so post-copy draws don't leak into this frame. Join at the DONE phase
    // (not the cheaper SEALED phase GXDrawDone waits for) because ImGui's draw lists, owned by
    // Aurora's render worker, replay during encode; aurora_end_frame would join here anyway.
    aurora_wait_for_frame_worker();
    settings_overlay::Draw();
    // Seal, pace to the VI retrace boundary (Aurora renders the sealed frame
    // during the wait), and pre-warm the next frame.
    VI_HLE_PresentFrame(/*presentedXfb=*/true, /*paceToRetrace=*/true);
}

PPC_NATIVE_OVERRIDE_VOID(80403B30, GX__CopyDisp_8016fc38, (uint32_t da, uint32_t c), (da, c));


extern "C" void GX__CopyTex_8016fd74(uint32_t da, uint32_t c) {
    // NSMBW_LOG_ORTHO also logs each distinct EFB-to-texture copy (source rect, destination size,
    // format, clear) with its caller (finding a play area rendered to a texture).
    static const bool logCopies = std::getenv("NSMBW_LOG_ORTHO") != nullptr;
    if (logCopies) {
        static std::vector<std::vector<uint32_t>> seenCopies;
        const CpuContext* cpu = TryGetCpuContext();
        std::vector<uint32_t> key{g_texCopyState.srcLeft, g_texCopyState.srcTop, g_texCopyState.srcWidth, g_texCopyState.srcHeight,
                                  g_texCopyState.dstWidth, g_texCopyState.dstHeight, g_texCopyState.dstFormat, c, cpu ? cpu->lr : 0u};
        if (std::find(seenCopies.begin(), seenCopies.end(), key) == seenCopies.end() && seenCopies.size() < 64) {
            seenCopies.push_back(key);
            std::fprintf(stderr, "[gx] copytex src %u %u %u x %u -> %u x %u fmt %u clear %u dst %08X lr %08X\n", key[0], key[1], key[2], key[3],
                         key[4], key[5], key[6], c, da, key[8]);
        }
    }
    EnsureAuroraFrameActive();
    // Match GX FIFO ordering: texture copies observe all prior draws.
    GXDrawDone();
    const uint16_t rawSrcLeft = g_texCopyState.srcLeft;
    const uint16_t rawSrcTop = g_texCopyState.srcTop;
    const uint16_t rawSrcWidth = g_texCopyState.srcWidth;
    const uint16_t rawSrcHeight = g_texCopyState.srcHeight;
    { static int n = 0; if (n++ < 40) RT_LOGF(RT_TAG_GX, "[copy] tex copy src %u,%u %ux%u -> 0x%08X clear=%u\n", rawSrcLeft, rawSrcTop, rawSrcWidth, rawSrcHeight, da, c); }

    // Keep the source in guest EFB coordinates. Aurora maps it to the scaled
    // EFB exactly once, matching Dolphin's ConvertEFBRectangle path.
    GXSetTexCopySrc(rawSrcLeft, rawSrcTop, rawSrcWidth, rawSrcHeight);
    // EFB copies stay GPU-only except probe-sized ones (e.g. the 4x4 lens-flare depth probe),
    // which Aurora reads back asynchronously via efb_ram::schedule and land in guest RAM a frame
    // later. RISK: copies above the probe threshold, or on the offscreen list, are not
    // auto-downloaded, so guest reads see stale RAM; call aurora_flush_efb_copies_to_ram if a
    // copy needs reading back.
    // NSMBW_LOG_COPY_FRAMES: per frame, the texture copies made (size, format, source, destination),
    // logged with the time whenever that list changes from the previous frame's.
    if (static const bool logFrames = std::getenv("NSMBW_LOG_COPY_FRAMES") != nullptr; logFrames) {
        static uint32_t frame = ~0u;
        static std::string current, previous;
        if (frame != static_cast<uint32_t>(g_gxFrameCount)) {
            if (current != previous) {
                const auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(
                                    std::chrono::steady_clock::now().time_since_epoch()).count();
                std::fprintf(stderr, "[copyframe] t=%lld frame %u:%s\n", static_cast<long long>(ms), frame,
                             current.c_str());
                previous = current;
            }
            current.clear();
            frame = static_cast<uint32_t>(g_gxFrameCount);
        }
        char item[96];
        std::snprintf(item, sizeof(item), " %ux%u/%u@%u,%u>%08X", g_texCopyState.dstWidth, g_texCopyState.dstHeight,
                      g_texCopyState.dstFormat, rawSrcLeft, rawSrcTop, da);
        current += item;
    }
    // NSMBW_LOG_COPIES: each distinct texture copy (destination size, format, source rectangle) once,
    // with the guest code that asked for it (profiling).
    if (static const bool logCopies = std::getenv("NSMBW_LOG_COPIES") != nullptr; logCopies) {
        static std::vector<uint64_t> seen;
        const uint64_t key = (uint64_t(g_texCopyState.dstWidth) << 48) | (uint64_t(g_texCopyState.dstHeight) << 32) |
                             (uint64_t(rawSrcWidth) << 16) | rawSrcHeight;
        if (std::find(seen.begin(), seen.end(), key) == seen.end() && seen.size() < 64) {
            seen.push_back(key);
            uint32_t lr = 0, ra[4] = {};
            if (const CpuContext* cpu = TryGetCpuContext()) {
                lr = cpu->lr;
                uint32_t sp = cpu->gpr[1];
                for (int d = 0; d < 4 && sp != 0; ++d) {
                    uint32_t back = 0;
                    if (!Memory::TryRead32(sp, back) || back == 0) break;
                    Memory::TryRead32(back + 4, ra[d]);
                    sp = back;
                }
            }
            std::fprintf(stderr, "[copytex] dst %ux%u fmt %u src %u,%u %ux%u dest %08X lr %08X %08X %08X %08X %08X\n",
                         g_texCopyState.dstWidth, g_texCopyState.dstHeight, g_texCopyState.dstFormat, rawSrcLeft,
                         rawSrcTop, rawSrcWidth, rawSrcHeight, da, lr, ra[0], ra[1], ra[2], ra[3]);
        }
    }
    GXCopyTex(GuestToHostPtr(da), (GXBool)c);
    // The extent the copy writes: a destination bigger than the source is a patch into a larger
    // texture (aurora sizes its copy the same way), not a claim on that whole texture's memory.
    const uint32_t copiedWidth = std::max<uint32_t>(rawSrcWidth >> (g_texCopyState.dstMipmap ? 1 : 0), 1);
    const uint32_t copiedHeight = std::max<uint32_t>(rawSrcHeight >> (g_texCopyState.dstMipmap ? 1 : 0), 1);
    RememberEfbCopyDestination(
        da, GXGetTexBufferSize(std::min<uint32_t>(g_texCopyState.dstWidth, copiedWidth),
                               std::min<uint32_t>(g_texCopyState.dstHeight, copiedHeight),
                               g_texCopyState.dstFormat, GX_FALSE, 0));
    GXSetTexCopySrc(rawSrcLeft, rawSrcTop, rawSrcWidth, rawSrcHeight);
}
PPC_NATIVE_OVERRIDE_VOID(80403C6C, GX__CopyTex_8016fd74, (uint32_t da, uint32_t c), (da, c));

extern "C" uint32_t GX__SetDispCopyYScale_HLE(float vscale) { return GXSetDispCopyYScale(vscale); }
PPC_NATIVE_OVERRIDE(804037F4, GX__SetDispCopyYScale_HLE, uint32_t, (float vscale), (vscale));

extern "C" float GX__GetYScaleFactor_HLE(uint32_t efbHeight, uint32_t xfbHeight) {
    if (efbHeight == 0 || xfbHeight == 0 || xfbHeight > 1024) return 1.0f;
    return GXGetYScaleFactor(static_cast<u16>(efbHeight), static_cast<u16>(xfbHeight));
}
PPC_NATIVE_OVERRIDE(804035C4, GX__GetYScaleFactor_HLE, float, (uint32_t efbHeight, uint32_t xfbHeight), (efbHeight, xfbHeight));
