// using System;
// using System.Diagnostics;
// using System.Threading;
// using System.Threading.Tasks;
// using System.Collections.Generic;
// using System.Runtime.InteropServices;
// using SystemUtilizationMonitor.Win32;
// using SystemUtilizationMonitor.Models;

// namespace SystemUtilizationMonitor.Services
// {
//     // Enhanced Input Hook Manager with polling fallback for VM compatibility
//     public class InputHookManager : IDisposable
//     {
//         // Traditional hook variables
//         private IntPtr keyboardHookId = IntPtr.Zero;
//         private IntPtr mouseHookId = IntPtr.Zero;
//         private Win32API.LowLevelProc keyboardProc;
//         private Win32API.LowLevelProc mouseProc;
//         private volatile bool isRunning = false;
//         private Thread messageThread;
//         private readonly ConfigurationModel config;

//         // Polling variables for VM compatibility
//         [DllImport("user32.dll")]
//         static extern short GetAsyncKeyState(int vKey);

//         private CancellationTokenSource cancellationTokenSource;
//         private Task pollingTask;
//         private readonly int pollingIntervalMs = 10; // 10ms polling interval

//         // Counters
//         private volatile int keyboardEventCount = 0;
//         private volatile int mouseEventCount = 0;
//         private volatile int pollingKeyboardEventCount = 0;
//         private DateTime lastMouseMove = DateTime.MinValue;

//         // Track state of all keys for polling
//         private readonly Dictionary<int, bool> keyStates = new Dictionary<int, bool>();

//         // Virtual key codes for common keys
//         private readonly int[] monitoredKeys = new int[]
//         {
//             // Letters A-Z
//             0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A,
//             0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50, 0x51, 0x52, 0x53, 0x54,
//             0x55, 0x56, 0x57, 0x58, 0x59, 0x5A,
            
//             // Numbers 0-9
//             0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39,
            
//             // Function keys F1-F12
//             0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x7B,
            
//             // Special keys
//             0x08, // Backspace
//             0x09, // Tab
//             0x0D, // Enter
//             0x10, // Shift
//             0x11, // Ctrl
//             0x12, // Alt
//             0x1B, // Escape
//             0x20, // Space
//             0x25, // Left Arrow
//             0x26, // Up Arrow
//             0x27, // Right Arrow
//             0x28, // Down Arrow
//             0x2E, // Delete
            
//             // Numpad
//             0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, // Numpad 0-9
//             0x6A, // Multiply
//             0x6B, // Add
//             0x6D, // Subtract
//             0x6E, // Decimal
//             0x6F, // Divide
            
//             // Other common keys
//             0xBA, // Semicolon
//             0xBB, // Plus
//             0xBC, // Comma
//             0xBD, // Minus
//             0xBE, // Period
//             0xBF, // Forward Slash
//             0xC0, // Grave accent
//             0xDB, // Open bracket
//             0xDC, // Backslash
//             0xDD, // Close bracket
//             0xDE  // Quote
//         };

//         public InputHookManager(ConfigurationModel configuration)
//         {
//             config = configuration ?? throw new ArgumentNullException(nameof(configuration));
//             keyboardProc = KeyboardHookProc;
//             mouseProc = MouseHookProc;
            
//             // Initialize key states for polling
//             foreach (int key in monitoredKeys)
//             {
//                 keyStates[key] = false;
//             }
//         }

//         public void Start()
//         {
//             if (isRunning) return;

//             isRunning = true;
            
//             // Start traditional hook message thread
//             messageThread = new Thread(MessageLoop)
//             {
//                 IsBackground = false,
//                 Name = "InputHookMessageLoop"
//             };
//             messageThread.SetApartmentState(ApartmentState.STA);
//             messageThread.Start();

//             // Start polling task for VM compatibility
//             cancellationTokenSource = new CancellationTokenSource();
//             pollingTask = Task.Run(() => PollingLoop(cancellationTokenSource.Token));

//             Thread.Sleep(200);
//             Console.WriteLine("Input monitoring started (hooks + polling for VM compatibility)");
//         }

//         public void Stop()
//         {
//             if (!isRunning) return;

//             isRunning = false;

//             // Stop polling
//             cancellationTokenSource?.Cancel();
//             try
//             {
//                 pollingTask?.Wait(1000);
//             }
//             catch (AggregateException)
//             {
//                 // Task was cancelled, this is expected
//             }

//             // Stop traditional hooks
//             if (messageThread != null && messageThread.IsAlive)
//             {
//                 Win32API.PostQuitMessage(0);
//                 messageThread.Join(2000);
//             }
//         }

//         private void MessageLoop()
//         {
//             try
//             {
//                 keyboardHookId = SetKeyboardHook(keyboardProc);
//                 mouseHookId = SetMouseHook(mouseProc);

//                 if (keyboardHookId == IntPtr.Zero)
//                     Console.WriteLine("Warning: Failed to install keyboard hook (Error: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
//                 else
//                     Console.WriteLine("Keyboard hook installed successfully");

//                 if (mouseHookId == IntPtr.Zero)
//                     Console.WriteLine("Warning: Failed to install mouse hook (Error: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
//                 else
//                     Console.WriteLine("Mouse hook installed successfully");

//                 Win32API.MSG msg;
//                 while (isRunning && Win32API.GetMessage(out msg, IntPtr.Zero, 0, 0))
//                 {
//                     Win32API.TranslateMessage(ref msg);
//                     Win32API.DispatchMessage(ref msg);
//                 }

//                 if (keyboardHookId != IntPtr.Zero)
//                 {
//                     Win32API.UnhookWindowsHookEx(keyboardHookId);
//                     keyboardHookId = IntPtr.Zero;
//                 }
//                 if (mouseHookId != IntPtr.Zero)
//                 {
//                     Win32API.UnhookWindowsHookEx(mouseHookId);
//                     mouseHookId = IntPtr.Zero;
//                 }
//             }
//             catch (Exception ex)
//             {
//                 Console.WriteLine("Message loop error: " + ex.Message);
//             }
//         }

//         private IntPtr SetKeyboardHook(Win32API.LowLevelProc proc)
//         {
//             using (Process curProcess = Process.GetCurrentProcess())
//             using (ProcessModule curModule = curProcess.MainModule)
//             {
//                 return Win32API.SetWindowsHookEx(config.Hook.WH_KEYBOARD_LL, proc,
//                     Win32API.GetModuleHandle(curModule.ModuleName), 0);
//             }
//         }

//         private IntPtr SetMouseHook(Win32API.LowLevelProc proc)
//         {
//             using (Process curProcess = Process.GetCurrentProcess())
//             using (ProcessModule curModule = curProcess.MainModule)
//             {
//                 return Win32API.SetWindowsHookEx(config.Hook.WH_MOUSE_LL, proc,
//                     Win32API.GetModuleHandle(curModule.ModuleName), 0);
//             }
//         }

//         private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
//         {
//             // Log to C:\temp\keyboard.log
//             try
//             {
//                 string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - HOOK - nCode: {nCode}, wParam: {wParam}, lParam: {lParam}{Environment.NewLine}";
//                 System.IO.File.AppendAllText(@"C:\temp\keyboard.log", logEntry);
//             }
//             catch
//             {
//             }

//             if (nCode >= 0)
//             {
//                 // Use configured keyboard constants
//                 if (wParam == (IntPtr)config.Keyboard.WM_KEYDOWN ||
//                     wParam == (IntPtr)config.Keyboard.WM_SYSKEYDOWN)
//                 {
//                     Interlocked.Increment(ref keyboardEventCount);
//                 }
//             }
//             return Win32API.CallNextHookEx(keyboardHookId, nCode, wParam, lParam);
//         }

//         private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
//         {
//             if (nCode >= 0)
//             {
//                 bool countEvent = false;

//                 // Use configured mouse constants
//                 if (wParam == (IntPtr)config.Mouse.WM_LBUTTONDOWN ||
//                     wParam == (IntPtr)config.Mouse.WM_RBUTTONDOWN ||
//                     wParam == (IntPtr)config.Mouse.WM_MBUTTONDOWN ||
//                     wParam == (IntPtr)config.Mouse.WM_MOUSEWHEEL)
//                 {
//                     countEvent = true;
//                 }
//                 else if (wParam == (IntPtr)config.Mouse.WM_MOUSEMOVE)
//                 {
//                     var now = DateTime.Now;
//                     // Use configured mouse move throttle
//                     if (now.Subtract(lastMouseMove).TotalMilliseconds > config.Mouse.MouseMoveThrottleMs)
//                     {
//                         lastMouseMove = now;
//                         countEvent = true;
//                     }
//                 }

//                 if (countEvent)
//                 {
//                     Interlocked.Increment(ref mouseEventCount);
//                 }
//             }
//             return Win32API.CallNextHookEx(mouseHookId, nCode, wParam, lParam);
//         }

//         private async Task PollingLoop(CancellationToken cancellationToken)
//         {
//             while (!cancellationToken.IsCancellationRequested)
//             {
//                 try
//                 {
//                     CheckKeyStates();
//                     await Task.Delay(pollingIntervalMs, cancellationToken);
//                 }
//                 catch (OperationCanceledException)
//                 {
//                     break;
//                 }
//                 catch (Exception ex)
//                 {
//                     Console.WriteLine($"Error in polling loop: {ex.Message}");
//                     await Task.Delay(100, cancellationToken);
//                 }
//             }
//         }

//         private void CheckKeyStates()
//         {
//             foreach (int key in monitoredKeys)
//             {
//                 // GetAsyncKeyState returns the key state since the last call
//                 // The most significant bit indicates if the key is currently pressed
//                 // The least significant bit indicates if the key was pressed since the last call
//                 short keyState = GetAsyncKeyState(key);
//                 bool isCurrentlyPressed = (keyState & 0x8000) != 0;
//                 bool wasPressed = (keyState & 0x0001) != 0;

//                 // Detect key press (transition from not pressed to pressed)
//                 if (!keyStates[key] && isCurrentlyPressed)
//                 {
//                     keyStates[key] = true;
//                     OnKeyPressed(key);
//                 }
//                 else if (keyStates[key] && !isCurrentlyPressed)
//                 {
//                     keyStates[key] = false;
//                 }

//                 // Count any key press activity detected by the LSB
//                 if (wasPressed)
//                 {
//                     Interlocked.Increment(ref pollingKeyboardEventCount);
                    
//                     // Log key press
//                     try
//                     {
//                         string keyName = GetKeyName(key);
//                         string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - POLL - Key: {keyName} (0x{key:X2}){Environment.NewLine}";
//                         System.IO.File.AppendAllText(@"C:\temp\keyboard_poll.log", logEntry);
//                     }
//                     catch
//                     {
//                         // Ignore logging errors
//                     }
//                 }
//             }
//         }

//         private void OnKeyPressed(int virtualKeyCode)
//         {
//             // This method is called when a key press is detected
//             // You can add additional logic here if needed
//         }

//         private string GetKeyName(int virtualKeyCode)
//         {
//             // Simple key name mapping for logging
//             switch (virtualKeyCode)
//             {
//                 case 0x08: return "Backspace";
//                 case 0x09: return "Tab";
//                 case 0x0D: return "Enter";
//                 case 0x10: return "Shift";
//                 case 0x11: return "Ctrl";
//                 case 0x12: return "Alt";
//                 case 0x1B: return "Escape";
//                 case 0x20: return "Space";
//                 case 0x25: return "Left";
//                 case 0x26: return "Up";
//                 case 0x27: return "Right";
//                 case 0x28: return "Down";
//                 case 0x2E: return "Delete";
//                 default:
//                     if (virtualKeyCode >= 0x41 && virtualKeyCode <= 0x5A) // A-Z
//                         return ((char)virtualKeyCode).ToString();
//                     if (virtualKeyCode >= 0x30 && virtualKeyCode <= 0x39) // 0-9
//                         return ((char)virtualKeyCode).ToString();
//                     if (virtualKeyCode >= 0x70 && virtualKeyCode <= 0x7B) // F1-F12
//                         return $"F{virtualKeyCode - 0x6F}";
//                     return $"Key{virtualKeyCode:X2}";
//             }
//         }

//         public int GetKeyboardEventCount()
//         {
//             return keyboardEventCount;
//         }

//         public int GetMouseEventCount()
//         {
//             return mouseEventCount;
//         }

//         // New method to get polling-based keyboard events (should work in VMs)
//         public int GetPollingKeyboardEventCount()
//         {
//             return pollingKeyboardEventCount;
//         }

//         // Combined count from both hook and polling
//         public int GetTotalKeyboardEventCount()
//         {
//             return keyboardEventCount + pollingKeyboardEventCount;
//         }

//         public void ResetCounters()
//         {
//             Interlocked.Exchange(ref keyboardEventCount, 0);
//             Interlocked.Exchange(ref mouseEventCount, 0);
//             Interlocked.Exchange(ref pollingKeyboardEventCount, 0);
//         }

//         public void Dispose()
//         {
//             Stop();
//             cancellationTokenSource?.Dispose();
//         }
//     }
// }