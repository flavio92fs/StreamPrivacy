using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace StreamPrivacy
{
    // Only harmless, newly spawned demo processes and a unique local test profile
    // are used. These checks exercise actual process termination, not a simulated
    // disposal or a call to the normal exit handler.
    internal static class ForcedExitTests
    {
        private sealed class Worker
        {
            internal Process Controller;
            internal Process Watcher;
            internal string Command;
        }

        internal static int Run(string report)
        {
            List<string> lines = new List<string>();
            List<Process> children = new List<Process>();
            string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report)), "forced-exit-" + Guid.NewGuid().ToString("N"));
            string executable = Path.Combine(fixture, "StreamPrivacy.exe");
            string recovery = Path.Combine(fixture, "Data", "Recovery");
            WindowItem first = null, monitor = null, independent = null;
            try
            {
                Directory.CreateDirectory(fixture);
                File.Copy(Application.ExecutablePath, executable);
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrivacyHook64.dll"), Path.Combine(fixture, "PrivacyHook64.dll"));
                File.WriteAllText(Path.Combine(fixture, "portable.flag"), "Isolated forced-exit test profile.");
                Process firstProcess = Start(executable, "--demo", children);
                Process monitorProcess = Start(executable, "--demo", children);
                Process independentProcess = Start(executable, "--demo", children);
                first = FindDemo(firstProcess);
                monitor = FindDemo(monitorProcess);
                independent = FindDemo(independentProcess);
                SetAffinity(monitor.Handle, 1);
                SetAffinity(independent.Handle, 0x11);
                AssertAffinity(first.Handle, 0, "Initial unprotected demo");
                lines.Add("PASS: isolated portable fixture and three harmless demo processes; no real app, startup registration or user profile changed.");

                string absentMarker = "StreamPrivacy.Window." + Guid.NewGuid().ToString("N");
                if (Native.SetCapturePrivacyChecked(first.Handle, 0x11, absentMarker))
                    throw new Exception("Native checked setter accepted a target without its session marker.");
                AssertAffinity(first.Handle, 0, "Native checked setter must reject an absent ownership marker");
                lines.Add("PASS: native checked setter rejects a missing ownership marker without enabling capture exclusion.");

                string blockedRecovery = Path.Combine(fixture, "not-a-directory");
                File.WriteAllText(blockedRecovery, "A regular file deliberately prevents creation of recovery storage.");
                using (ProtectionSession blocked = new ProtectionSession(blockedRecovery))
                {
                    FieldInfo markerField = typeof(ProtectionSession).GetField("marker", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (markerField == null) throw new Exception("Cannot inspect session marker for failed-journal test.");
                    string blockedMarker = (string)markerField.GetValue(blocked);
                    bool rejected = false;
                    try { blocked.Protect(first); } catch (Exception) { rejected = true; }
                    if (!rejected) throw new Exception("Protection was accepted without writable recovery storage.");
                    AssertAffinity(first.Handle, 0, "Failed journal creation must not enable exclusion");
                    if (Native.GetProp(first.Handle, blockedMarker) != IntPtr.Zero)
                        throw new Exception("Failed journal creation left an untracked ownership marker.");
                }
                lines.Add("PASS: unusable recovery storage rejects protection before affinity mutation and leaves no ownership marker on the target.");

                using (ProtectionSession maintained = new ProtectionSession(recovery))
                {
                    maintained.Protect(first);
                    Process lostWatcher = Process.GetProcessById(maintained.WatcherProcessId);
                    children.Add(lostWatcher);
                    Kill(lostWatcher);
                    maintained.Prune();
                    int replacementPid = maintained.WatcherProcessId;
                    if (replacementPid <= 0 || replacementPid == lostWatcher.Id)
                        throw new Exception("Routine maintenance did not replace the terminated watchdog.");
                    Process replacementWatcher = Process.GetProcessById(replacementPid);
                    children.Add(replacementWatcher);
                    if (replacementWatcher.HasExited) throw new Exception("Replacement watchdog is not running.");
                    AssertAffinity(first.Handle, 0x11, "Watchdog replacement must preserve active exclusion");
                    SetAffinity(first.Handle, 1);
                    maintained.Protect(first);
                    AssertAffinity(first.Handle, 0x11, "Automatic reapplication after independent change");
                    if (maintained.RestoreAll().Count != 0) throw new Exception("Restoration after independent change failed.");
                    AssertAffinity(first.Handle, 1, "Automatic reapplication must remember the newer original affinity");
                    WaitForExit(replacementWatcher, 10000, "Replacement watchdog did not exit after normal restoration.");
                }
                SetAffinity(first.Handle, 0);
                lines.Add("PASS: routine maintenance replaces a killed watchdog while preserving active exclusion; reapplication after an independent MONITOR change restores the newer original instead of stale NONE.");

                Worker killed = StartWorker(executable, recovery, fixture, children, first, monitor, independent);
                AssertAffinity(first.Handle, 0x11, "Protected NONE demo");
                AssertAffinity(monitor.Handle, 0x11, "Protected MONITOR demo");
                RecoverySafety.RecoverPending(recovery);
                AssertAffinity(first.Handle, 0x11, "Live-owner recovery must preserve active protection");
                Kill(killed.Controller);
                WaitUntil(delegate { return AffinityIs(first.Handle, 0) && AffinityIs(monitor.Handle, 1); }, 15000,
                    "Watchdog did not restore original affinities after a real controller Kill().");
                WaitForExit(killed.Watcher, 10000, "Watchdog did not exit after restoring a killed controller.");
                AssertAffinity(independent.Handle, 0x11, "Independent exclusion after controller kill");
                lines.Add("PASS: actual controller Kill() restored the same live demo windows to NONE and original MONITOR without restarting either target; independently excluded window remained 0x11.");

                Worker normal = StartWorker(executable, recovery, fixture, children, first, monitor, independent);
                File.WriteAllText(normal.Command, "restore");
                WaitForExit(normal.Controller, 15000, "Normal worker restore/exit did not complete.");
                if (normal.Controller.ExitCode != 0) throw new Exception("Normal worker restore failed.");
                WaitForExit(normal.Watcher, 10000, "Normal restoration left a watchdog process running.");
                AssertAffinity(first.Handle, 0, "Normal restore NONE demo");
                AssertAffinity(monitor.Handle, 1, "Normal restore MONITOR demo");
                AssertAffinity(independent.Handle, 0x11, "Independent exclusion after normal restore");
                lines.Add("PASS: normal RestoreAll/disposal restores originals and terminates its watchdog without changing independent protection.");

                Worker bothKilled = StartWorker(executable, recovery, fixture, children, first, monitor, independent);
                Kill(bothKilled.Watcher);
                Kill(bothKilled.Controller);
                AssertAffinity(first.Handle, 0x11, "Double-kill precondition");
                RecoverySafety.RecoverPending(recovery);
                AssertAffinity(first.Handle, 0, "Journal recovery NONE demo");
                AssertAffinity(monitor.Handle, 1, "Journal recovery MONITOR demo");
                AssertAffinity(independent.Handle, 0x11, "Independent exclusion after journal replay");
                RecoverySafety.RecoverPending(recovery);
                AssertAffinity(first.Handle, 0, "Repeated journal recovery");
                lines.Add("PASS: killing both watchdog and controller leaves a journal that the next recovery call replays safely; original affinities restored, unrelated 0x11 retained, second replay harmless.");

                Worker changed = StartWorker(executable, recovery, fixture, children, first);
                SetAffinity(first.Handle, 1);
                Kill(changed.Controller);
                WaitForExit(changed.Watcher, 15000, "Watchdog did not finish after independent target change.");
                AssertAffinity(first.Handle, 1, "Independent change made after protection");
                SetAffinity(first.Handle, 0);
                lines.Add("PASS: watchdog preserves a later independent affinity change instead of overwriting it with a stale original.");

                Worker mismatched = StartWorker(executable, recovery, fixture, children, first);
                Kill(mismatched.Watcher);
                Kill(mismatched.Controller);
                string lostMarker = JournalMarker(recovery, mismatched.Controller.Id);
                if (Native.GetProp(first.Handle, lostMarker) == IntPtr.Zero)
                    throw new Exception("Marker-mismatch test did not find the live window's session property.");
                Native.RemoveProp(first.Handle, lostMarker);
                RecoverySafety.RecoverPending(recovery);
                AssertAffinity(first.Handle, 0x11, "Recovery must not modify a window whose ownership marker no longer matches");
                AssertAffinity(independent.Handle, 0x11, "Independent exclusion after mismatched-marker recovery");
                SetAffinity(first.Handle, 0);
                lines.Add("PASS: an otherwise matching PID/thread/HWND with a missing session marker is treated as a stale record; recovery leaves its affinity untouched.");

                Worker destroyed = StartWorker(executable, recovery, fixture, children, first);
                Kill(destroyed.Watcher);
                Kill(destroyed.Controller);
                firstProcess.CloseMainWindow();
                WaitForExit(firstProcess, 5000, "Harmless target did not close for stale-window test.");
                RecoverySafety.RecoverPending(recovery);
                AssertAffinity(monitor.Handle, 1, "Other demo after stale-window recovery");
                AssertAffinity(independent.Handle, 0x11, "Independent demo after stale-window recovery");
                lines.Add("PASS: recovery ignores a destroyed target HWND and leaves other windows unchanged.");
                lines.Add("NOT TESTED: actual Discord/OBS video output or termination of real user applications. API state was checked on the same live test windows after real process kills.");
                File.WriteAllLines(report, lines.ToArray());
                return 0;
            }
            catch (Exception error)
            {
                lines.Add("FAIL: " + error);
                File.WriteAllLines(report, lines.ToArray());
                return 1;
            }
            finally
            {
                // These handles belong only to the demos created above.
                foreach (Process process in children)
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(3000); } } catch (Exception) { }
                try { RecoverySafety.RecoverPending(recovery); } catch (Exception) { }
                foreach (Process process in children) process.Dispose();
            }
        }

        internal static int RunWorker(string[] args)
        {
            string ready = args.Length > 2 ? args[2] : null;
            try
            {
                if (args.Length < 5) throw new ArgumentException("Expected isolated recovery directory, ready file, command file and demo HWNDs.");
                using (ProtectionSession session = new ProtectionSession(args[1]))
                {
                    for (int index = 4; index < args.Length; index++)
                    {
                        IntPtr handle = new IntPtr(Int64.Parse(args[index], CultureInfo.InvariantCulture));
                        uint pid;
                        uint thread = Native.GetWindowThreadProcessId(handle, out pid);
                        if (pid == 0 || thread == 0) throw new Exception("Test worker received an invalid target.");
                        session.Protect(new WindowItem { Handle = handle, Pid = pid, Thread = thread,
                            App = "Isolated harmless demo", ExecutablePath = Native.ExecutablePath(pid), Title = "" });
                    }
                    if (session.WatcherProcessId <= 0) throw new Exception("Recovery watchdog was not started.");
                    File.WriteAllText(ready + ".tmp", session.WatcherProcessId.ToString(CultureInfo.InvariantCulture));
                    File.Move(ready + ".tmp", ready);
                    DateTime limit = DateTime.UtcNow.AddMinutes(3);
                    while (!File.Exists(args[3]))
                    {
                        if (DateTime.UtcNow > limit) throw new TimeoutException("Test worker command timed out.");
                        Thread.Sleep(50);
                    }
                    if (File.ReadAllText(args[3]).Trim() != "restore") throw new Exception("Unknown isolated worker command.");
                    List<string> failures = session.RestoreAll();
                    if (failures.Count != 0) throw new Exception("Normal worker restore failed: " + failures[0]);
                }
                return 0;
            }
            catch (Exception error)
            {
                if (!String.IsNullOrEmpty(ready)) File.WriteAllText(ready + ".error", error.ToString());
                return 1;
            }
        }

        private static Worker StartWorker(string executable, string recovery, string fixture, List<Process> children, params WindowItem[] items)
        {
            string unique = Path.Combine(fixture, "worker-" + Guid.NewGuid().ToString("N"));
            string ready = unique + ".ready";
            string command = unique + ".command";
            string arguments = "--forced-exit-worker " + Quote(recovery) + " " + Quote(ready) + " " + Quote(command);
            foreach (WindowItem item in items) arguments += " " + item.Handle.ToInt64().ToString(CultureInfo.InvariantCulture);
            Process controller = Start(executable, arguments, children);
            WaitUntil(delegate {
                if (File.Exists(ready + ".error")) throw new Exception(File.ReadAllText(ready + ".error"));
                if (controller.HasExited) throw new Exception("Test worker exited before confirming protection.");
                return File.Exists(ready);
            }, 20000, "Test worker did not confirm protection and watchdog readiness.");
            int pid = Int32.Parse(File.ReadAllText(ready), CultureInfo.InvariantCulture);
            Process watcher = Process.GetProcessById(pid);
            if (!String.Equals(watcher.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
            { watcher.Dispose(); throw new Exception("Refusing to control an unexpected watchdog executable."); }
            children.Add(watcher);
            return new Worker { Controller = controller, Watcher = watcher, Command = command };
        }

        private static Process Start(string executable, string arguments, List<Process> children)
        {
            Process process = Process.Start(new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(executable)
            });
            children.Add(process);
            return process;
        }

        private static WindowItem FindDemo(Process process)
        {
            WindowItem result = null;
            WaitUntil(delegate {
                foreach (WindowItem item in MainForm.EnumerateWindows(true))
                    if (item.Pid == process.Id && item.Title == "Stream Privacy - finestra di prova") { result = item; return true; }
                return false;
            }, 10000, "Harmless demo HWND did not appear.");
            return result;
        }

        private static string JournalMarker(string recovery, int ownerPid)
        {
            foreach (string path in Directory.GetFiles(recovery, "*.xml"))
            {
                XmlDocument document = new XmlDocument(); document.XmlResolver = null;
                using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    document.Load(reader);
                XmlElement root = document.DocumentElement;
                if (root != null && root.GetAttribute("ownerPid") == ownerPid.ToString(CultureInfo.InvariantCulture))
                    return root.GetAttribute("marker");
            }
            throw new Exception("No isolated recovery journal matched the terminated test worker.");
        }

        private static void SetAffinity(IntPtr handle, uint affinity)
        {
            if (!Native.SetCapturePrivacy(handle, affinity)) throw new Exception("Unable to set test affinity: " + Native.Error(Marshal.GetLastWin32Error()));
            AssertAffinity(handle, affinity, "Test affinity setup");
        }

        private static bool AffinityIs(IntPtr handle, uint expected)
        {
            uint observed;
            return Native.GetWindowDisplayAffinity(handle, out observed) && observed == expected;
        }

        private static void AssertAffinity(IntPtr handle, uint expected, string context)
        {
            if (!AffinityIs(handle, expected)) throw new Exception(context + ": expected affinity " + expected.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static string Quote(string value) { return "\"" + value + "\""; }

        private static void Kill(Process process)
        {
            if (process.HasExited) throw new Exception("Process exited before the forced-termination test.");
            process.Kill();
            WaitForExit(process, 5000, "Forced termination did not complete.");
        }

        private static void WaitForExit(Process process, int milliseconds, string failure)
        {
            if (!process.WaitForExit(milliseconds)) throw new TimeoutException(failure);
        }

        private static void WaitUntil(Func<bool> condition, int milliseconds, string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException(failure);
                Application.DoEvents();
                Thread.Sleep(50);
            }
        }
    }
}
