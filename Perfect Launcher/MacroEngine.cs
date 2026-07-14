using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Um passo do macro: ou uma tecla, ou um delay em ms.
    public class MacroStep
    {
        public Keys Key;
        public int DelayMs;

        public bool IsDelay { get { return DelayMs > 0; } }

        public string ToToken()
        {
            return IsDelay ? DelayMs.ToString() : Key.ToString();
        }

        public static MacroStep Parse(string tok)
        {
            int ms;
            if (int.TryParse(tok, out ms) && ms > 0)
                return new MacroStep { DelayMs = ms };

            Keys k;
            if (Enum.TryParse(tok, true, out k) && k != Keys.None)
                return new MacroStep { Key = k };

            return null;
        }
    }

    public class Macro
    {
        public string Name = "Novo macro";
        public Keys Trigger = Keys.None;
        public bool AllAccounts = true;
        public List<MacroStep> Steps = new List<MacroStep>();

        public string SequenceText
        {
            get { return string.Join(" ", Steps.Select(s => s.ToToken())); }
        }

        public void SetSequenceFromText(string text)
        {
            Steps.Clear();
            if (string.IsNullOrWhiteSpace(text))
                return;
            foreach (var tok in text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var st = MacroStep.Parse(tok);
                if (st != null)
                    Steps.Add(st);
            }
        }

        public string Serialize()
        {
            return Name.Replace(";", " ") + ";" + (int)Trigger + ";" + (AllAccounts ? 1 : 0) + ";" + SequenceText;
        }

        public static Macro Deserialize(string line)
        {
            try
            {
                var parts = line.Split(new[] { ';' }, 4);
                if (parts.Length < 4)
                    return null;

                var m = new Macro { Name = parts[0], Trigger = (Keys)int.Parse(parts[1]), AllAccounts = parts[2] == "1" };
                m.SetSequenceFromText(parts[3]);
                return m;
            }
            catch
            {
                return null;
            }
        }
    }

    // Executa macros: escuta as teclas de atalho e reproduz a sequência na conta
    // atual ou em todas (focando cada janela e mandando input real via SendInput,
    // o que funciona mesmo com o streamer mode do PW desligado).
    public class MacroEngine : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;
        const uint LLKHF_INJECTED = 0x10;

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

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
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        IntPtr _hook = IntPtr.Zero;
        readonly HookProc _proc;
        readonly Func<List<IntPtr>> _getTargets;
        volatile bool _running;

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
            if (nCode >= 0 && Enabled && !_running)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                    // Ignora input que nós mesmos geramos (SendInput)
                    if ((data.flags & LLKHF_INJECTED) == 0)
                    {
                        var key = (Keys)data.vkCode;
                        var macro = Macros.FirstOrDefault(m => m.Trigger == key && m.Steps.Count > 0);
                        if (macro != null)
                        {
                            RunMacro(macro);
                            return (IntPtr)1; // consome a tecla de atalho
                        }
                    }
                }
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void RunMacro(Macro macro)
        {
            if (_running)
                return;
            _running = true;
            Task.Run(() =>
            {
                try { Execute(macro); }
                catch { }
                finally { _running = false; }
            });
        }

        void Execute(Macro macro)
        {
            if (macro.AllAccounts)
            {
                List<IntPtr> targets;
                try { targets = _getTargets() ?? new List<IntPtr>(); }
                catch { return; }

                IntPtr original = GetForegroundWindow();
                foreach (var h in targets)
                {
                    if (h == IntPtr.Zero)
                        continue;
                    ShowWindowAsync(h, SW_RESTORE);
                    SetForegroundWindow(h);
                    Thread.Sleep(60);
                    PlaySteps(macro);
                }
                if (original != IntPtr.Zero)
                    SetForegroundWindow(original);
            }
            else
            {
                // Conta atual: a janela em foco já recebe o SendInput
                PlaySteps(macro);
            }
        }

        void PlaySteps(Macro macro)
        {
            foreach (var st in macro.Steps)
            {
                if (st.IsDelay)
                {
                    Thread.Sleep(Math.Min(st.DelayMs, 10000));
                    continue;
                }
                SendKey((ushort)st.Key, false);
                Thread.Sleep(20);
                SendKey((ushort)st.Key, true);
                Thread.Sleep(20);
            }
        }

        void SendKey(ushort vk, bool up)
        {
            var inp = new INPUT[1];
            inp[0].type = INPUT_KEYBOARD;
            inp[0].U.ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, 0),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
        }

        // ---- Persistência ----
        public static List<Macro> Load(string path)
        {
            var list = new List<Macro>();
            try
            {
                if (File.Exists(path))
                    foreach (var line in File.ReadAllLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;
                        var m = Macro.Deserialize(line);
                        if (m != null)
                            list.Add(m);
                    }
            }
            catch { }
            return list;
        }

        public static void Save(string path, List<Macro> macros)
        {
            try
            {
                File.WriteAllLines(path, macros.Select(m => m.Serialize()).ToArray());
            }
            catch { }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
