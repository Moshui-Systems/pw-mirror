using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Perfect_Launcher
{
    // Espelha (mirror) TODOS os comandos de teclado e mouse da janela ativa (a "master")
    // para todas as outras janelas de cliente abertas. Usa hooks de baixo nível para
    // capturar o input real e PostMessage para reenviá-lo às demais janelas — a mesma
    // técnica que o recurso de Combo já usa (SendKeyPress), só que para todas de uma vez.
    //
    // Observação: PostMessage entrega mensagens diretamente na fila da janela alvo e NÃO
    // passa pelos hooks WH_*_LL, então não existe loop de realimentação.
    public class InputMirror : IDisposable
    {
        // ---- Tipos de hook ----
        const int WH_KEYBOARD_LL = 13;
        const int WH_MOUSE_LL = 14;

        // ---- Mensagens de teclado ----
        const int WM_KEYDOWN = 0x0100;
        const int WM_KEYUP = 0x0101;
        const int WM_SYSKEYDOWN = 0x0104;
        const int WM_SYSKEYUP = 0x0105;

        // ---- Mensagens de mouse ----
        const int WM_MOUSEMOVE = 0x0200;
        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;
        const int WM_RBUTTONDOWN = 0x0204;
        const int WM_RBUTTONUP = 0x0205;
        const int WM_MBUTTONDOWN = 0x0207;
        const int WM_MBUTTONUP = 0x0208;
        const int WM_MOUSEWHEEL = 0x020A;

        // Flags de estado dos botões (wParam das mensagens de mouse)
        const uint MK_LBUTTON = 0x0001;
        const uint MK_RBUTTON = 0x0002;
        const uint MK_MBUTTON = 0x0010;

        // LLKHF_EXTENDED (bit 0 do campo flags do KBDLLHOOKSTRUCT)
        const uint LLKHF_EXTENDED = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", EntryPoint = "PostMessageA")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // ---- Estado ----
        IntPtr _kbHook = IntPtr.Zero;
        IntPtr _mouseHook = IntPtr.Zero;
        readonly HookProc _kbProc;    // manter as delegates vivas (o GC não sabe do SetWindowsHookEx)
        readonly HookProc _mouseProc;

        // Fornecido pela Form1: retorna os handles das janelas de cliente abertas no momento.
        readonly Func<List<IntPtr>> _getTargets;

        // Cache dos alvos, atualizado no máximo a cada 500ms para não pesar em cada movimento de mouse.
        List<IntPtr> _cache = new List<IntPtr>();
        int _lastRefresh = -100000;

        /// <summary>Liga/desliga o espelhamento.</summary>
        public bool Enabled { get; set; }

        /// <summary>Tecla que liga/desliga o mirror mesmo com o launcher minimizado (padrão: Pause/Break).</summary>
        public Keys ToggleKey = Keys.Pause;

        /// <summary>Se false, movimentos do mouse não são espelhados (apenas cliques/roda).</summary>
        public bool MirrorMouseMove = true;

        /// <summary>Disparado quando o estado muda pela ToggleKey, para a UI se atualizar.</summary>
        public event Action<bool> StateChanged;

        public InputMirror(Func<List<IntPtr>> getTargets)
        {
            _getTargets = getTargets;
            _kbProc = KeyboardProc;
            _mouseProc = MouseProc;

            IntPtr hMod = GetModuleHandle(Process.GetCurrentProcess().MainModule.ModuleName);
            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        }

        // Converte um uint para IntPtr sem sofrer extensão de sinal em processos 64-bit
        // (importante: wParam/lParam precisam manter os bits altos zerados).
        static IntPtr Ptr(uint v) { return (IntPtr)(long)v; }

        // Retorna a lista de janelas de cliente APENAS se a janela ativa for uma delas
        // (ou seja, o jogador está jogando em uma conta). Caso contrário, retorna null e
        // nada é espelhado (evita espelhar quando o foco está no launcher ou em outro app).
        List<IntPtr> TargetsIfPlaying(out IntPtr master)
        {
            master = GetForegroundWindow();

            int now = Environment.TickCount;
            if (now - _lastRefresh > 500)
            {
                try { _cache = _getTargets() ?? new List<IntPtr>(); }
                catch { _cache = new List<IntPtr>(); }
                _lastRefresh = now;
            }

            return _cache.Contains(master) ? _cache : null;
        }

        IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));

                // Tecla de liga/desliga (consumida para não vazar para o jogo)
                if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && (Keys)data.vkCode == ToggleKey)
                {
                    Enabled = !Enabled;
                    System.Media.SystemSounds.Beep.Play();
                    StateChanged?.Invoke(Enabled);
                    return (IntPtr)1;
                }

                if (Enabled && (msg == WM_KEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP))
                    BroadcastKey(msg, data);
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void BroadcastKey(int msg, KBDLLHOOKSTRUCT data)
        {
            IntPtr master;
            var targets = TargetsIfPlaying(out master);
            if (targets == null)
                return;

            uint scan = MapVirtualKey(data.vkCode, 0); // MAPVK_VK_TO_VSC
            bool extended = (data.flags & LLKHF_EXTENDED) != 0;
            bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);

            // lParam de WM_KEYDOWN/UP: repeat=1, scan code, flag de tecla estendida
            // e, no keyup, os bits de "estado anterior" (30) e "transição" (31).
            uint lp = 1u | (scan << 16);
            if (extended) lp |= (1u << 24);
            if (!isDown) lp |= (1u << 30) | (1u << 31);

            IntPtr wParam = Ptr(data.vkCode);
            IntPtr lParam = Ptr(lp);

            foreach (IntPtr h in targets)
            {
                if (h == master || h == IntPtr.Zero)
                    continue;
                PostMessage(h, (uint)msg, wParam, lParam);
            }
        }

        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Enabled)
            {
                int msg = wParam.ToInt32();
                var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                BroadcastMouse(msg, data);
            }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        void BroadcastMouse(int msg, MSLLHOOKSTRUCT data)
        {
            if (msg == WM_MOUSEMOVE && !MirrorMouseMove)
                return;

            IntPtr master;
            var targets = TargetsIfPlaying(out master);
            if (targets == null)
                return;

            // Ponto do cursor relativo à área cliente da master. Como as janelas dos clientes
            // têm o mesmo tamanho, a mesma coordenada cai no mesmo ponto da UI em cada uma.
            POINT rel = data.pt;
            ScreenToClient(master, ref rel);
            IntPtr lpClient = Ptr(((uint)(rel.y & 0xFFFF) << 16) | (uint)(rel.x & 0xFFFF));

            // A roda usa coordenadas de TELA e o delta no high word do wParam.
            int wheelDelta = (short)((data.mouseData >> 16) & 0xFFFF);
            IntPtr wheelW = Ptr((uint)(wheelDelta << 16));
            IntPtr screenL = Ptr(((uint)(data.pt.y & 0xFFFF) << 16) | (uint)(data.pt.x & 0xFFFF));

            uint keyState = 0;
            switch (msg)
            {
                case WM_LBUTTONDOWN: keyState = MK_LBUTTON; break;
                case WM_RBUTTONDOWN: keyState = MK_RBUTTON; break;
                case WM_MBUTTONDOWN: keyState = MK_MBUTTON; break;
            }
            IntPtr btnW = Ptr(keyState);

            foreach (IntPtr h in targets)
            {
                if (h == master || h == IntPtr.Zero)
                    continue;

                if (msg == WM_MOUSEWHEEL)
                    PostMessage(h, WM_MOUSEWHEEL, wheelW, screenL);
                else
                    PostMessage(h, (uint)msg, btnW, lpClient);
            }
        }

        public void Dispose()
        {
            if (_kbHook != IntPtr.Zero) { UnhookWindowsHookEx(_kbHook); _kbHook = IntPtr.Zero; }
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        }
    }
}
