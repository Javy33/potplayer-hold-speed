// PotPlayer launcher with hold-to-speed-up.
//
// Starts PotPlayer (path in target.txt next to this exe) with the same arguments, then, once per
// session, watches the keyboard while PotPlayer is in front: tap Right = normal seek, hold Right =
// faster (STEPS presses of PotPlayer's default speed-up key C, 0.1 each), release = STEPS presses
// of X, restoring the previous speed. Exits when PotPlayer closes. Built and wired up by install.ps1.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: System.Reflection.AssemblyTitle("PotPlayer (hold to speed up)")]

static class PotPlayerHoldSpeed {
    const ushort TRIGGER_VK = 0x27; // Right arrow
    const int HOLD_MS = 300;        // press longer than this to count as "hold"
    const int STEPS = 10;           // 10 x 0.1 = +1.0, i.e. 1x -> 2x

    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint LLKHF_INJECTED = 0x10, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    const ushort VK_SPEED_UP = 0x43, VK_SPEED_DOWN = 0x58; // C, X

    delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    static readonly HookProc proc = Hook; // keep the delegate alive for the hook's lifetime
    static IntPtr hook;
    static Timer holdTimer;
    static string potName;
    static int state; // 0 idle, 1 pressed (tap or hold undecided), 2 fast

    [STAThread]
    static void Main() {
        string pot = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "target.txt")).Trim();
        try {
            Process.Start(new ProcessStartInfo(pot, ArgsAfterExe(Environment.CommandLine)) { UseShellExecute = false });
        } catch (Exception e) {
            MessageBox.Show("Could not start " + pot + "\n" + e.Message, "PotPlayer hold-to-speed");
            return;
        }

        bool first;
        using (new Mutex(true, @"Local\PotPlayerHoldSpeed", out first)) {
            if (first) RunHook(pot);
        }
    }

    static string ArgsAfterExe(string cmd) {
        int i;
        if (cmd.StartsWith("\"")) { i = cmd.IndexOf('"', 1); i = i < 0 ? cmd.Length : i + 1; }
        else { i = cmd.IndexOf(' '); if (i < 0) i = cmd.Length; }
        return cmd.Substring(i).TrimStart();
    }

    static void RunHook(string pot) {
        potName = Path.GetFileNameWithoutExtension(pot);
        holdTimer = new Timer { Interval = HOLD_MS };
        holdTimer.Tick += delegate {
            holdTimer.Stop();
            if (state == 1) { state = 2; Tap(VK_SPEED_UP, STEPS); }
        };
        var watch = new Timer { Interval = 2000 };
        watch.Tick += delegate { if (!PotPlayerRunning()) Application.Exit(); };
        watch.Start();

        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, delegate { Application.Exit(); });
        var tray = new NotifyIcon {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(pot),
            Text = "PotPlayer: hold Right for 2x",
            ContextMenuStrip = menu,
            Visible = true
        };
        try { Application.Run(); }
        finally { tray.Visible = false; UnhookWindowsHookEx(hook); }
    }

    static IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam) {
        if (nCode >= 0) {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            if (k.vkCode == TRIGGER_VK && (k.flags & LLKHF_INJECTED) == 0) {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) {
                    if (state != 0) return (IntPtr)1; // swallow auto-repeat
                    if (!ModifierDown() && PotPlayerActive()) {
                        state = 1;
                        holdTimer.Start();
                        return (IntPtr)1;
                    }
                } else if (state == 1) {
                    // Released before the hold threshold: replay the original key.
                    holdTimer.Stop();
                    state = 0;
                    Later(delegate { Tap(TRIGGER_VK, 1); });
                    return (IntPtr)1;
                } else if (state == 2) {
                    state = 0;
                    if (PotPlayerActive()) Later(delegate { Tap(VK_SPEED_DOWN, STEPS); });
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    // Inject outside the hook callback.
    static void Later(Action action) {
        var t = new Timer { Interval = 1 };
        t.Tick += delegate { t.Stop(); t.Dispose(); action(); };
        t.Start();
    }

    static bool ModifierDown() {
        foreach (int vk in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) return true;
        return false;
    }

    static bool PotPlayerActive() {
        uint pid;
        GetWindowThreadProcessId(GetForegroundWindow(), out pid);
        try {
            using (var p = Process.GetProcessById((int)pid))
                return string.Equals(p.ProcessName, potName, StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }

    static bool PotPlayerRunning() {
        var procs = Process.GetProcessesByName(potName);
        foreach (var p in procs) p.Dispose();
        return procs.Length > 0;
    }

    static void Tap(ushort vk, int count) {
        if (count <= 0) return;
        uint ext = (vk >= 0x21 && vk <= 0x2E) ? KEYEVENTF_EXTENDEDKEY : 0; // arrows, PgUp/PgDn, Home/End, Ins/Del
        ushort scan = (ushort)MapVirtualKey(vk, 0);
        var inputs = new INPUT[count * 2];
        for (int i = 0; i < inputs.Length; i++) {
            inputs[i].type = 1; // INPUT_KEYBOARD
            inputs[i].u.ki.wVk = vk;
            inputs[i].u.ki.wScan = scan;
            inputs[i].u.ki.dwFlags = ext | (i % 2 == 1 ? KEYEVENTF_KEYUP : 0);
        }
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }
}
