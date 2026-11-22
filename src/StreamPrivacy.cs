using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StreamPrivacy
{
    internal static class Native
    {
        internal delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr RemoveProp(IntPtr window, string name);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsWow64Process(IntPtr process, out bool wow64);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder name, ref uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] internal static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        [DllImport("PrivacyHook64.dll", CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCapturePrivacy(IntPtr window, uint affinity);
        [DllImport("PrivacyHook64.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetCapturePrivacyChecked(IntPtr window, uint affinity, string marker);

        internal static string Error(int error) { return new Win32Exception(error).Message + " (" + error + ")"; }
        internal static bool IsSupportedProcess(uint pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                bool wow64;
                if (!IsWow64Process(process, out wow64)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return !wow64;
            }
            finally { CloseHandle(process); }
        }
        internal static string ExecutablePath(uint pid)
        {
            IntPtr process = OpenProcess(0x1000, false, pid);
            if (process == IntPtr.Zero) return "";
            try {
                uint size = 32768; System.Text.StringBuilder path = new System.Text.StringBuilder((int)size);
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString() : "";
            } finally { CloseHandle(process); }
        }
    }

    internal sealed class WindowItem
    {
        internal IntPtr Handle;
        internal uint Pid;
        internal uint Thread;
        internal string App;
        internal string Title;
        internal string ExecutablePath;
    }

    internal sealed class ProtectedWindow
    {
        internal WindowItem Window;
        internal uint Original;
        internal bool Uncertain;
        internal long ProcessStartTicks;
    }

    internal sealed class ProtectionSession : IDisposable
    {
        private readonly string marker = "StreamPrivacy.Window." + Guid.NewGuid().ToString("N");
        private readonly IntPtr markerValue = new IntPtr(RecoverySafety.MarkerValue);
        private readonly object sync = new object();
        private readonly RecoverySafety recovery;
        internal readonly Dictionary<IntPtr, ProtectedWindow> Windows = new Dictionary<IntPtr, ProtectedWindow>();

        internal ProtectionSession(string recoveryDirectory = null)
        {
            recovery = new RecoverySafety(recoveryDirectory, marker);
        }

        internal int WatcherProcessId { get { lock (sync) return recovery.WatcherProcessId; } }

        internal bool IsSameWindow(ProtectedWindow item)
        {
            return RecoverySafety.Matches(item, marker);
        }

        internal void Prune()
        {
            lock (sync)
            {
                List<IntPtr> gone = new List<IntPtr>();
                foreach (KeyValuePair<IntPtr, ProtectedWindow> entry in Windows)
                    if (!IsSameWindow(entry.Value)) gone.Add(entry.Key);
                foreach (IntPtr handle in gone) Windows.Remove(handle);
                if (gone.Count > 0) recovery.Update(Windows.Values, Windows.Count);
                recovery.Maintain(Windows.Values, Windows.Count);
            }
        }

        internal string Protect(WindowItem item)
        {
            lock (sync)
            {
            Prune();
            uint pid;
            if (Native.GetWindowThreadProcessId(item.Handle, out pid) != item.Thread || pid != item.Pid)
                throw new InvalidOperationException("La finestra è cambiata. Aggiorna l'elenco e selezionala di nuovo.");
            if (!Native.IsSupportedProcess(item.Pid))
                throw new InvalidOperationException("Questa versione supporta le app a 64 bit. La finestra selezionata appartiene a un'app a 32 bit.");
            uint original;
            if (!Native.GetWindowDisplayAffinity(item.Handle, out original))
                throw new InvalidOperationException("Impossibile leggere lo stato della finestra. " + Native.Error(Marshal.GetLastWin32Error()));
            if (original == 0x11)
            {
                if (Windows.ContainsKey(item.Handle)) recovery.Prepare(Windows.Values);
                return "Windows conferma già l'esclusione. Verifica comunque l'anteprima dello stream.";
            }
            if (original != 0 && original != 1)
                throw new InvalidOperationException("La finestra usa un'impostazione di protezione non riconosciuta.");
            ProtectedWindow tracked = new ProtectedWindow { Window = item, Original = original, Uncertain = true, ProcessStartTicks = RecoverySafety.ProcessStartTicks(item.Pid) };
            bool newlyTracked = !Windows.ContainsKey(item.Handle);
            if (!Native.SetProp(item.Handle, marker, markerValue)) throw new Win32Exception(Marshal.GetLastWin32Error());
            // Keep the original even if a timed-out request finishes late. Restore remains available.
            if (newlyTracked) Windows.Add(item.Handle, tracked);
            else {
                tracked = Windows[item.Handle];
                // If the app changed its setting after a confirmed request,
                // preserve that new setting when automatic protection reapplies.
                if (!tracked.Uncertain) tracked.Original = original;
                tracked.Uncertain = true;
            }
            // No affinity mutation occurs until its original value is durable
            // and an independent recovery companion has acknowledged readiness.
            try {
                if (!RecoverySafety.Matches(tracked, marker)) throw new InvalidOperationException("La finestra è cambiata prima della protezione.");
                recovery.Prepare(Windows.Values);
            }
            catch
            {
                // Preparing failed before any affinity call. A newly created
                // marker must not make an untouched window look session-owned.
                if (newlyTracked)
                {
                    Native.RemoveProp(item.Handle, marker);
                    Windows.Remove(item.Handle);
                    try { recovery.Update(Windows.Values, Windows.Count); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                throw;
            }
            if (!Native.SetCapturePrivacyChecked(item.Handle, 0x11, marker))
                throw new InvalidOperationException("Protezione non confermata: " + Native.Error(Marshal.GetLastWin32Error()) + ". Puoi usare Ripristina per annullare la richiesta.");
            uint affinity;
            if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 0x11)
                throw new InvalidOperationException("Windows non conferma l'esclusione. Non considerare protetta questa finestra.");
            tracked.Uncertain = false;
            recovery.Update(Windows.Values, Windows.Count);
            return "Esclusione confermata da Windows. Ora controlla l'anteprima in OBS e su Discord.";
            }
        }

        internal void Restore(IntPtr handle)
        {
            lock (sync)
            {
            ProtectedWindow tracked;
            if (!Windows.TryGetValue(handle, out tracked)) return;
            RecoverySafety.RestoreMarked(tracked, marker);
            Windows.Remove(handle);
            recovery.Update(Windows.Values, Windows.Count);
            }
        }

        internal List<string> RestoreAll()
        {
            lock (sync)
            {
            List<string> errors = new List<string>();
            foreach (IntPtr handle in new List<IntPtr>(Windows.Keys))
                try { Restore(handle); } catch (Exception error) { errors.Add(error.Message); }
            return errors;
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                try {
                    if (Windows.Count == 0) recovery.Update(Windows.Values, 0);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                finally { recovery.Dispose(); }
            }
        }
    }

}
