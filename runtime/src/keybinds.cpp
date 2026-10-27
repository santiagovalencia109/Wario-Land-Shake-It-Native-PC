#include "keybinds.h"

#include "runtime_config.h"

#include <SDL3/SDL_keyboard.h>
#include <SDL3/SDL_scancode.h>
#include <SDL3/SDL_timer.h>

#include <array>
#include <cstdint>
#include <string>
#include <mutex>
#include <vector>

namespace Keybinds {
namespace {

struct Default {
    const char* key;   // Config.toml [keyboard] key
    const char* name;  // shown in the Keybinds tab
    std::vector<SDL_Scancode> codes;
};

const std::array<Default, kActionCount>& Defaults() {
    static const std::array<Default, kActionCount> defaults = {{
        {"up", "Up", {SDL_SCANCODE_UP}},
        {"down", "Down", {SDL_SCANCODE_DOWN}},
        {"left", "Left", {SDL_SCANCODE_LEFT}},
        {"right", "Right", {SDL_SCANCODE_RIGHT}},
        {"jump", "Jump (2)", {SDL_SCANCODE_X, SDL_SCANCODE_SPACE}},
        {"run", "Run / Fire (1)", {SDL_SCANCODE_Z, SDL_SCANCODE_LSHIFT}},
        {"a", "A", {SDL_SCANCODE_A}},
        {"shake", "Shake (Spin)", {SDL_SCANCODE_C, SDL_SCANCODE_LCTRL}},
        {"tilt_left", "Tilt: lower angle", {SDL_SCANCODE_Q}},
        {"tilt_right", "Tilt: raise angle", {SDL_SCANCODE_E}},
        {"plus", "+ (Pause)", {SDL_SCANCODE_RETURN, SDL_SCANCODE_ESCAPE}},
        {"minus", "- (Minus)", {SDL_SCANCODE_MINUS, SDL_SCANCODE_TAB}},
    }};
    return defaults;
}

std::mutex g_mutex;
bool g_loaded = false;
std::array<std::vector<SDL_Scancode>, kActionCount> g_codes;
int g_capturing = -1;
std::vector<bool> g_heldAtCapture;  // keys already down when the capture began
bool g_suppress = false;            // a capture just ended: ignore the keyboard until all keys are up
std::string g_captureNote;          // WARIO: why the last capture was refused (key already in use)
uint64_t g_captureNoteNs = 0;

std::vector<SDL_Scancode> Parse(const std::string& text) {
    std::vector<SDL_Scancode> codes;
    size_t start = 0;
    while (start <= text.size()) {
        const size_t comma = text.find(',', start);
        const std::string name = RuntimeConfigFile::Trim(
            std::string_view(text).substr(start, comma == std::string::npos ? std::string::npos : comma - start));
        if (!name.empty()) {
            const SDL_Scancode code = SDL_GetScancodeFromName(name.c_str());
            if (code != SDL_SCANCODE_UNKNOWN) {
                codes.push_back(code);
            }
        }
        if (comma == std::string::npos) {
            break;
        }
        start = comma + 1;
    }
    return codes;
}

std::string Format(const std::vector<SDL_Scancode>& codes, const char* separator) {
    std::string text;
    for (const SDL_Scancode code : codes) {
        if (!text.empty()) {
            text += separator;
        }
        text += SDL_GetScancodeName(code);
    }
    return text;
}

void LoadLocked() {
    if (g_loaded) {
        return;
    }
    g_loaded = true;
    for (int i = 0; i < kActionCount; ++i) {
        const std::string saved = RuntimeConfigFile::KeyboardBinding(Defaults()[i].key);
        g_codes[i] = saved.empty() ? Defaults()[i].codes : Parse(saved);
    }
}

void SaveLocked(int action) {
    RuntimeConfigFile::SetKeyboardBinding(Defaults()[action].key, Format(g_codes[action], ","));
}

bool AnyKeyDown() {
    int count = 0;
    const bool* keys = SDL_GetKeyboardState(&count);
    for (int i = 0; keys != nullptr && i < count; ++i) {
        if (keys[i]) {
            return true;
        }
    }
    return false;
}

} // namespace

const char* ActionName(int action) {
    return action >= 0 && action < kActionCount ? Defaults()[action].name : "";
}

std::string KeyLabel(int action) {
    std::lock_guard<std::mutex> lock(g_mutex);
    LoadLocked();
    if (action < 0 || action >= kActionCount) {
        return {};
    }
    const std::string text = Format(g_codes[action], " / ");
    return text.empty() ? "None" : text;
}

bool Held(int action) {
    std::lock_guard<std::mutex> lock(g_mutex);
    LoadLocked();
    if (g_capturing >= 0 || g_suppress || action < 0 || action >= kActionCount) {
        return false;
    }
    int count = 0;
    const bool* keys = SDL_GetKeyboardState(&count);
    for (const SDL_Scancode code : g_codes[action]) {
        if (keys != nullptr && code < count && keys[code]) {
            return true;
        }
    }
    return false;
}

void BeginCapture(int action) {
    std::lock_guard<std::mutex> lock(g_mutex);
    LoadLocked();
    int count = 0;
    const bool* keys = SDL_GetKeyboardState(&count);
    g_heldAtCapture.assign(static_cast<size_t>(count), false);
    for (int i = 0; keys != nullptr && i < count; ++i) {
        g_heldAtCapture[i] = keys[i];
    }
    g_capturing = action;
}

int Capturing() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_capturing;
}

bool PollCapture() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_capturing < 0) {
        return true;
    }
    int count = 0;
    const bool* keys = SDL_GetKeyboardState(&count);
    for (int i = 0; keys != nullptr && i < count; ++i) {
        const bool before = i < static_cast<int>(g_heldAtCapture.size()) && g_heldAtCapture[i];
        if (!keys[i]) {
            if (before) {
                g_heldAtCapture[i] = false;  // released: a later press of it counts
            }
            continue;
        }
        if (before) {
            continue;
        }
        const auto code = static_cast<SDL_Scancode>(i);
        if (code != SDL_SCANCODE_ESCAPE) {
            // WARIO: una tecla solo puede tener una accion; si ya la usa otra, no se asigna.
            int owner = -1;
            for (int a = 0; a < kActionCount && owner < 0; ++a) {
                if (a == g_capturing) continue;
                for (const SDL_Scancode c : g_codes[a]) {
                    if (c == code) { owner = a; break; }
                }
            }
            if (owner >= 0) {
                const char* keyName = SDL_GetScancodeName(code);
                g_captureNote = std::string(keyName != nullptr && *keyName ? keyName : "That key") + " is already used by " +
                                Defaults()[owner].name + ". Change that one first.";
                g_captureNoteNs = SDL_GetTicksNS();
            } else {
                g_codes[g_capturing] = {code};
                SaveLocked(g_capturing);
                g_captureNote.clear();
            }
        }
        g_capturing = -1;
        g_suppress = true;
        return true;
    }
    return false;
}

bool Suppressed() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_suppress && !AnyKeyDown()) {
        g_suppress = false;
    }
    return g_capturing >= 0 || g_suppress;
}

void ResetDefaults() {
    std::lock_guard<std::mutex> lock(g_mutex);
    LoadLocked();
    for (int i = 0; i < kActionCount; ++i) {
        g_codes[i] = Defaults()[i].codes;
        SaveLocked(i);
    }
}

std::string CaptureNote() {
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_captureNote.empty() && SDL_GetTicksNS() - g_captureNoteNs > 5'000'000'000ull) g_captureNote.clear();
    return g_captureNote;
}

} // namespace Keybinds
