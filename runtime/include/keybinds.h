#pragma once

#include <cstddef>
#include <string>

// NSMBW: the keyboard's sideways Wii Remote, one or two keys per control, changeable from the
// game's settings window (Keybinds tab) and saved to Config.toml's [keyboard] table as SDL
// scancode names ("jump = \"X,Space\"").
namespace Keybinds {

enum Action : int {
    kUp,
    kDown,
    kLeft,
    kRight,
    kJump,      // 2
    kRun,       // 1
    kA,
    kShake,
    kTiltLeft,
    kTiltRight,
    kPlus,
    kMinus,
    kActionCount,
};

const char* ActionName(int action);
// "X / Space", or "None".
std::string KeyLabel(int action);
// Any of the action's keys held (false while a key is being captured, or until it is let go).
bool Held(int action);

// Waits for the next key pressed and binds it to `action` alone; Escape cancels.
void BeginCapture(int action);
// The action waiting for a key, or -1.
int Capturing();
// Polls the keyboard during a capture; true once it has ended (bound or cancelled).
bool PollCapture();
// WARIO: why the last key picked was refused (already used by another action), for a few seconds; or "".
std::string CaptureNote();
// Keys held from a capture are ignored until they are released.
bool Suppressed();
void ResetDefaults();

} // namespace Keybinds
