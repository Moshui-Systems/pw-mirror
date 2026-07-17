using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Auto-clicker: clica sozinho (botão esquerdo ou direito) num intervalo, na posição
    // atual do cursor ou numa coordenada fixa. Liga/desliga por uma tecla de atalho.
    // Usa SendInput (input real do SO) — não injeta nem debuga nada.
    public class AutoClicker : IDisposable
    {
        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        const int WM_SYSKEYDOWN = 0x0104;

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

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
        static extern bool SetCursorPos(int x, int y);

        // Aumenta a resolução do timer do SO para o Sleep ficar preciso (~1ms).
        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint uPeriod);
        [DllImport("winmm.dll")]
        static extern uint timeEndPeriod(uint uPeriod);

        IntPtr _hook = IntPtr.Zero;
        readonly HookProc _proc;
        readonly Thread _thread;
        volatile bool _disposed;

        // ---- Config ----
        public bool RightButton;          // false = esquerdo, true = direito
        public int IntervalMs = 100;      // tempo entre cliques
        public bool UseFixedPoint;        // false = cursor atual, true = (X,Y) fixo
        public int X, Y;
        public Keys ToggleKey = Keys.F6;

        volatile bool _active;
        public bool Active { get { return _active; } }
        public event Action<bool> StateChanged;

        public AutoClicker()
        {
            _proc = HookProc_;
            IntPtr hMod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);

            _thread = new Thread(ClickLoop) { IsBackground = true };
            _thread.Start();
        }

        public void SetActive(bool on)
        {
            if (_active == on)
                return;
            _active = on;
            StateChanged?.Invoke(on);
        }

        IntPtr HookProc_(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    if ((Keys)data.vkCode == ToggleKey)
                    {
                        SetActive(!_active);
                        System.Media.SystemSounds.Beep.Play();
                        return (IntPtr)1; // consome a tecla de atalho
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void ClickLoop()
        {
            var sw = new Stopwatch();
            bool highRes = false;

            while (!_disposed)
            {
                if (_active)
                {
                    if (!highRes) { timeBeginPeriod(1); highRes = true; }

                    sw.Restart();
                    try
                    {
                        if (UseFixedPoint)
                            SetCursorPos(X, Y);
                        Click(RightButton);
                    }
                    catch { }

                    // Espera o restante do intervalo com precisão (Sleep até faltar
                    // ~2ms, depois spin curto). Lê IntervalMs a cada ciclo (muda ao vivo).
                    int interval = Math.Max(1, IntervalMs);
                    while (!_disposed && _active)
                    {
                        long elapsed = sw.ElapsedMilliseconds;
                        if (elapsed >= interval) break;
                        if (interval - elapsed > 2)
                            Thread.Sleep(1);
                        else
                            Thread.SpinWait(150);
                    }
                }
                else
                {
                    if (highRes) { timeEndPeriod(1); highRes = false; }
                    Thread.Sleep(15);
                }
            }

            if (highRes)
                timeEndPeriod(1);
        }

        void Click(bool right)
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi.dwFlags = right ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            inputs[1].type = INPUT_MOUSE;
            inputs[1].mi.dwFlags = right ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public void Dispose()
        {
            _disposed = true;
            _active = false;
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
