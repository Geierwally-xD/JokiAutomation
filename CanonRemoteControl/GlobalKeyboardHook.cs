using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CanonRemoteControl
{
    public sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_SHIFT_LEFT = 0xA0;
        private const int VK_SHIFT_RIGHT = 0xA1;
        private const int VK_CONTROL_LEFT = 0xA2;
        private const int VK_CONTROL_RIGHT = 0xA3;

        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event EventHandler<KeyboardHookEventArgs> KeyDown;
        public event EventHandler<KeyboardHookEventArgs> KeyUp;

        public GlobalKeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookId != IntPtr.Zero)
            {
                return;
            }

            _hookId = SetHook(_proc);
        }

        public void Uninstall()
        {
            if (_hookId == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    proc,
                    GetModuleHandle(curModule.ModuleName),
                    0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int message = wParam.ToInt32();

                bool ctrlPressed = IsKeyPressed(VK_CONTROL_LEFT) || IsKeyPressed(VK_CONTROL_RIGHT);
                bool shiftPressed = IsKeyPressed(VK_SHIFT_LEFT) || IsKeyPressed(VK_SHIFT_RIGHT);

                var args = new KeyboardHookEventArgs
                {
                    VirtualKeyCode = vkCode,
                    CtrlPressed = ctrlPressed,
                    ShiftPressed = shiftPressed
                };

                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    KeyDown?.Invoke(this, args);
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    KeyUp?.Invoke(this, args);
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsKeyPressed(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        public void Dispose()
        {
            Uninstall();
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }

    public sealed class KeyboardHookEventArgs : EventArgs
    {
        public int VirtualKeyCode { get; set; }

        public bool CtrlPressed { get; set; }

        public bool ShiftPressed { get; set; }
    }
}
