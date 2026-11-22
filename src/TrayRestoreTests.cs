using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StreamPrivacy
{
    internal static class TrayRestoreTests
    {
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int state);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

        internal static int Run(string report)
        {
            List<string> lines = new List<string>();
            Process demo = null;
            MainForm controller = null;
            ProtectionSession externalSession = new ProtectionSession(Path.GetFullPath(report) + ".external-recovery");
            try
            {
                SettingStore isolated = new SettingStore(Path.GetFullPath(report) + ".settings.xml");
                AppSettings settings = new AppSettings();
                settings.AutoProtect = false;
                isolated.Save(settings);
                demo = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--demo") {
                    UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
                });
                WindowItem demoWindow = null;
                PumpUntil(delegate {
                    foreach (WindowItem candidate in MainForm.EnumerateWindows(true))
                        if (candidate.Pid == demo.Id && candidate.Title == "Stream Privacy - finestra di prova")
                        { demoWindow = candidate; return true; }
                    return false;
                }, "Separate harmless demo window did not appear.");
                externalSession.Protect(demoWindow);
                AssertExcluded(demoWindow.Handle, "external demo before restoration tests");
                lines.Add("PASS: independent demo process protected; only isolated settings used.");

                controller = new MainForm(false, isolated);
                controller.Start(true);
                if (controller.Visible || Native.IsWindowVisible(controller.Handle))
                    throw new Exception("Tray startup unexpectedly displayed the controller.");
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: actual OpenWindow path restores hidden tray startup; native visibility and on-screen title area confirmed.");

                controller.Close();
                Application.DoEvents();
                if (controller.IsDisposed || controller.Visible)
                    throw new Exception("Closing X did not leave controller alive and hidden.");
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: X-to-tray followed by actual OpenWindow restores the protected main window.");

                controller.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: minimize-to-tray restores without returning to hidden/minimized state.");

                ShowWindow(controller.Handle, 0);
                if (Native.IsWindowVisible(controller.Handle))
                    throw new Exception("Native-only hidden-window precondition failed.");
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: native-only hidden HWND is restored, independent of WinForms visibility bookkeeping.");

                controller.Bounds = new Rectangle(100000, 100000, controller.Width, controller.Height);
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: off-screen window returns with an accessible title area on a current monitor.");

                controller.Hide();
                MethodInfo recreate = typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic);
                if (recreate == null) throw new Exception("RecreateHandle test entry point is missing.");
                recreate.Invoke(controller, null);
                Open(controller);
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: recreated hidden HWND restores with capture exclusion 0x11 re-established.");

                controller.Hide();
                if (!Native.PostMessage(controller.Handle, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero))
                    throw new Exception("Unable to post registered show request.");
                AssertRestored(controller, demoWindow);
                lines.Add("PASS: registered ShowMessage/WndProc reopen path also restores visibility and capture exclusion.");

                controller.Close(); Application.DoEvents();
                MainForm liveController = controller;
                FieldInfo busy = typeof(MainForm).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic);
                PumpUntil(delegate { return !(bool)busy.GetValue(liveController); }, "Controller did not become idle before recovery.");
                MethodInfo refresh = typeof(MainForm).GetMethod("RefreshWindows", BindingFlags.Instance | BindingFlags.NonPublic);
                refresh.Invoke(controller, null);
                int createdHandles = 0; controller.HandleCreated += delegate { createdHandles++; };
                MethodInfo recovery = typeof(MainForm).GetMethod("RequestOpenWindow", BindingFlags.Instance | BindingFlags.NonPublic);
                recovery.Invoke(controller, new object[] { true });
                AssertRestored(controller, demoWindow);
                if (createdHandles == 0) throw new Exception("Recovery did not recreate the controller's native window.");
                lines.Add("PASS: recovery menu path waits for the scan, recreates only the main window and reopens it protected.");
                lines.Add("PASS: independent protected app remained at affinity 0x11 throughout every UI restoration case.");

                externalSession.Restore(demoWindow.Handle);
                uint restored;
                if (!Native.GetWindowDisplayAffinity(demoWindow.Handle, out restored) || restored != 0)
                    throw new Exception("Independent demo's original affinity was not restored.");
                MainForm form = controller;
                controller.RequestExit();
                PumpUntil(delegate { return form.IsDisposed; }, "Controller did not exit after restore tests.");
                lines.Add("PASS: explicit exit completed; independent demo restored to its original affinity.");
                lines.Add("NOT REPRODUCED: actual Discord stream start/stop or a live capture session; these tests exercise window lifecycle and restoration states.");
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
                externalSession.RestoreAll();
                if (controller != null && !controller.IsDisposed)
                {
                    controller.RequestExit();
                    DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                    while (!controller.IsDisposed && DateTime.UtcNow < deadline)
                    { Application.DoEvents(); Thread.Sleep(25); }
                }
                if (demo != null)
                {
                    try { demo.CloseMainWindow(); if (!demo.WaitForExit(2000)) demo.Kill(); }
                    catch (Exception) { }
                    demo.Dispose();
                }
            }
        }

        private static void Open(MainForm form)
        {
            MethodInfo open = typeof(MainForm).GetMethod("OpenWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            if (open == null) throw new Exception("OpenWindow test entry point is missing.");
            open.Invoke(form, null);
        }

        private static void AssertRestored(MainForm form, WindowItem independentDemo)
        {
            try { PumpUntil(delegate {
                if (form.IsDisposed || !form.Visible || !Native.IsWindowVisible(form.Handle) || IsIconic(form.Handle)) return false;
                Rect nativeBounds;
                if (!GetWindowRect(form.Handle, out nativeBounds)) return false;
                Rectangle title = new Rectangle(nativeBounds.Left, nativeBounds.Top,
                    Math.Max(1, nativeBounds.Right - nativeBounds.Left), 32);
                bool accessible = false;
                foreach (Screen screen in Screen.AllScreens)
                {
                    Rectangle overlap = Rectangle.Intersect(screen.WorkingArea, title);
                    if (overlap.Width >= Math.Min(160, title.Width) && overlap.Height >= 24)
                    { accessible = true; break; }
                }
                return accessible;
            }, "Main window did not become natively visible, non-minimized and accessible on-screen."); }
            catch (Exception error) {
                Rect last; GetWindowRect(form.Handle, out last);
                throw new Exception(error.Message + " ManagedVisible=" + form.Visible + ", NativeVisible=" + Native.IsWindowVisible(form.Handle) + ", ManagedState=" + form.WindowState + ", NativeIconic=" + IsIconic(form.Handle) + ", Bounds=" + last.Left + "," + last.Top + "," + last.Right + "," + last.Bottom, error);
            }
            AssertExcluded(form.Handle, "restored main window");
            AssertExcluded(independentDemo.Handle, "independent app while controller reopens");
        }

        private static void AssertExcluded(IntPtr window, string context)
        {
            uint affinity;
            if (!Native.GetWindowDisplayAffinity(window, out affinity) || affinity != 0x11)
                throw new Exception("Capture exclusion not confirmed for " + context + ".");
        }

        private static void PumpUntil(Func<bool> condition, string failure)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline) throw new Exception(failure);
                Application.DoEvents(); Thread.Sleep(25);
            }
        }
    }
}
