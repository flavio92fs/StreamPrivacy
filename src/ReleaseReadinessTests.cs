using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace StreamPrivacy
{
    internal static class ReleaseReadinessTests
    {
        internal static int Run(string report)
        {
            List<string> lines = new List<string>(); Process signal = null; Process other = null; MainForm form = null;
            try
            {
                AppSettings defaults = new AppSettings();
                if (!defaults.DarkMode || defaults.ChatOnly) throw new Exception("New profiles must default to dark and all apps.");
                string legacy = Path.GetFullPath(report) + ".legacy.xml";
                File.WriteAllText(legacy, "<streamPrivacy version=\"1\"><chatOnly>true</chatOnly><autoProtect>false</autoProtect><protectedApps /></streamPrivacy>");
                AppSettings old = new SettingStore(legacy).Load();
                if (!old.DarkMode || !old.ChatOnly || old.AutoProtect) throw new Exception("Legacy choices were not preserved.");
                lines.Add("PASS: dark/all-app defaults; legacy profiles retain previous filter/protection choices and gain dark mode.");
                foreach (string name in new string[] { "Signal", "signal-beta.exe", "ms-teams", "Slack", "Telegram", "DiscordCanary", "Element" })
                    if (!MainForm.IsChatApp(name)) throw new Exception("Chat filter missed " + name);
                lines.Add("PASS: chat filter recognizes Signal and other known chat process names; detection is not a capture-compatibility claim.");

                string root = Path.GetFullPath(report) + ".apps"; Directory.CreateDirectory(root);
                string portableExe = Path.Combine(root, "PortableProfile.exe"); File.Copy(Application.ExecutablePath, portableExe, true);
                string marker = Path.Combine(root, "portable.flag"); File.WriteAllText(marker, "Stream Privacy portable profile");
                string profileReport = Path.Combine(root, "profile.txt");
                using (Process profile = Process.Start(new ProcessStartInfo(portableExe, "--profile-path \"" + profileReport + "\"") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }))
                    if (!profile.WaitForExit(5000) || profile.ExitCode != 0) throw new Exception("Portable profile probe failed.");
                string expected = Path.Combine(root, "Data", "settings.xml");
                if (!String.Equals(File.ReadAllText(profileReport), expected, StringComparison.OrdinalIgnoreCase) || File.Exists(expected)) throw new Exception("Portable settings path is wrong or probe wrote settings.");
                File.Delete(marker);
                using (Process profile = Process.Start(new ProcessStartInfo(portableExe, "--profile-path \"" + profileReport + "\"") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden }))
                    if (!profile.WaitForExit(5000) || profile.ExitCode != 0) throw new Exception("Installed profile probe failed.");
                string installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreamPrivacy", "settings.xml");
                if (!String.Equals(File.ReadAllText(profileReport), installed, StringComparison.OrdinalIgnoreCase)) throw new Exception("Installed settings path is wrong.");
                lines.Add("PASS: portable marker uses adjacent Data/settings.xml; installed mode uses LocalAppData. Probes do not write user settings.");

                string signalExe = Path.Combine(root, "Signal.exe"), otherExe = Path.Combine(root, "UnlistedDemo.exe");
                File.Copy(Application.ExecutablePath, signalExe, true); File.Copy(Application.ExecutablePath, otherExe, true);
                File.Copy(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PrivacyHook64.dll"), Path.Combine(root, "PrivacyHook64.dll"), true);
                signal = Process.Start(new ProcessStartInfo(signalExe, "--demo") { UseShellExecute = false });
                other = Process.Start(new ProcessStartInfo(otherExe, "--demo") { UseShellExecute = false });
                SettingStore settings = new SettingStore(Path.GetFullPath(report) + ".settings.xml");
                AppSettings local = new AppSettings(); local.ChatOnly = true; local.AutoProtect = false; settings.Save(local);
                form = new MainForm(false, settings, "en"); form.Start(false);
                MainForm active = form; ListView list = Field<ListView>(form, "windows");
                PumpUntil(delegate { return Find(list, "Signal") != null && !Field<bool>(active, "busy"); }, "Synthetic Signal not detected by chat filter.");
                if (Find(list, "UnlistedDemo") != null) throw new Exception("Chat filter did not filter non-chat demo.");
                foreach (WindowItem item in MainForm.EnumerateWindows())
                    if (item.Pid == signal.Id && item.Title.Length != 0) throw new Exception("Production enumeration returned a window title.");
                lines.Add("PASS: real UI detects a harmless Signal-named demo under chat filter; normal enumeration returns no window titles. Not a real Signal capture test.");

                Control[] theme = form.Controls.Find("themeToggle", true); if (theme.Length != 1) throw new Exception("Theme toggle missing.");
                CheckBox toggle = (CheckBox)theme[0];
                if (!toggle.Checked || form.BackColor.GetBrightness() > 0.3) throw new Exception("Dark theme not applied.");
                toggle.Checked = false;
                if (settings.Load().DarkMode || form.BackColor.GetBrightness() < 0.7) throw new Exception("Light theme not applied/persisted.");
                toggle.Checked = true; if (!settings.Load().DarkMode) throw new Exception("Dark theme not saved.");
                string screenshot = Path.GetFullPath(report) + ".dark.png"; Application.DoEvents();
                // Render the normal production startup control enabled; never invoke it.
                CheckBox startup = Field<CheckBox>(form, "startup"); startup.Enabled = true;
                using (Bitmap picture = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size)); picture.Save(screenshot); }
                startup.Enabled = false;
                LanguagePicker language = (LanguagePicker)form.Controls.Find("languageSelector", true)[0];
                if (language.Text != "English" || list.Columns[1].Text != "Windows" || Field<System.Windows.Forms.Button>(form, "protect").Text != "Apply selection" || Find(list, "Signal").SubItems[2].Text != "No exclusion") throw new Exception("English interface was not applied to real controls.");
                File.Copy(screenshot, Path.GetFullPath(report) + ".dark-en.png", true);
                language.SelectedItem = "Italiano"; Application.DoEvents();
                if (settings.Load().Language != "it" || list.Columns[1].Text != "Finestre" || Find(list, "Signal").SubItems[2].Text != "Nessuna esclusione") throw new Exception("Italian switch did not update and persist real controls.");
                startup.Enabled = true;
                using (Bitmap picture = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size)); picture.Save(Path.GetFullPath(report) + ".dark-it.png"); }
                startup.Enabled = false;
                language.SelectedItem = "English"; Application.DoEvents();
                if (settings.Load().Language != "en" || list.Columns[1].Text != "Windows") throw new Exception("English choice did not persist.");
                lines.Add("PASS: actual English/Italian controls, headers and app states switch correctly; language choice persists. Screenshots are rendered from real controls.");
                CheckBox filter = Field<CheckBox>(form, "chatOnly"); filter.Checked = false;
                PumpUntil(delegate { return Find(list, "UnlistedDemo") != null && !Field<bool>(active, "busy"); }, "All-app view failed to show unknown app.");
                lines.Add("PASS: dark/light toggle updates palette and persists choice; disabling chat filter reveals an unlisted app.");
                form.RequestExit(); PumpUntil(delegate { return active.IsDisposed; }, "Controller failed to close.");
                lines.Add("NOT TESTED: actual Signal client capture or universal app compatibility. No startup entry or real app settings modified.");
                File.WriteAllLines(report, lines.ToArray()); return 0;
            }
            catch (Exception error) { lines.Add("FAIL: " + error); File.WriteAllLines(report, lines.ToArray()); return 1; }
            finally
            {
                if (form != null && !form.IsDisposed) { form.RequestExit(); DateTime deadline = DateTime.UtcNow.AddSeconds(5); while (!form.IsDisposed && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(25); } }
                foreach (Process child in new Process[] { signal, other }) if (child != null) { try { child.CloseMainWindow(); if (!child.WaitForExit(2000)) child.Kill(); } catch (Exception) { } child.Dispose(); }
            }
        }
        private static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
        private static ListViewItem Find(ListView list, string name) { foreach (ListViewItem item in list.Items) if (item.Text == name) return item; return null; }
        private static void PumpUntil(Func<bool> condition, string error) { DateTime deadline = DateTime.UtcNow.AddSeconds(10); while (!condition()) { if (DateTime.UtcNow > deadline) throw new Exception(error); Application.DoEvents(); Thread.Sleep(25); } }
    }
}
