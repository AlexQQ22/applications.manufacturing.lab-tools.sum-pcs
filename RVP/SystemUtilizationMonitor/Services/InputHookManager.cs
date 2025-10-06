using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;  // ← ADD THIS
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using SystemUtilizationMonitor.Win32;
using SystemUtilizationMonitor.Models;

namespace SystemUtilizationMonitor.Services
{
    [ExcludeFromCodeCoverage]  // ← ADD THIS
    public class InputHookManager : IDisposable
    {
        private IntPtr keyboardHookId = IntPtr.Zero;
        private IntPtr mouseHookId = IntPtr.Zero;
        private Win32API.LowLevelProc keyboardProc;
        private Win32API.LowLevelProc mouseProc;
        private volatile bool isRunning = false;
        private Thread messageThread;
        private readonly ConfigurationModel config;
        private RawInputForm rawInputForm;

        private volatile int keyboardEventCount = 0;
        private volatile int mouseEventCount = 0;
        private volatile int rawKeyboardEventCount = 0;
        private DateTime lastMouseMove = DateTime.MinValue;

        // Raw Input structures and constants
        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWKEYBOARD
        {
            public ushort MakeCode;
            public ushort Flags;
            public ushort Reserved;
            public ushort VKey;
            public uint Message;
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct RAWINPUT
        {
            [FieldOffset(0)]
            public RAWINPUTHEADER header;
            [FieldOffset(24)]
            public RAWKEYBOARD keyboard;
        }

        // Raw Input API declarations
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevice, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        // Constants
        const int RIM_TYPEKEYBOARD = 1;
        const int RID_INPUT = 0x10000003;
        const int WM_INPUT = 0x00FF;
        const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        const ushort HID_USAGE_GENERIC_KEYBOARD = 0x06;
        const uint RIDEV_INPUTSINK = 0x00000100;

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

            if (rawInputForm != null)
            {
                rawInputForm.Invoke(new Action(() => rawInputForm.Close()));
            }

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
                // Create invisible form for raw input
                rawInputForm = new RawInputForm();
                rawInputForm.RawKeyboardInput += OnRawKeyboardInput;

                // Register for raw keyboard input
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                rid[0].usUsage = HID_USAGE_GENERIC_KEYBOARD;
                rid[0].dwFlags = RIDEV_INPUTSINK;
                rid[0].hwndTarget = rawInputForm.Handle;

                if (!RegisterRawInputDevices(rid, (uint)rid.Length, (uint)Marshal.SizeOf(rid[0])))
                {
                    Console.WriteLine("Warning: Failed to register raw input device (Error: " + Marshal.GetLastWin32Error() + ")");
                }
                else
                {
                    Console.WriteLine("Raw input device registered successfully");
                }

                // Set up traditional hooks as backup
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

                // Run the message loop
                Application.Run(rawInputForm);

                // Cleanup
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

        private void OnRawKeyboardInput(object sender, RawKeyboardEventArgs e)
        {
            //// Log raw keyboard input
            //try
            //{
            //    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - RAW - VKey: {e.VKey}, MakeCode: {e.MakeCode}, Flags: {e.Flags}, Message: {e.Message}{Environment.NewLine}";
            //    System.IO.File.AppendAllText(@"C:\temp\raw_keyboard.log", logEntry);
            //}
            //catch
            //{
            //}

            // Count key press events (not releases)
            if ((e.Flags & 0x01) == 0) // RI_KEY_MAKE (key press, not release)
            {
                Interlocked.Increment(ref rawKeyboardEventCount);
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
            // Log traditional hook to C:\temp\keyboard.log
            //try
            //{
            //    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - HOOK - nCode: {nCode}, wParam: {wParam}, lParam: {lParam}{Environment.NewLine}";
            //    System.IO.File.AppendAllText(@"C:\temp\keyboard.log", logEntry);
            //}
            //catch
            //{
            //}

            if (nCode >= 0)
            {
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

        public int GetRawKeyboardEventCount()
        {
            return rawKeyboardEventCount;
        }

        public void ResetCounters()
        {
            Interlocked.Exchange(ref keyboardEventCount, 0);
            Interlocked.Exchange(ref mouseEventCount, 0);
            Interlocked.Exchange(ref rawKeyboardEventCount, 0);
        }

        public void Dispose()
        {
            Stop();
        }

        // Hidden form to receive raw input messages
        private class RawInputForm : Form
        {
            public event EventHandler<RawKeyboardEventArgs> RawKeyboardInput;

            public RawInputForm()
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Visible = false;
                this.SetVisibleCore(false);
            }

            protected override void SetVisibleCore(bool value)
            {
                base.SetVisibleCore(false);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_INPUT)
                {
                    ProcessRawInput(m.LParam);
                }
                base.WndProc(ref m);
            }

            private void ProcessRawInput(IntPtr lParam)
            {
                uint dwSize = 0;
                GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));

                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
                    {
                        RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
                        if (raw.header.dwType == RIM_TYPEKEYBOARD)
                        {
                            RawKeyboardInput?.Invoke(this, new RawKeyboardEventArgs
                            {
                                VKey = raw.keyboard.VKey,
                                MakeCode = raw.keyboard.MakeCode,
                                Flags = raw.keyboard.Flags,
                                Message = raw.keyboard.Message
                            });
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public class RawKeyboardEventArgs : EventArgs
        {
            public ushort VKey { get; set; }
            public ushort MakeCode { get; set; }
            public ushort Flags { get; set; }
            public uint Message { get; set; }
        }
    }
}