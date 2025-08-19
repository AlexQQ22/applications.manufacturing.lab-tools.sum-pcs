using System;
using System.Diagnostics;
using System.Threading;
using SystemUtilizationMonitor.Win32;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Services
{
    // Input Hook Manager with configurable key mappings
    public class InputHookManager : IDisposable
    {
        private IntPtr keyboardHookId = IntPtr.Zero;
        private IntPtr mouseHookId = IntPtr.Zero;
        private Win32API.LowLevelProc keyboardProc;
        private Win32API.LowLevelProc mouseProc;
        private volatile bool isRunning = false;
        private Thread messageThread;
        private readonly ConfigurationModel config;

        private volatile int keyboardEventCount = 0;
        private volatile int mouseEventCount = 0;
        private DateTime lastMouseMove = DateTime.MinValue;

        public InputHookManager(ConfigurationModel configuration)
        {
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            keyboardProc = KeyboardHookProc;
            mouseProc = MouseHookProc;
        }

        public void Start()
        {
            if (isRunning) return;

            isRunning = true;
            messageThread = new Thread(MessageLoop)
            {
                IsBackground = false,
                Name = "InputHookMessageLoop"
            };
            messageThread.SetApartmentState(ApartmentState.STA);
            messageThread.Start();

            Thread.Sleep(200);
        }

        public void Stop()
        {
            if (!isRunning) return;

            isRunning = false;

            if (messageThread != null && messageThread.IsAlive)
            {
                Win32API.PostQuitMessage(0);
                messageThread.Join(2000);
            }
        }

        private void MessageLoop()
        {
            try
            {
                keyboardHookId = SetKeyboardHook(keyboardProc);
                mouseHookId = SetMouseHook(mouseProc);

                if (keyboardHookId == IntPtr.Zero)
                    Console.WriteLine("Warning: Failed to install keyboard hook (Error: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                else
                    Console.WriteLine("Keyboard hook installed successfully");

                if (mouseHookId == IntPtr.Zero)
                    Console.WriteLine("Warning: Failed to install mouse hook (Error: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                else
                    Console.WriteLine("Mouse hook installed successfully");

                Win32API.MSG msg;
                while (isRunning && Win32API.GetMessage(out msg, IntPtr.Zero, 0, 0))
                {
                    Win32API.TranslateMessage(ref msg);
                    Win32API.DispatchMessage(ref msg);
                }

                if (keyboardHookId != IntPtr.Zero)
                {
                    Win32API.UnhookWindowsHookEx(keyboardHookId);
                    keyboardHookId = IntPtr.Zero;
                }
                if (mouseHookId != IntPtr.Zero)
                {
                    Win32API.UnhookWindowsHookEx(mouseHookId);
                    mouseHookId = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Message loop error: " + ex.Message);
            }
        }

        private IntPtr SetKeyboardHook(Win32API.LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return Win32API.SetWindowsHookEx(config.Hook.WH_KEYBOARD_LL, proc,
                    Win32API.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr SetMouseHook(Win32API.LowLevelProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return Win32API.SetWindowsHookEx(config.Hook.WH_MOUSE_LL, proc,
                    Win32API.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Use configured keyboard constants
                if (wParam == (IntPtr)config.Keyboard.WM_KEYDOWN || 
                    wParam == (IntPtr)config.Keyboard.WM_SYSKEYDOWN)
                {
                    Interlocked.Increment(ref keyboardEventCount);
                }
            }
            return Win32API.CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool countEvent = false;

                // Use configured mouse constants
                if (wParam == (IntPtr)config.Mouse.WM_LBUTTONDOWN ||
                    wParam == (IntPtr)config.Mouse.WM_RBUTTONDOWN ||
                    wParam == (IntPtr)config.Mouse.WM_MBUTTONDOWN ||
                    wParam == (IntPtr)config.Mouse.WM_MOUSEWHEEL)
                {
                    countEvent = true;
                }
                else if (wParam == (IntPtr)config.Mouse.WM_MOUSEMOVE)
                {
                    var now = DateTime.Now;
                    // Use configured mouse move throttle
                    if (now.Subtract(lastMouseMove).TotalMilliseconds > config.Mouse.MouseMoveThrottleMs)
                    {
                        lastMouseMove = now;
                        countEvent = true;
                    }
                }

                if (countEvent)
                {
                    Interlocked.Increment(ref mouseEventCount);
                }
            }
            return Win32API.CallNextHookEx(mouseHookId, nCode, wParam, lParam);
        }

        public int GetKeyboardEventCount()
        {
            return keyboardEventCount;
        }

        public int GetMouseEventCount()
        {
            return mouseEventCount;
        }

        public void ResetCounters()
        {
            Interlocked.Exchange(ref keyboardEventCount, 0);
            Interlocked.Exchange(ref mouseEventCount, 0);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}