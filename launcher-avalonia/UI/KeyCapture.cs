using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    // Casilla para capturar una tecla: clic y pulsar; clic derecho la borra. La ventana le entrega la tecla
    // (MainWindow, evento KeyDown en tunel, antes de que las flechas muevan el foco).
    public sealed class KeyCapture : Border
    {
        private readonly TextBlock _text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        private bool _waiting;

        public string Value { get; private set; }
        public event Action Changed;

        // Devuelve un aviso si la tecla ya esta asignada a otra casilla (entonces no se asigna), o null.
        public Func<string, string> Validate;
        public static event Action<string> Rejected;
        public static KeyCapture Active;

        public IBrush TextColor { set => _text.Foreground = value; }

        public KeyCapture(string value)
        {
            Value = value ?? "";
            Height = 28;
            BorderThickness = new Thickness(1);
            BorderBrush = WarioTheme.FieldBorder;
            CornerRadius = new CornerRadius(4);
            Cursor = new Cursor(StandardCursorType.Hand);
            _text.Foreground = WarioTheme.Text;
            Child = _text;
            Refresh0();
            PointerPressed += OnPressed;
            DetachedFromVisualTree += (s, e) => { if (Active == this) Active = null; };
        }

        private void Refresh0()
        {
            _text.Text = _waiting ? Loc.T("Pulsa una tecla…", "Press a key…") : (Value.Length > 0 ? Value : "—");
            Background = _waiting ? WarioTheme.Purple : WarioTheme.Field;
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            if (Active != null && Active != this) { Active._waiting = false; Active.Refresh0(); }
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                Active = null; _waiting = false; Value = ""; Refresh0(); Changed?.Invoke();
                return;
            }
            _waiting = true;
            Active = this;
            Refresh0();
            e.Handled = true;
        }

        public void Deliver(Key key)
        {
            Active = null;
            _waiting = false;
            string name = SdlKeyName(key);
            if (name != null && !Bindings.Reserved(name))
            {
                string conflict = Validate?.Invoke(name);
                if (conflict != null) Rejected?.Invoke(conflict);
                else if (name != Value) { Value = name; Changed?.Invoke(); }
            }
            Refresh0();
        }

        // Tecla de Avalonia -> nombre de tecla de SDL (SDL_GetScancodeFromName), el que guarda Config.toml.
        public static string SdlKeyName(Key k)
        {
            if (k >= Key.A && k <= Key.Z) return ((char)('A' + (k - Key.A))).ToString();
            if (k >= Key.D0 && k <= Key.D9) return ((char)('0' + (k - Key.D0))).ToString();
            if (k >= Key.NumPad0 && k <= Key.NumPad9) return "Keypad " + (k - Key.NumPad0);
            if (k >= Key.F1 && k <= Key.F12) return "F" + (k - Key.F1 + 1);
            switch (k)
            {
                case Key.Up: return "Up";
                case Key.Down: return "Down";
                case Key.Left: return "Left";
                case Key.Right: return "Right";
                case Key.Space: return "Space";
                case Key.Enter: return "Return";
                case Key.Escape: return "Escape";
                case Key.Tab: return "Tab";
                case Key.Back: return "Backspace";
                case Key.Delete: return "Delete";
                case Key.Insert: return "Insert";
                case Key.Home: return "Home";
                case Key.End: return "End";
                case Key.PageUp: return "PageUp";
                case Key.PageDown: return "PageDown";
                case Key.LeftShift: return "Left Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.LeftAlt: return "Left Alt";
                case Key.RightAlt: return "Right Alt";
                case Key.OemMinus: return "-";
                case Key.OemPlus: return "=";
                case Key.OemComma: return null; // la coma separa teclas en Config.toml
                case Key.OemPeriod: return ".";
                case Key.OemQuestion: return "/";
                case Key.OemSemicolon: return ";";
                case Key.OemQuotes: return "'";
                case Key.OemOpenBrackets: return "[";
                case Key.OemCloseBrackets: return "]";
                case Key.OemPipe: return "\\";
                case Key.OemTilde: return "`";
                case Key.Add: return "Keypad +";
                case Key.Subtract: return "Keypad -";
                case Key.Multiply: return "Keypad *";
                case Key.Divide: return "Keypad /";
                case Key.Decimal: return "Keypad .";
                case Key.CapsLock: return "CapsLock";
            }
            return null;
        }
    }
}
