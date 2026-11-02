using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarioLauncher.Core
{
    // Editor minimo de Config.toml: cambia solo las claves tocadas y deja intactas las demas lineas,
    // los comentarios y lo que el juego escribe por su cuenta (tamano de ventana, F10, etc.).
    public sealed class TomlFile
    {
        private readonly string _path;
        private List<string> _lines = new List<string>();

        public TomlFile(string path) { _path = path; Reload(); }

        public string Path => _path;

        public void Reload()
        {
            _lines = new List<string>();
            if (File.Exists(_path))
            {
                string text = File.ReadAllText(_path, Encoding.UTF8);
                if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
                _lines.AddRange(text.Replace("\r\n", "\n").Split('\n'));
                while (_lines.Count > 0 && _lines[_lines.Count - 1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
            }
        }

        public void Save()
        {
            string dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (string l in _lines) sb.Append(l).Append(Environment.NewLine);
            File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string SectionOf(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("[") && !t.StartsWith("[[") && t.Contains("]"))
                return t.Substring(1, t.IndexOf(']') - 1).Trim();
            return null;
        }

        private static bool KeyOf(string line, out string key, out string value)
        {
            key = null; value = null;
            string t = line.Trim();
            if (t.Length == 0 || t[0] == '#' || t[0] == '[') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim().Trim('"');
            value = StripComment(t.Substring(eq + 1).Trim());
            return true;
        }

        private static string StripComment(string v)
        {
            bool inStr = false; char quote = '\0';
            for (int i = 0; i < v.Length; i++)
            {
                char c = v[i];
                if (inStr)
                {
                    if (c == '\\' && quote == '"') { i++; continue; }
                    if (c == quote) inStr = false;
                }
                else if (c == '"' || c == '\'') { inStr = true; quote = c; }
                else if (c == '#') return v.Substring(0, i).Trim();
            }
            return v.Trim();
        }

        private int Find(string section, string key, out int sectionEnd, out int sectionStart)
        {
            string cur = "";
            sectionStart = -1; sectionEnd = -1;
            int found = -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                string s = SectionOf(_lines[i]);
                if (s != null)
                {
                    if (cur == section && sectionStart >= 0 && sectionEnd < 0) sectionEnd = i;
                    cur = s;
                    if (s == section && sectionStart < 0) sectionStart = i;
                    continue;
                }
                if (cur == section && KeyOf(_lines[i], out string k, out _) && k == key) found = i;
            }
            if (sectionStart >= 0 && sectionEnd < 0) sectionEnd = _lines.Count;
            return found;
        }

        public string GetRaw(string section, string key)
        {
            int idx = Find(section, key, out _, out _);
            if (idx < 0) return null;
            KeyOf(_lines[idx], out _, out string v);
            return v;
        }

        public void SetRaw(string section, string key, string literal)
        {
            int idx = Find(section, key, out int end, out int start);
            string line = key + " = " + literal;
            if (idx >= 0) { _lines[idx] = line; return; }
            if (start < 0)
            {
                if (_lines.Count > 0 && _lines[_lines.Count - 1].Trim().Length != 0) _lines.Add("");
                _lines.Add("[" + section + "]");
                _lines.Add(line);
                return;
            }
            int insert = end;
            while (insert - 1 > start && _lines[insert - 1].Trim().Length == 0) insert--;
            _lines.Insert(insert, line);
        }

        public void Remove(string section, string key)
        {
            int idx = Find(section, key, out _, out _);
            if (idx >= 0) _lines.RemoveAt(idx);
        }

        // ---- tipos ----
        public string GetString(string section, string key, string fallback = "")
        {
            string v = GetRaw(section, key);
            if (v == null) return fallback;
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < v.Length - 1; i++)
                {
                    if (v[i] == '\\' && i + 1 < v.Length - 1)
                    {
                        char n = v[++i];
                        sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                    }
                    else sb.Append(v[i]);
                }
                return sb.ToString();
            }
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') return v.Substring(1, v.Length - 2);
            return v;
        }

        public void SetString(string section, string key, string value)
        {
            string esc = (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
            SetRaw(section, key, "\"" + esc + "\"");
        }

        public bool GetBool(string section, string key, bool fallback)
        {
            string v = GetRaw(section, key);
            if (v == "true") return true;
            if (v == "false") return false;
            return fallback;
        }

        public void SetBool(string section, string key, bool value) => SetRaw(section, key, value ? "true" : "false");

        public double GetDouble(string section, string key, double fallback)
        {
            string v = GetRaw(section, key);
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }

        public void SetDouble(string section, string key, double value)
        {
            string s = value.ToString("0.0###", CultureInfo.InvariantCulture);
            SetRaw(section, key, s);
        }

        public int GetInt(string section, string key, int fallback)
        {
            string v = GetRaw(section, key);
            return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : fallback;
        }

        public void SetInt(string section, string key, int value) => SetRaw(section, key, value.ToString(CultureInfo.InvariantCulture));
    }

    // Ajustes propios del lanzador (no los lee el juego): UserData\launcher.ini, clave=valor.
    public sealed class LauncherSettings
    {
        private readonly string _path;
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public LauncherSettings(string path)
        {
            _path = path;
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) _values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
        }

        public string Get(string key, string fallback) => _values.TryGetValue(key, out string v) ? v : fallback;
        public bool GetBool(string key, bool fallback) => _values.TryGetValue(key, out string v) ? v == "1" : fallback;
        public void Set(string key, string value) { _values[key] = value; Save(); }
        public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path));
                var lines = new List<string>();
                foreach (var kv in _values) lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch { }
        }
    }
}
