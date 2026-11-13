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
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--profile-path") { File.WriteAllText(args[1], SettingStore.DefaultPath); return 0; }
            if (args.Length > 0 && args[0] == "--capture-test")
            {
                using (CaptureTestForm capture = new CaptureTestForm(Array.IndexOf(args, "--light") < 0, LanguageArgument(args) ?? new AppSettings().Language)) Application.Run(capture);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--demo")
            {
                Form demo = new Form { Text = "Stream Privacy - finestra di prova", Size = new Size(560, 340), StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(30, 58, 100) };
                demo.Controls.Add(new Label { Text = "CONTENUTO DI PROVA\n\nSe la protezione funziona nel metodo di cattura\nche stai usando, questo testo non appare nello stream.\n\nQuesta finestra non contiene dati personali.", ForeColor = Color.White, Font = new Font("Segoe UI", 14), TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill });
                Application.Run(demo); return 0;
            }
            if (args.Length > 0 && args[0] == "--self-test") return SelfTest(args.Length > 1 ? args[1] : "self-test.txt");
            if (args.Length > 0 && args[0] == "--integration-test") return IntegrationTest(args.Length > 1 ? args[1] : "integration-test.txt");
            if (args.Length > 0 && args[0] == "--regression-test") return RegressionTest(args.Length > 1 ? args[1] : "regression-test.txt");
            if (args.Length > 0 && args[0] == "--capture-demo-test") return CaptureDemoTest(args.Length > 1 ? args[1] : "capture-demo-test.txt");
            if (args.Length > 0 && args[0] == "--tray-restore-test") return TrayRestoreTests.Run(args.Length > 1 ? args[1] : "tray-restore-test.txt");
            if (args.Length > 0 && args[0] == "--release-readiness-test") return ReleaseReadinessTests.Run(args.Length > 1 ? args[1] : "release-readiness-test.txt");
            if (args.Length > 1 && args[0] == "--render")
            {
                using (MainForm form = new MainForm(true, null, LanguageArgument(args)))
                {
                    if (Array.IndexOf(args, "--light") >= 0) ((CheckBox)form.Controls.Find("themeToggle", true)[0]).Checked = false;
                    form.Start(false); Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(args[1]); }
                    form.ClosePreview(); return 0;
                }
            }
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
            bool created;
            using (Mutex single = new Mutex(true, "Local\\StreamPrivacy." + user, out created))
            {
                if (!created) { Native.PostMessage(new IntPtr(0xffff), MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero); return 0; }
                try {
                    using (MainForm form = new MainForm(false, null, LanguageArgument(args)))
                    {
                        form.Start(args.Length > 0 && args[0] == "--tray");
                        Application.Run();
                    }
                } finally { single.ReleaseMutex(); }
            }
            return 0;
        }

        private static string LanguageArgument(string[] args)
        {
            int index = Array.IndexOf(args, "--language");
            if (index < 0) return null;
            if (index + 1 < args.Length && (args[index + 1] == "en" || args[index + 1] == "it")) return args[index + 1];
            throw new ArgumentException("Use --language en or --language it.");
        }
        private static WindowItem DemoWindow(Process process)
        {
            foreach (WindowItem item in MainForm.EnumerateWindows(true))
                if (item.Pid == process.Id && item.Title == "Stream Privacy - finestra di prova") return item;
            return null;
        }
        private static void PumpUntil(Func<bool> condition, string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception(failure); Application.DoEvents(); Thread.Sleep(25); }
        }
        private static int IntegrationTest(string report)
        {
            List<string> lines = new List<string>(); Process demo = null; MainForm controller = null;
            try
            {
                SettingStore testStore = new SettingStore(Path.GetFullPath(report) + ".settings.xml");
                AppSettings choices = new AppSettings(); choices.ChatOnly = false;
                choices.AddApp(Application.ExecutablePath); testStore.Save(choices);
                AppSettings loaded = testStore.Load();
                if (!loaded.AutoProtect || !loaded.ContainsApp(Application.ExecutablePath.ToUpperInvariant())) throw new Exception("Saved rule did not survive reload.");
                lines.Add("PASS: saved executable-path rule reloaded, case-insensitive match.");
                string command = StartupRegistration.CommandForExecutable(@"C:\Example Folder\StreamPrivacy.exe");
                if (command != "\"C:\\Example Folder\\StreamPrivacy.exe\" --tray") throw new Exception("Startup command not quoted correctly.");
                if (StartupRegistration.IsOwnedCommand(command, @"C:\Other\StreamPrivacy.exe")) throw new Exception("Startup path ownership mismatch.");
                lines.Add("PASS: startup command quoted; another executable path not treated as owned. Registry unchanged.");
                controller = new MainForm(false, testStore); controller.Start(true);
                if (controller.Visible) throw new Exception("Tray startup displayed the main form.");
                lines.Add("PASS: controller starts hidden in tray.");
                demo = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--demo") { UseShellExecute = false });
                WindowItem first = null;
                PumpUntil(delegate { first = DemoWindow(demo); uint affinity; return first != null && Native.GetWindowDisplayAffinity(first.Handle, out affinity) && affinity == 0x11; }, "Automatic protection of first window failed.");
                lines.Add("PASS: background watcher automatically protects a new demo window.");
                controller.Show(); controller.Close(); Application.DoEvents();
                if (controller.Visible || controller.IsDisposed) throw new Exception("X did not keep tray controller alive.");
                lines.Add("PASS: closing X hides UI without stopping controller.");
                demo.CloseMainWindow(); if (!demo.WaitForExit(2000)) throw new Exception("Demo did not exit."); demo.Dispose();
                demo = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--demo") { UseShellExecute = false });
                WindowItem reopened = null;
                PumpUntil(delegate { reopened = DemoWindow(demo); uint affinity; return reopened != null && Native.GetWindowDisplayAffinity(reopened.Handle, out affinity) && affinity == 0x11; }, "Reopened app not automatically protected.");
                lines.Add("PASS: re-opened app receives protection through remembered rule.");
                controller.RequestExit(); PumpUntil(delegate { return controller.IsDisposed; }, "Tray controller did not exit.");
                uint restored;
                if (!Native.GetWindowDisplayAffinity(reopened.Handle, out restored) || restored != 0) throw new Exception("Exit did not restore demo.");
                lines.Add("PASS: explicit tray exit restores original window affinity.");
                lines.Add("NOT TESTED: actual Windows login startup; Discord/WhatsApp/Telegram capture output. No real startup registration or real-app changes made.");
                File.WriteAllLines(report, lines.ToArray()); return 0;
            }
            catch (Exception error) { lines.Add("FAIL: " + error); File.WriteAllLines(report, lines.ToArray()); return 1; }
            finally
            {
                if (controller != null && !controller.IsDisposed) { controller.RequestExit(); DateTime until = DateTime.UtcNow.AddSeconds(5); while (!controller.IsDisposed && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(25); } }
                if (demo != null) { try { demo.CloseMainWindow(); if (!demo.WaitForExit(2000)) demo.Kill(); } catch (Exception) { } demo.Dispose(); }
            }
        }

        private static T Field<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(target);
        }
        private static int CaptureDemoTest(string report)
        {
            List<string> lines = new List<string>(); Process demo = null; MainForm controller = null;
            try
            {
                SettingStore testStore = new SettingStore(Path.GetFullPath(report) + ".settings.xml");
                AppSettings initial = new AppSettings(); testStore.Save(initial);
                HashSet<uint> before = new HashSet<uint>(); foreach (WindowItem item in MainForm.EnumerateWindows(true)) before.Add(item.Pid);
                controller = new MainForm(false, testStore); controller.Start(false);
                Control[] controls = controller.Controls.Find("captureTestButton", true);
                if (controls.Length != 1) throw new Exception("Public capture-test button not found.");
                ((Button)controls[0]).PerformClick();
                WindowItem found = null;
                PumpUntil(delegate {
                    foreach (WindowItem item in MainForm.EnumerateWindows(true))
                        if (!before.Contains(item.Pid) && item.ExecutablePath == Application.ExecutablePath && (item.Title == "Stream Privacy - verifica cattura" || item.Title == "Stream Privacy - capture test")) { found = item; return true; }
                    return false;
                }, "Capture-test window not found.");
                demo = Process.GetProcessById((int)found.Pid);
                uint affinity;
                if (!Native.GetWindowDisplayAffinity(found.Handle, out affinity) || affinity != 0x11) throw new Exception("New capture test did not protect itself on startup.");
                lines.Add("PASS: clicking the public capture-test button opens a separately protected window; Windows confirms affinity 0x11.");
                AppSettings after = testStore.Load();
                if (after.ProtectedApps.Count != 0 || after.ChatOnly != initial.ChatOnly || after.AutoProtect != initial.AutoProtect) throw new Exception("Opening capture test changed saved choices.");
                lines.Add("PASS: no app rule, filter change or saved-choice change is needed to protect the public test window.");
                MainForm form = controller; controller.RequestExit(); PumpUntil(delegate { return form.IsDisposed; }, "Controller did not exit.");
                if (!Native.GetWindowDisplayAffinity(found.Handle, out affinity) || affinity != 0x11) throw new Exception("Self-protection was incorrectly tied to controller lifetime.");
                lines.Add("PASS: test window keeps its own protection after the controller exits.");
                lines.Add("NOT TESTED: actual OBS/Discord output; local monitor visibility is expected.");
                File.WriteAllLines(report, lines.ToArray()); return 0;
            }
            catch (Exception error) { lines.Add("FAIL: " + error); File.WriteAllLines(report, lines.ToArray()); return 1; }
            finally {
                if (controller != null && !controller.IsDisposed) { controller.RequestExit(); DateTime until = DateTime.UtcNow.AddSeconds(5); while (!controller.IsDisposed && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(25); } }
                if (demo != null) { try { demo.CloseMainWindow(); if (!demo.WaitForExit(2000)) demo.Kill(); } catch (Exception) { } demo.Dispose(); }
            }
        }
        private static void RefreshController(MainForm form)
        {
            typeof(MainForm).GetMethod("RefreshWindows", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(form, null);
        }
        private static ListViewItem AppItem(ListView list, string name)
        {
            foreach (ListViewItem row in list.Items) if (row.Text == name) return row;
            return null;
        }
        private static int RegressionTest(string report)
        {
            List<string> lines = new List<string>(); Process first = null; Process second = null; MainForm controller = null;
            try
            {
                string testRoot = Path.GetFullPath(report) + ".apps"; Directory.CreateDirectory(testRoot);
                string appA = Path.Combine(testRoot, "PrivacyDemoA.exe"), appB = Path.Combine(testRoot, "PrivacyDemoB.exe");
                File.Copy(Application.ExecutablePath, appA, true); File.Copy(Application.ExecutablePath, appB, true);
                File.Copy(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PrivacyHook64.dll"), Path.Combine(testRoot, "PrivacyHook64.dll"), true);
                SettingStore testStore = new SettingStore(Path.GetFullPath(report) + ".settings.xml");
                AppSettings choices = new AppSettings(); choices.ChatOnly = false; testStore.Save(choices);
                first = Process.Start(new ProcessStartInfo(appA, "--demo") { UseShellExecute = false });
                second = Process.Start(new ProcessStartInfo(appB, "--demo") { UseShellExecute = false });
                controller = new MainForm(false, testStore); controller.Start(false);
                MainForm form = controller; ListView live = Field<ListView>(controller, "windows"); Button apply = Field<Button>(controller, "protect");
                if (!live.CheckBoxes) throw new Exception("App list has no checkboxes.");
                PumpUntil(delegate { return !Field<bool>(form, "busy") && AppItem(live, "PrivacyDemoA") != null && AppItem(live, "PrivacyDemoB") != null; }, "Demo app rows not found.");
                ListViewItem rowA = AppItem(live, "PrivacyDemoA"), rowB = AppItem(live, "PrivacyDemoB");
                rowA.Checked = true; rowB.Checked = true; rowA.Selected = true;
                if (!apply.Enabled) throw new Exception("Apply disabled after checking multiple apps.");
                RefreshController(controller);
                if (!Field<bool>(controller, "busy")) throw new Exception("Background scan did not start.");
                if (!apply.Enabled) throw new Exception("Apply flickers disabled during background scan.");
                apply.PerformClick();
                PumpUntil(delegate { AppSettings saved = testStore.Load(); return saved.ContainsApp(appA) && saved.ContainsApp(appB); }, "Click during background scan was lost.");
                WindowItem windowA = null, windowB = null;
                PumpUntil(delegate {
                    windowA = DemoWindow(first); windowB = DemoWindow(second); uint a, b;
                    return windowA != null && windowB != null && Native.GetWindowDisplayAffinity(windowA.Handle, out a) && Native.GetWindowDisplayAffinity(windowB.Handle, out b) && a == 0x11 && b == 0x11;
                }, "Batch apply did not protect both apps.");
                lines.Add("PASS: two different app paths can be checked and protected together; both rules saved.");
                lines.Add("PASS: apply stays enabled during scanning and a click during the scan is executed.");
                PumpUntil(delegate { return !Field<bool>(form, "busy"); }, "Controller did not become idle.");
                int enabledChanges = 0; apply.EnabledChanged += delegate { enabledChanges++; };
                for (int scan = 0; scan < 4; scan++)
                {
                    RefreshController(controller); PumpUntil(delegate { return !Field<bool>(form, "busy"); }, "Scan did not finish.");
                    if (!Object.ReferenceEquals(rowA, AppItem(live, "PrivacyDemoA")) || !Object.ReferenceEquals(rowB, AppItem(live, "PrivacyDemoB"))) throw new Exception("Unchanged app rows were recreated.");
                    if (!rowA.Checked || !rowB.Checked || !rowA.Selected) throw new Exception("Refresh lost a checkbox or selection.");
                }
                if (enabledChanges != 0) throw new Exception("Apply enabled state changed during idle scans: " + enabledChanges);
                lines.Add("PASS: four unchanged scans preserve row objects, checkbox states, selection and button enabled state.");
                rowA.Checked = false; apply.PerformClick();
                PumpUntil(delegate { uint a, b; return Native.GetWindowDisplayAffinity(windowA.Handle, out a) && Native.GetWindowDisplayAffinity(windowB.Handle, out b) && a == 0 && b == 0x11; }, "Unchecking one app did not restore it independently.");
                AppSettings after = testStore.Load(); if (after.ContainsApp(appA) || !after.ContainsApp(appB)) throw new Exception("Checkbox rule changes not saved.");
                lines.Add("PASS: unchecking one app and applying restores only that app and updates saved rules.");
                controller.RequestExit(); PumpUntil(delegate { return form.IsDisposed; }, "Controller did not exit.");
                uint restored; if (!Native.GetWindowDisplayAffinity(windowB.Handle, out restored) || restored != 0) throw new Exception("Remaining app not restored on exit.");
                lines.Add("PASS: exit restores remaining app; no real apps or startup settings modified.");
                File.WriteAllLines(report, lines.ToArray()); return 0;
            }
            catch (Exception error) { lines.Add("FAIL: " + error); File.WriteAllLines(report, lines.ToArray()); return 1; }
            finally
            {
                if (controller != null && !controller.IsDisposed) { controller.RequestExit(); DateTime until = DateTime.UtcNow.AddSeconds(5); while (!controller.IsDisposed && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(25); } }
                foreach (Process process in new Process[] { first, second }) if (process != null) { try { process.CloseMainWindow(); if (!process.WaitForExit(2000)) process.Kill(); } catch (Exception) { } process.Dispose(); }
            }
        }

        private static int SelfTest(string report)
        {
            List<string> lines = new List<string>(); Process demo = null; ProtectionSession session = new ProtectionSession();
            try
            {
                demo = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--demo") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                WindowItem item = null;
                for (int attempt = 0; attempt < 60 && item == null; attempt++)
                {
                    foreach (WindowItem candidate in MainForm.EnumerateWindows(true)) if (candidate.Pid == demo.Id && candidate.Title == "Stream Privacy - finestra di prova") { item = candidate; break; }
                    if (item == null) Thread.Sleep(100);
                }
                if (item == null) throw new Exception("Test window did not appear.");
                uint affinity;
                if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 0) throw new Exception("Unexpected initial affinity.");
                lines.Add("PASS: separate x64 process started; initial affinity is NONE.");
                session.Protect(item);
                if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 0x11) throw new Exception("Exclusion not confirmed.");
                lines.Add("PASS: cross-process helper set WDA_EXCLUDEFROMCAPTURE; Windows readback is 0x11.");
                session.Restore(item.Handle);
                if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 0) throw new Exception("Restore failed.");
                lines.Add("PASS: original affinity restored to NONE.");
                if (!Native.SetCapturePrivacy(item.Handle, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
                session.Protect(item); session.Restore(item.Handle);
                if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 1) throw new Exception("Existing monitor protection was not preserved.");
                lines.Add("PASS: existing WDA_MONITOR protection preserved by protect/restore.");
                if (!Native.SetCapturePrivacy(item.Handle, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                if (Native.SetCapturePrivacy(IntPtr.Zero, 0x11)) throw new Exception("Invalid handle accepted.");
                lines.Add("PASS: invalid window rejected.");
                if (Native.SetCapturePrivacy(item.Handle, 42)) throw new Exception("Invalid affinity accepted.");
                lines.Add("PASS: unsupported affinity rejected.");
                session.Protect(item);
                if (session.RestoreAll().Count != 0) throw new Exception("Restore all failed.");
                if (!Native.GetWindowDisplayAffinity(item.Handle, out affinity) || affinity != 0) throw new Exception("Restore all readback failed.");
                lines.Add("PASS: restore all clears session changes.");
                lines.Add("NOT TESTED: WhatsApp, Telegram, actual OBS/Discord output, screenshots, new-window handling.");
                File.WriteAllLines(report, lines.ToArray()); return 0;
            }
            catch (Exception error) { lines.Add("FAIL: " + error.ToString()); File.WriteAllLines(report, lines.ToArray()); return 1; }
            finally
            {
                session.RestoreAll();
                if (demo != null) { try { demo.CloseMainWindow(); if (!demo.WaitForExit(2000)) demo.Kill(); } catch (Exception) { } demo.Dispose(); }
            }
        }
    }
}
