using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    // Pagina Controles: que tecla o boton del mando hace cada accion del Wii Remote de lado.
    public sealed partial class MainWindow
    {
        private static readonly IBrush ConflictBrush = new SolidColorBrush(Color.FromRgb(255, 110, 110));
        private Action<string> _keyRejected;

        private Control BuildControls()
        {
            var p = new Page();
            p.Header(Loc.T("Controles", "Controls"), Loc.T("Qué tecla o botón del mando hace cada acción. El juego usa un Wii Remote de lado.",
                "Which key or gamepad button does each action. The game uses a sideways Wii Remote."));
            int style = Math.Max(0, Array.IndexOf(Bindings.Styles, Ls.Get("pad_style", "playstation")));
            var styles = new[] { new Choice("Xbox", 0), new Choice("PlayStation (DualSense / DualShock)", 1), new Choice("Nintendo (Pro Controller)", 2) };
            p.Row(Loc.T("Tipo de mando a editar", "Gamepad type to edit"), Combo(styles, styles[style], v =>
            {
                Ls.Set("pad_style", Bindings.Styles[(int)v]);
                Reshow(ControlsPage);
            }), Loc.T("El port detecta el tipo de mando solo; aquí eliges cuál configurar.", "The port detects the pad type itself; here you pick which one to set up."));
            p.Check(Loc.T("Vibración", "Rumble"), Cfg.GetBool("controller", "rumble", true), v => Write(t => t.SetBool("controller", "rumble", v)));
            if (!int.TryParse(Cfg.GetString("gamepad", "wario_rumble_strength", "100"), out int rumble)) rumble = 100;
            p.Slider(Loc.T("Intensidad de la vibración", "Rumble strength"), Math.Clamp(rumble, 0, 100),
                v => Write(t => { if (v == 100) t.Remove("gamepad", "wario_rumble_strength"); else t.SetString("gamepad", "wario_rumble_strength", v.ToString(CultureInfo.InvariantCulture)); }));

            p.Section(Loc.T("Movimiento (Wii Remote de lado)", "Motion (sideways Wii Remote)"));
            var stickModes = new[]
            {
                new Choice(Loc.T("Apuntar / inclinar según la dirección", "Aim / tilt by direction"), "aim"),
                new Choice(Loc.T("Inclinar con el eje horizontal", "Tilt with the horizontal axis"), "tilt"),
                new Choice(Loc.T("Mover (cruceta)", "Move (D-pad)"), "dpad"),
            };
            string sm = Cfg.GetString("gamepad", "wario_left_stick", "aim");
            p.Row(Loc.T("Stick izquierdo", "Left stick"), Combo(stickModes, stickModes.FirstOrDefault(c => (string)c.Value == sm) ?? stickModes[0],
                v => Write(t => { if ((string)v == "aim") t.Remove("gamepad", "wario_left_stick"); else t.SetString("gamepad", "wario_left_stick", (string)v); })),
                Loc.T("El stick inclina el mando (hasta 85°, para apuntar) y Wario se mueve con la cruceta. «Mover» lo usa como cruceta.",
                      "The stick tilts the remote (up to 85°, for aiming) and Wario moves with the D-pad. “Move” uses it as the D-pad."));
            p.Check(Loc.T("Sensores de movimiento del mando al iniciar", "Gamepad motion sensors at start"), Cfg.GetString("gamepad", "wario_motion", "on") != "off",
                v => Write(t => t.SetString("gamepad", "wario_motion", v ? "on" : "off")),
                Loc.T("DualSense / DualShock 4 / Pro Controller: inclina y sacude el mando como un Wii Remote de lado. En el juego, el panel táctil (o Captura) lo activa y desactiva.",
                      "DualSense / DualShock 4 / Pro Controller: tilt and shake the pad like a sideways Wii Remote. In game, the touchpad (or Capture) toggles it."));
            p.Check(Loc.T("Invertir inclinación de los sensores", "Invert sensor tilt"), Cfg.GetString("gamepad", "wario_motion_invert_roll", "") == "1",
                v => Write(t => { if (v) t.SetString("gamepad", "wario_motion_invert_roll", "1"); else t.Remove("gamepad", "wario_motion_invert_roll"); }));
            p.Check(Loc.T("Invertir cabeceo de los sensores", "Invert sensor pitch"), Cfg.GetString("gamepad", "wario_motion_invert_pitch", "") == "1",
                v => Write(t => { if (v) t.SetString("gamepad", "wario_motion_invert_pitch", "1"); else t.Remove("gamepad", "wario_motion_invert_pitch"); }));

            p.Section(Loc.T("Asignación", "Mapping"));
            p.ControlsHeader(Loc.T("ACCIÓN", "ACTION"), Loc.T("TECLADO", "KEYBOARD"), Loc.T("MANDO", "GAMEPAD"));
            string styleName = Bindings.Styles[style];
            // Una tecla o un boton solo puede tener una accion: se rechaza asignar uno que ya este en uso.
            var keyBoxes = new List<(KeyCapture box, string action)>();
            var padBoxes = new List<(ComboBox box, string action)>();
            var padExtras = new List<(string input, string action)>();
            TextBlock conflictInfo = null;
            void Warn(string msg) { if (conflictInfo != null) { conflictInfo.Text = msg; conflictInfo.Foreground = ConflictBrush; } }
            static string PadValue(ComboBox c) => c.SelectedItem is Choice ch ? (string)ch.Value : "";
            string KeyOwner(KeyCapture self, string name)
            {
                foreach (var kb in keyBoxes)
                    if (kb.box != self && string.Equals(kb.box.Value, name, StringComparison.OrdinalIgnoreCase)) return kb.action;
                return null;
            }
            string PadOwner(ComboBox self, string value)
            {
                if (value.Length == 0) return null;
                foreach (var pb in padBoxes) if (pb.box != self && PadValue(pb.box) == value) return pb.action;
                string selfLabel = padBoxes.FirstOrDefault(pb => pb.box == self).action;
                foreach (var pe in padExtras) if (pe.input == value && pe.action != selfLabel) return pe.action;
                return null;
            }
            void MarkConflicts()
            {
                var dup = new List<string>();
                foreach (var kb in keyBoxes)
                {
                    string other = kb.box.Value.Length > 0 ? KeyOwner(kb.box, kb.box.Value) : null;
                    kb.box.TextColor = other != null ? ConflictBrush : WarioTheme.Text;
                    if (other != null && !dup.Contains(kb.box.Value)) dup.Add(kb.box.Value);
                }
                foreach (var pb in padBoxes)
                {
                    string v = PadValue(pb.box);
                    string other = PadOwner(pb.box, v);
                    pb.box.Foreground = other != null ? ConflictBrush : WarioTheme.Text;
                    string shown = Bindings.PadLabel(v, style);
                    if (other != null && !dup.Contains(shown)) dup.Add(shown);
                }
                if (conflictInfo == null) return;
                if (dup.Count > 0)
                    Warn(Loc.T("Asignadas a más de una acción (en rojo): ", "Assigned to more than one action (in red): ") + string.Join(", ", dup) +
                         Loc.T(". Cambia o borra una de ellas.", ". Change or clear one of them."));
                else conflictInfo.Text = "";
            }
            if (_keyRejected != null) KeyCapture.Rejected -= _keyRejected;
            _keyRejected = Warn;
            KeyCapture.Rejected += _keyRejected;

            foreach (var a in Bindings.Actions)
            {
                var act = a;
                List<string> keys = Bindings.Split(Cfg.GetRaw("keyboard", a.Key) != null ? Cfg.GetString("keyboard", a.Key) : a.KeyboardDefault);
                string padKey = styleName + "_" + a.Key;
                List<string> pads = Bindings.Split(Cfg.GetRaw("gamepad", padKey) != null ? Cfg.GetString("gamepad", padKey) : a.PadDefault[style]);
                string actLabel = Loc.English ? a.En : a.Es;

                var k1 = new KeyCapture(keys.Count > 0 ? keys[0] : "");
                var k2 = new KeyCapture(keys.Count > 1 ? keys[1] : "");
                void SaveKeys()
                {
                    var l = new List<string>();
                    if (k1.Value.Length > 0) l.Add(k1.Value);
                    if (k2.Value.Length > 0 && k2.Value != k1.Value) l.Add(k2.Value);
                    string joined = string.Join(",", l);
                    Write(t => { if (joined == act.KeyboardDefault) t.Remove("keyboard", act.Key); else t.SetString("keyboard", act.Key, joined); });
                }
                foreach (var kc in new[] { k1, k2 })
                {
                    var self = kc;
                    self.Changed += SaveKeys;
                    self.Validate = name =>
                    {
                        string owner = KeyOwner(self, name);
                        return owner == null ? null : Loc.T("La tecla " + name + " ya está asignada a «" + owner + "». Bórrala allí primero (clic derecho).",
                                                            "The " + name + " key is already assigned to “" + owner + "”. Clear it there first (right-click).");
                    };
                    self.Changed += MarkConflicts;
                    keyBoxes.Add((self, actLabel));
                }

                var options = Bindings.PadInputs.Select(i => new Choice(Bindings.PadLabel(i, style), i)).ToList();
                // Entradas que no estan en la lista (escritas a mano) se conservan como una opcion mas.
                ComboBox PadCombo(string value)
                {
                    var opts = options.ToList();
                    if (!opts.Any(o => (string)o.Value == value)) opts.Add(new Choice(value, value));
                    return Combo(opts.ToArray(), opts.First(o => (string)o.Value == value), null, 170);
                }
                var p1 = PadCombo(pads.Count > 0 ? pads[0] : "");
                var p2 = PadCombo(pads.Count > 1 ? pads[1] : "");
                var extra = pads.Skip(2).ToList();  // mas de dos entradas (p. ej. "back,touchpad,leftshoulder,lefttrigger")
                void SavePad()
                {
                    var l = new List<string>();
                    string v1 = PadValue(p1), v2 = PadValue(p2);
                    if (v1.Length > 0) l.Add(v1);
                    if (v2.Length > 0 && v2 != v1) l.Add(v2);
                    foreach (string x in extra) if (!l.Contains(x)) l.Add(x);
                    string joined = string.Join(",", l);
                    Write(t => { if (joined == act.PadDefault[style]) t.Remove("gamepad", padKey); else t.SetString("gamepad", padKey, joined); });
                }
                foreach (var pc in new[] { p1, p2 })
                {
                    var self = pc;
                    string previous = PadValue(self);
                    bool reverting = false;
                    self.SelectionChanged += (s, e) =>
                    {
                        if (reverting) return;
                        string v = PadValue(self);
                        string owner = PadOwner(self, v);
                        if (owner != null)
                        {
                            reverting = true;
                            self.SelectedItem = ((IEnumerable<Choice>)self.ItemsSource).FirstOrDefault(c => (string)c.Value == previous);
                            reverting = false;
                            Warn(Loc.T(Bindings.PadLabel(v, style) + " ya está asignado a «" + owner + "». Quítalo allí primero.",
                                       Bindings.PadLabel(v, style) + " is already assigned to “" + owner + "”. Remove it there first."));
                            return;
                        }
                        previous = v;
                        SavePad();
                        MarkConflicts();
                    };
                    padBoxes.Add((self, actLabel));
                }
                foreach (string x in extra) padExtras.Add((x, actLabel));
                string label = actLabel;
                if (extra.Count > 0) label += "  (+" + string.Join(", ", extra.Select(x => Bindings.PadLabel(x, style))) + ")";
                p.ControlsRow(label, k1, k2, p1, p2);
            }
            p.Info(Loc.T("Clic en una casilla del teclado y pulsa la tecla; clic derecho la borra. Una tecla o botón ya asignado no se puede repetir. Los cambios se aplican al abrir el juego.",
                         "Click a keyboard box and press the key; right-click clears it. A key or button already in use can't be assigned again. Changes apply when the game starts."), WarioTheme.Muted);
            conflictInfo = p.Info(" ", WarioTheme.Muted);
            MarkConflicts();
            p.Buttons(
                (Loc.T("Restaurar teclado", "Reset keyboard"), () =>
                {
                    Write(t => { foreach (var a in Bindings.Actions) t.Remove("keyboard", a.Key); });
                    Reshow(ControlsPage);
                }),
                (Loc.T("Restaurar mando", "Reset gamepad"), () =>
                {
                    Write(t => { foreach (var a in Bindings.Actions) t.Remove("gamepad", styleName + "_" + a.Key); });
                    Reshow(ControlsPage);
                }));
            return p;
        }
    }
}
