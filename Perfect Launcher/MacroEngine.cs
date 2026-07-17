using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    public enum StepType { KeyPress, KeyDown, KeyUp, Delay, MouseClick, MouseMove, Text }
    public enum TriggerMode { Once, Repeat, Toggle, Hold }
    public enum MacroButton { Left, Right, Middle }

    // Um passo do macro.
    public class MacroStep
    {
        public StepType Type;
        public Keys Key;            // KeyPress/KeyDown/KeyUp
        public int DelayMs;         // Delay
        public MacroButton Button;  // MouseClick
        public bool UseCursor = true; // MouseClick/MouseMove: no cursor atual?
        public int X, Y;            // MouseClick/MouseMove (se !UseCursor)
        public string Text = "";    // Text

        public string Describe()
        {
            switch (Type)
            {
                case StepType.KeyPress: return "Tecla: " + Key;
                case StepType.KeyDown: return "Segurar: " + Key;
                case StepType.KeyUp: return "Soltar: " + Key;
                case StepType.Delay: return "Espera: " + DelayMs + " ms";
                case StepType.MouseClick: return "Clique " + BtnName(Button) + (UseCursor ? " (no cursor)" : " em " + X + "," + Y);
                case StepType.MouseMove: return "Mover mouse para " + (UseCursor ? "(cursor)" : X + "," + Y);
                case StepType.Text: return "Digitar: \"" + (Text.Length > 24 ? Text.Substring(0, 24) + "…" : Text) + "\"";
            }
            return "?";
        }

        static string BtnName(MacroButton b) { return b == MacroButton.Left ? "esquerdo" : b == MacroButton.Right ? "direito" : "meio"; }

        // Serialização compacta (uma linha "step=...")
        public string Serialize()
        {
            switch (Type)
            {
                case StepType.KeyPress: return "k:" + (int)Key;
                case StepType.KeyDown: return "d:" + (int)Key;
                case StepType.KeyUp: return "u:" + (int)Key;
                case StepType.Delay: return "w:" + DelayMs;
                case StepType.MouseClick: return "c:" + (int)Button + "," + (UseCursor ? "cur" : X + "," + Y);
                case StepType.MouseMove: return "m:" + (UseCursor ? "cur" : X + "," + Y);
                case StepType.Text: return "t:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(Text ?? ""));
            }
            return "";
        }

        public static MacroStep Deserialize(string s)
        {
            try
            {
                int colon = s.IndexOf(':');
                if (colon < 0) return null;
                string tag = s.Substring(0, colon);
                string val = s.Substring(colon + 1);
                var st = new MacroStep();
                switch (tag)
                {
                    case "k": st.Type = StepType.KeyPress; st.Key = (Keys)int.Parse(val); return st;
                    case "d": st.Type = StepType.KeyDown; st.Key = (Keys)int.Parse(val); return st;
                    case "u": st.Type = StepType.KeyUp; st.Key = (Keys)int.Parse(val); return st;
                    case "w": st.Type = StepType.Delay; st.DelayMs = int.Parse(val); return st;
                    case "c":
                        st.Type = StepType.MouseClick;
                        var cp = val.Split(',');
                        st.Button = (MacroButton)int.Parse(cp[0]);
                        if (cp.Length >= 3 && cp[1] != "cur") { st.UseCursor = false; st.X = int.Parse(cp[1]); st.Y = int.Parse(cp[2]); }
                        else if (cp.Length >= 2 && cp[1] != "cur") { st.UseCursor = false; st.X = int.Parse(cp[1]); st.Y = cp.Length > 2 ? int.Parse(cp[2]) : 0; }
                        else st.UseCursor = true;
                        return st;
                    case "m":
                        st.Type = StepType.MouseMove;
                        if (val == "cur") st.UseCursor = true;
                        else { st.UseCursor = false; var mp = val.Split(','); st.X = int.Parse(mp[0]); st.Y = int.Parse(mp[1]); }
                        return st;
                    case "t": st.Type = StepType.Text; st.Text = Encoding.UTF8.GetString(Convert.FromBase64String(val)); return st;
                }
            }
            catch { }
            return null;
        }
    }

    public class Macro
    {
        public string Name = "Novo macro";
        public Keys Trigger = Keys.None;
        public TriggerMode Mode = TriggerMode.Once;
        public int RepeatCount = 1;
        public bool AllAccounts = false;
        public List<MacroStep> Steps = new List<MacroStep>();

        // Estado em runtime (não salvo)
        internal volatile bool Running;
    }

    // Executa macros: escuta as teclas de atalho e reproduz a sequência (na conta atual
    // ou em todas, via foco + SendInput). Suporta modos: uma vez, repetir N, alternar
    // (loop) e segurar (roda enquanto a tecla estiver pressionada).
    public class MacroEngine : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;
        const uint LLKHF_INJECTED = 0x10;

        const uint INPUT_MOUSE = 0;
        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_UNICODE = 0x0004;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
        const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020, MOUSEEVENTF_MIDDLEUP = 0x0040;
        const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);
        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        IntPtr _hook = IntPtr.Zero;
        readonly HookProc _proc;
        readonly Func<List<IntPtr>> _getTargets;

        public List<Macro> Macros = new List<Macro>();
        public bool Enabled = true;

        public MacroEngine(Func<List<IntPtr>> getTargets)
        {
            _getTargets = getTargets;
            _proc = HookCallback;
            IntPtr hMod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Enabled)
            {
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if (down || up)
                {
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    if ((data.flags & LLKHF_INJECTED) == 0)
                    {
                        var key = (Keys)data.vkCode;
                        var macro = Macros.FirstOrDefault(m => m.Trigger == key && m.Steps.Count > 0);
                        if (macro != null)
                        {
                            if (down) OnTriggerDown(macro);
                            else OnTriggerUp(macro);
                            return (IntPtr)1; // consome a tecla de atalho
                        }
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void OnTriggerDown(Macro m)
        {
            switch (m.Mode)
            {
                case TriggerMode.Once:
                case TriggerMode.Repeat:
                    if (!m.Running)
                        RunTimes(m, m.Mode == TriggerMode.Repeat ? Math.Max(1, m.RepeatCount) : 1);
                    break;
                case TriggerMode.Toggle:
                    if (m.Running) m.Running = false;    // desliga o loop
                    else StartLoop(m);
                    break;
                case TriggerMode.Hold:
                    if (!m.Running) StartLoop(m);
                    break;
            }
        }

        void OnTriggerUp(Macro m)
        {
            if (m.Mode == TriggerMode.Hold)
                m.Running = false;
        }

        void RunTimes(Macro m, int times)
        {
            m.Running = true;
            Task.Run(() =>
            {
                try { for (int i = 0; i < times && m.Running; i++) Execute(m); }
                catch { }
                finally { m.Running = false; }
            });
        }

        void StartLoop(Macro m)
        {
            m.Running = true;
            Task.Run(() =>
            {
                try { while (m.Running) { Execute(m); Thread.Sleep(5); } }
                catch { }
                finally { m.Running = false; }
            });
        }

        void Execute(Macro m)
        {
            if (m.AllAccounts)
            {
                List<IntPtr> targets;
                try { targets = _getTargets() ?? new List<IntPtr>(); }
                catch { return; }

                IntPtr original = GetForegroundWindow();
                foreach (var h in targets)
                {
                    if (h == IntPtr.Zero) continue;
                    ShowWindowAsync(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    Thread.Sleep(60);
                    PlaySteps(m);
                }
                if (original != IntPtr.Zero) SetForegroundWindow(original);
            }
            else
            {
                PlaySteps(m);
            }
        }

        void PlaySteps(Macro m)
        {
            foreach (var st in m.Steps)
            {
                if (!m.Running && (m.Mode == TriggerMode.Toggle || m.Mode == TriggerMode.Hold))
                    return;
                DoStep(st);
            }
        }

        void DoStep(MacroStep st)
        {
            switch (st.Type)
            {
                case StepType.KeyPress: SendKey((ushort)st.Key, false); Thread.Sleep(15); SendKey((ushort)st.Key, true); break;
                case StepType.KeyDown: SendKey((ushort)st.Key, false); break;
                case StepType.KeyUp: SendKey((ushort)st.Key, true); break;
                case StepType.Delay: Thread.Sleep(Math.Max(0, Math.Min(st.DelayMs, 600000))); break;
                case StepType.MouseMove: if (!st.UseCursor) SetCursorPos(st.X, st.Y); break;
                case StepType.MouseClick:
                    if (!st.UseCursor) SetCursorPos(st.X, st.Y);
                    ClickMouse(st.Button);
                    break;
                case StepType.Text:
                    foreach (char c in st.Text ?? "") { SendChar(c); Thread.Sleep(5); }
                    break;
            }
            Thread.Sleep(5);
        }

        void SendKey(ushort vk, bool up)
        {
            var inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].u.ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = up ? KEYEVENTF_KEYUP : 0 };
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        void SendChar(char c)
        {
            var inp = new INPUT[2];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].u.ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE };
            inp[1].type = INPUT_KEYBOARD;
            inp[1].u.ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP };
            SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        void ClickMouse(MacroButton b)
        {
            uint down = b == MacroButton.Left ? MOUSEEVENTF_LEFTDOWN : b == MacroButton.Right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_MIDDLEDOWN;
            uint up = b == MacroButton.Left ? MOUSEEVENTF_LEFTUP : b == MacroButton.Right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_MIDDLEUP;
            var inp = new INPUT[2];
            inp[0].type = INPUT_MOUSE; inp[0].u.mi = new MOUSEINPUT { dwFlags = down };
            inp[1].type = INPUT_MOUSE; inp[1].u.mi = new MOUSEINPUT { dwFlags = up };
            SendInput(2, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---- Persistência (formato tipo INI, robusto a caracteres especiais) ----
        public static List<Macro> Load(string path)
        {
            var list = new List<Macro>();
            try
            {
                if (!File.Exists(path)) return list;
                Macro cur = null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line == "[macro]") { cur = new Macro { Steps = new List<MacroStep>() }; list.Add(cur); continue; }
                    if (cur == null) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    switch (k)
                    {
                        case "name": cur.Name = v; break;
                        case "trigger": cur.Trigger = (Keys)int.Parse(v); break;
                        case "mode": cur.Mode = (TriggerMode)int.Parse(v); break;
                        case "repeat": cur.RepeatCount = int.Parse(v); break;
                        case "target": cur.AllAccounts = v == "1"; break;
                        case "step": var st = MacroStep.Deserialize(v); if (st != null) cur.Steps.Add(st); break;
                    }
                }
            }
            catch { }
            return list;
        }

        public static void Save(string path, List<Macro> macros)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var m in macros)
                {
                    sb.AppendLine("[macro]");
                    sb.AppendLine("name=" + (m.Name ?? "").Replace("\r", " ").Replace("\n", " "));
                    sb.AppendLine("trigger=" + (int)m.Trigger);
                    sb.AppendLine("mode=" + (int)m.Mode);
                    sb.AppendLine("repeat=" + m.RepeatCount);
                    sb.AppendLine("target=" + (m.AllAccounts ? 1 : 0));
                    foreach (var st in m.Steps)
                        sb.AppendLine("step=" + st.Serialize());
                    sb.AppendLine();
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        public void Dispose()
        {
            foreach (var m in Macros) m.Running = false;
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
