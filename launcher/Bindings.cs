using System.Collections.Generic;
using System.Windows.Forms;

namespace WarioLauncher
{
    // Acciones del mando virtual (Wii Remote de lado), en el orden de Keybinds del runtime.
    // Config.toml: [keyboard] <accion> = "X,Space" (nombres SDL) y [gamepad] <estilo>_<accion> = "a,b".
    public static class Bindings
    {
        public sealed class Action
        {
            public string Key, Es, En, KeyboardDefault;
            public string[] PadDefault; // xbox, playstation, nintendo
        }

        public static readonly Action[] Actions =
        {
            A("up", "Arriba", "Up", "Up", "dpup", "dpup", "dpup"),
            A("down", "Abajo", "Down", "Down", "dpdown", "dpdown", "dpdown"),
            A("left", "Izquierda", "Left", "Left", "dpleft", "dpleft", "dpleft"),
            A("right", "Derecha", "Right", "Right", "dpright", "dpright", "dpright"),
            A("jump", "Saltar (botón 2)", "Jump (2 button)", "X,Space", "a", "a", "a,b"),
            A("run", "Correr / Atacar (botón 1)", "Run / Attack (1 button)", "Z,Left Shift", "b,x", "b,x", "x"),
            A("a", "Botón A", "A button", "A", "y", "y", "y"),
            A("shake", "Sacudir", "Shake", "C,Left Ctrl", "lefttrigger,righttrigger", "lefttrigger,righttrigger", "lefttrigger,righttrigger"),
            A("tilt_left", "Inclinar a la izquierda (teclado: bajar ángulo)", "Tilt left (keyboard: lower angle)", "Q", "leftshoulder", "leftshoulder", "leftshoulder"),
            A("tilt_right", "Inclinar a la derecha (teclado: subir ángulo)", "Tilt right (keyboard: raise angle)", "E", "rightshoulder", "rightshoulder", "rightshoulder"),
            A("plus", "+ (Pausa)", "+ (Pause)", "Return,Escape", "start", "start", "start"),
            A("minus", "− (Menos)", "− (Minus)", "-,Tab", "back", "back", "back"),
        };

        private static Action A(string key, string es, string en, string kb, string xbox, string ps, string nin) =>
            new Action { Key = key, Es = es, En = en, KeyboardDefault = kb, PadDefault = new[] { xbox, ps, nin } };

        public static readonly string[] Styles = { "xbox", "playstation", "nintendo" };

        // Entradas del mando que entiende el runtime (SDL), con su nombre segun el tipo de mando.
        public static readonly string[] PadInputs =
        {
            "", "a", "b", "x", "y", "leftshoulder", "rightshoulder", "lefttrigger", "righttrigger",
            "leftstick", "rightstick", "start", "back", "guide", "touchpad", "misc1",
            "dpup", "dpdown", "dpleft", "dpright",
            "leftx-", "leftx+", "lefty-", "lefty+", "rightx-", "rightx+", "righty-", "righty+",
        };

        public static string PadLabel(string input, int style)
        {
            bool ps = style == 1, nin = style == 2;
            switch (input)
            {
                case "": return Loc.T("(ninguno)", "(none)");
                case "a": return ps ? "✕ Cruz" : nin ? "B (abajo)" : "A";
                case "b": return ps ? "○ Círculo" : nin ? "A (derecha)" : "B";
                case "x": return ps ? "□ Cuadrado" : nin ? "Y (izquierda)" : "X";
                case "y": return ps ? "△ Triángulo" : nin ? "X (arriba)" : "Y";
                case "leftshoulder": return ps ? "L1" : nin ? "L" : "LB";
                case "rightshoulder": return ps ? "R1" : nin ? "R" : "RB";
                case "lefttrigger": return ps ? "L2" : nin ? "ZL" : "LT";
                case "righttrigger": return ps ? "R2" : nin ? "ZR" : "RT";
                case "leftstick": return ps ? "L3" : Loc.T("Stick izq. (clic)", "Left stick (click)");
                case "rightstick": return ps ? "R3" : Loc.T("Stick der. (clic)", "Right stick (click)");
                case "start": return ps ? "Options" : nin ? "+" : "Start / Menu";
                case "back": return ps ? "Create / Share" : nin ? "−" : "Back / View";
                case "guide": return ps ? "PS" : nin ? "Home" : "Guide";
                case "touchpad": return Loc.T("Panel táctil", "Touchpad");
                case "misc1": return ps ? Loc.T("Micrófono", "Mic") : nin ? Loc.T("Captura", "Capture") : "Share";
                case "dpup": return Loc.T("Cruceta ↑", "D-pad ↑");
                case "dpdown": return Loc.T("Cruceta ↓", "D-pad ↓");
                case "dpleft": return Loc.T("Cruceta ←", "D-pad ←");
                case "dpright": return Loc.T("Cruceta →", "D-pad →");
                case "leftx-": return Loc.T("Stick izq. ←", "Left stick ←");
                case "leftx+": return Loc.T("Stick izq. →", "Left stick →");
                case "lefty-": return Loc.T("Stick izq. ↑", "Left stick ↑");
                case "lefty+": return Loc.T("Stick izq. ↓", "Left stick ↓");
                case "rightx-": return Loc.T("Stick der. ←", "Right stick ←");
                case "rightx+": return Loc.T("Stick der. →", "Right stick →");
                case "righty-": return Loc.T("Stick der. ↑", "Right stick ↑");
                case "righty+": return Loc.T("Stick der. ↓", "Right stick ↓");
            }
            return input;
        }

        // Tecla de Windows -> nombre de tecla de SDL (SDL_GetScancodeFromName).
        public static string SdlKeyName(Keys key)
        {
            Keys k = key & Keys.KeyCode;
            if (k >= Keys.A && k <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Keypad " + (k - Keys.NumPad0);
            if (k >= Keys.F1 && k <= Keys.F12) return "F" + (k - Keys.F1 + 1);
            switch (k)
            {
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Space: return "Space";
                case Keys.Return: return "Return";
                case Keys.Escape: return "Escape";
                case Keys.Tab: return "Tab";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.LShiftKey: return "Left Shift";
                case Keys.RShiftKey: return "Right Shift";
                case Keys.ShiftKey: return "Left Shift";
                case Keys.LControlKey: return "Left Ctrl";
                case Keys.RControlKey: return "Right Ctrl";
                case Keys.ControlKey: return "Left Ctrl";
                case Keys.LMenu: return "Left Alt";
                case Keys.RMenu: return "Right Alt";
                case Keys.Menu: return "Left Alt";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return null; // la coma separa teclas en Config.toml
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.Oemtilde: return "`";
                case Keys.Add: return "Keypad +";
                case Keys.Subtract: return "Keypad -";
                case Keys.Multiply: return "Keypad *";
                case Keys.Divide: return "Keypad /";
                case Keys.Decimal: return "Keypad .";
                case Keys.CapsLock: return "CapsLock";
            }
            return null;
        }

        // F10, F11 y F12 los usa el juego (menu, ventana, captura).
        public static bool Reserved(string sdlName) => sdlName == "F10" || sdlName == "F11";

        public static List<string> Split(string text)
        {
            var list = new List<string>();
            foreach (string part in (text ?? "").Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }
    }
}
