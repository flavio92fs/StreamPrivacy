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
    internal sealed class BufferedListView : ListView
    {
        internal bool DarkTheme;
        internal BufferedListView()
        {
            DoubleBuffered = true; OwnerDraw = true;
            DrawColumnHeader += delegate(object sender, DrawListViewColumnHeaderEventArgs args) {
                using (Brush fill = new SolidBrush(Theme.Raised(DarkTheme))) args.Graphics.FillRectangle(fill, args.Bounds);
                using (Pen separator = new Pen(Theme.Border(DarkTheme)))
                    args.Graphics.DrawLine(separator, args.Bounds.Right - 1, args.Bounds.Top + 7, args.Bounds.Right - 1, args.Bounds.Bottom - 7);
                Rectangle text = args.Bounds; text.Inflate(-9, 0);
                TextRenderer.DrawText(args.Graphics, args.Header.Text, Font, text, Theme.Muted(DarkTheme),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            DrawItem += delegate(object sender, DrawListViewItemEventArgs args) { args.DrawDefault = true; };
            DrawSubItem += delegate(object sender, DrawListViewSubItemEventArgs args) { args.DrawDefault = true; };
            SizeChanged += delegate { FillLastColumn(); };
        }
        private void FillLastColumn()
        {
            if (Columns.Count == 0) return;
            int used = 0; for (int index = 0; index < Columns.Count - 1; index++) used += Columns[index].Width;
            int width = Math.Max(110, ClientSize.Width - used - 2);
            if (Columns[Columns.Count - 1].Width != width) Columns[Columns.Count - 1].Width = width;
        }
    }

    internal sealed class AppRow
    {
        internal string Path;
        internal string App;
        internal readonly List<WindowItem> Windows = new List<WindowItem>();
    }

    internal sealed class MainForm : Form
    {
        internal static readonly uint ShowMessage = Native.RegisterWindowMessage("StreamPrivacy.Show.{BFE25F87-070A-4012-9103-C59799CA5ED1}");
        private readonly ProtectionSession session;
        private readonly ListView windows = new BufferedListView();
        private readonly ListView savedApps = new BufferedListView();
        private readonly Label status = new Label();
        private readonly Label summary = new Label();
        private readonly CheckBox chatOnly = new CheckBox();
        private readonly CheckBox automatic = new CheckBox();
        private readonly CheckBox startup = new CheckBox();
        private readonly CheckBox darkMode = new CheckBox();
        private readonly LanguagePicker languageSelector = new LanguagePicker();
        private readonly ToolTip filterHint = new ToolTip();
        private readonly Button protect = new ThemedButton();
        private readonly Button remove = new ThemedButton();
        private readonly Button pause = new ThemedButton();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem trayAutomatic = new ToolStripMenuItem("Protezione automatica");
        private readonly Dictionary<IntPtr, DateTime> retryAfter = new Dictionary<IntPtr, DateTime>();
        private readonly bool preview;
        private AppSettings settings;
        private SettingStore store;
        private string settingsError;
        private bool syncing;
        private bool busy;
        private bool exitRequested;
        private bool allowClose;
        private bool trayHintShown;
        private bool pendingPause;
        private int activeTab;
        private bool scanning;
        private bool completing;
        private bool updatingChoices;
        private bool restoringWindow;
        private bool openPosted;
        private bool recoveryPending;
        private bool statusError;
        private string statusSource;
        private UiLanguage ui;
        private readonly Dictionary<string, bool> stagedApps = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ListViewItem> liveRows = new Dictionary<string, ListViewItem>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Action> pendingActions = new Queue<Action>();

        internal MainForm(bool previewMode = false, SettingStore isolatedStore = null, string languageOverride = null)
        {
            preview = previewMode;
            session = new ProtectionSession(isolatedStore == null ? null : isolatedStore.RecoveryDirectory);
            settings = new AppSettings();
            if (!preview)
            {
                store = isolatedStore ?? new SettingStore(SettingStore.DefaultPath);
                try { settings = store.Load(); }
                catch (Exception error) { settingsError = error.Message; store = null; }
            }
            if (languageOverride != null) settings.Language = UiLanguage.Normalize(languageOverride);
            ui = new UiLanguage(settings.Language);
            Text = "Stream Privacy 0.2.6"; Font = new Font("Segoe UI", 10); DoubleBuffered = true;
            BackColor = Color.FromArgb(246, 248, 251); ForeColor = Color.FromArgb(28, 39, 55);
            ClientSize = new Size(1000, 720); MinimumSize = new Size(970, 740);
            StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Dpi;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamPrivacy.Icon"))
                if (stream != null) Icon = new Icon(stream, new Size(32, 32));

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(24) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new int[] { 46, 50, 44, 42 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new int[] { 58, 78, 82 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "Le tue chat, fuori dallo stream", Font = new Font("Segoe UI", 21, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
            layout.Controls.Add(new Label { Text = "Spunta tutte le app da proteggere e premi Applica selezione. Le scelte vengono ricordate.\nIl tasto X lascia Stream Privacy attivo nell'area vicino all'orologio.", Dock = DockStyle.Fill }, 0, 1);

            FlowLayoutPanel options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            startup.Text = "Avvia con Windows"; startup.AutoSize = true; startup.Margin = new Padding(0, 9, 30, 0);
            automatic.Text = "Protezione automatica"; automatic.AutoSize = true; automatic.Margin = new Padding(0, 9, 28, 0);
            automatic.Checked = settings.AutoProtect;
            if (!preview && isolatedStore == null) { try { startup.Checked = StartupRegistration.IsEnabled(); } catch (Exception error) { settingsError = error.Message; } }
            startup.Enabled = isolatedStore == null;
            startup.CheckedChanged += StartupChanged; automatic.CheckedChanged += AutomaticChanged;
            options.Controls.Add(startup); options.Controls.Add(automatic);
            darkMode.Name = "themeToggle"; darkMode.Text = "Tema scuro"; darkMode.AutoSize = true;
            darkMode.Margin = new Padding(0, 9, 28, 0); darkMode.Checked = settings.DarkMode;
            darkMode.CheckedChanged += delegate {
                if (syncing) return;
                settings.DarkMode = darkMode.Checked; ApplyTheme(); SaveSettings();
            };
            options.Controls.Add(darkMode);
            summary.Name = "summaryText"; summary.AutoSize = true; summary.Margin = new Padding(0, 10, 0, 0); options.Controls.Add(summary);
            layout.Controls.Add(options, 0, 2);

            FlowLayoutPanel filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            chatOnly.Text = "Solo app di chat"; chatOnly.AutoSize = true; chatOnly.Checked = settings.ChatOnly; chatOnly.Margin = new Padding(0, 8, 22, 0);
            filterHint.SetToolTip(chatOnly, "Include Signal, WhatsApp, Telegram, Discord, Teams, Slack e altre chat note. Disattiva il filtro per mostrare tutte le app rilevate.");
            chatOnly.CheckedChanged += delegate { if (!syncing) { settings.ChatOnly = chatOnly.Checked; SaveSettings(); RefreshWindows(); } };
            Button refresh = MakeButton("Aggiorna", false); refresh.Click += delegate { RefreshWindows(); };
            Button demo = MakeButton("Finestra di prova", false); demo.Name = "captureTestButton"; demo.Click += delegate {
                using (Process test = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--capture-test" + (settings.DarkMode ? "" : " --light") + " --language " + settings.Language) { UseShellExecute = false })) { }
                SetStatus("La prova richiede automaticamente l'esclusione. Leggi lo stato nella sua finestra: sul tuo monitor resta visibile, controlla il video trasmesso.", false);
            };
            filters.Controls.Add(chatOnly); filters.Controls.Add(refresh); filters.Controls.Add(demo);
            languageSelector.Name = "languageSelector"; languageSelector.Width = 110;
            languageSelector.Margin = new Padding(18, 4, 0, 0); languageSelector.SelectedItem = settings.Language == "it" ? "Italiano" : "English";
            languageSelector.SelectedIndexChanged += delegate {
                if (syncing) return;
                settings.Language = (string)languageSelector.SelectedItem == "Italiano" ? "it" : "en";
                ApplyLanguage(); SaveSettings();
            };
            filters.Controls.Add(languageSelector); layout.Controls.Add(filters, 0, 3);

            TabControl tabs = new ThemedTabControl { Dock = DockStyle.Fill };
            TabPage live = new TabPage("App aperte"); TabPage remembered = new TabPage("App memorizzate");
            SetupList(windows); windows.CheckBoxes = true; windows.Columns.Add("App", 230); windows.Columns.Add("Finestre", 110); windows.Columns.Add("Stato Windows", 300); windows.Columns.Add("Regola salvata", 140);
            SetupList(savedApps); savedApps.Columns.Add("App", 180); savedApps.Columns.Add("Riattivazione", 620);
            windows.SelectedIndexChanged += delegate { UpdateButtons(); }; savedApps.SelectedIndexChanged += delegate { UpdateButtons(); };
            windows.ItemCheck += delegate(object sender, ItemCheckEventArgs args) {
                if (updatingChoices) return;
                AppRow app = windows.Items[args.Index].Tag as AppRow;
                if (app == null || String.IsNullOrEmpty(app.Path)) { args.NewValue = args.CurrentValue; return; }
                stagedApps[app.Path] = args.NewValue == CheckState.Checked;
            };
            live.Controls.Add(windows); remembered.Controls.Add(savedApps); tabs.TabPages.Add(live); tabs.TabPages.Add(remembered);
            tabs.SelectedIndexChanged += delegate { activeTab = tabs.SelectedIndex; UpdateButtons(); }; layout.Controls.Add(tabs, 0, 4);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0), WrapContents = false };
            SetupButton(protect, "Applica selezione", true); SetupButton(remove, "Rimuovi app e ripristina", false); SetupButton(pause, "Pausa e ripristina tutte", false);
            protect.Click += delegate { ProtectSelected(); }; remove.Click += delegate { RemoveSelectedApp(tabs.SelectedIndex == 1); }; pause.Click += delegate { PauseAndRestore(); };
            actions.Controls.Add(protect); actions.Controls.Add(remove); actions.Controls.Add(pause); layout.Controls.Add(actions, 0, 5);
            status.Name = "statusText"; status.Dock = DockStyle.Fill; status.Padding = new Padding(12); status.BackColor = Color.FromArgb(228, 236, 247);
            SetStatus("La conferma di Windows va verificata nell'anteprima dello stream. Le app scelte vengono salvate sul PC.", false);
            layout.Controls.Add(status, 0, 6);
            layout.Controls.Add(new Label { Text = "Le nuove finestre vengono rilevate periodicamente: possono comparire per un breve intervallo prima della protezione.\nSolo app a 64 bit. Notifiche di Windows e audio esclusi. Per chiudere davvero usa Esci dal menu dell'icona.\nOffline: nessuna telemetria e nessuna connessione verso l'esterno.", Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0), ForeColor = Color.FromArgb(85, 95, 110), Font = new Font("Segoe UI", 9) }, 0, 7);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Apri Stream Privacy", null, delegate { RequestOpenWindow(false); });
            menu.Items.Add("Ripristina finestra", null, delegate { RequestOpenWindow(true); });
            trayAutomatic.CheckOnClick = false; trayAutomatic.Checked = settings.AutoProtect;
            trayAutomatic.Click += delegate { automatic.Checked = !automatic.Checked; }; menu.Items.Add(trayAutomatic);
            menu.Items.Add("Pausa e ripristina tutte", null, delegate { PauseAndRestore(); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Esci e ripristina", null, delegate { RequestExit(); });
            tray.ContextMenuStrip = menu; tray.Icon = Icon; tray.Text = "Stream Privacy";
            tray.DoubleClick += delegate { RequestOpenWindow(false); };
            timer.Interval = 750; timer.Tick += delegate { RefreshWindows(); };
            Resize += delegate { if (!preview && !restoringWindow && WindowState == FormWindowState.Minimized) HideToTray(); };
            FormClosing += OnClosing;
            FormClosed += delegate { tray.Visible = false; tray.Dispose(); timer.Dispose(); filterHint.Dispose(); if (!busy) session.Dispose(); if (!preview) Application.ExitThread(); };
            FillSavedApps(); UpdateButtons(); ApplyTheme(); ApplyLanguage();
        }

        internal void Start(bool hidden)
        {
            // Create the control handle even when starting entirely in the tray.
            IntPtr handle = Handle;
            if (!preview) { tray.Visible = true; timer.Start(); }
            if (!hidden) Show();
            if (settingsError != null)
            {
                SetStatus("Impostazioni non disponibili: " + settingsError, true);
                if (!preview) tray.ShowBalloonTip(8000, Tr("Stream Privacy: controlla le impostazioni"), Tr("Non è stato possibile leggere le impostazioni. Apri il programma dall'icona."), ToolTipIcon.Warning);
            }
            RefreshWindows();
        }
        internal void ClosePreview() { allowClose = true; Close(); }
        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            // A new native window needs its own exclusion, including after recovery.
            if (!Native.SetWindowDisplayAffinity(Handle, 0x11) && status != null)
                SetStatus("Windows non ha confermato l'esclusione della finestra di Stream Privacy. Codice: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error(), true);
            Theme.ApplyTitleBar(this, settings == null || settings.DarkMode);
        }
        protected override void WndProc(ref Message message)
        {
            if ((uint)message.Msg == ShowMessage) { RequestOpenWindow(false); return; }
            base.WndProc(ref message);
        }
        private void RequestOpenWindow(bool recover)
        {
            if (IsDisposed || Disposing || allowClose || exitRequested) return;
            recoveryPending = recoveryPending || recover;
            if (openPosted) return;
            openPosted = true;
            // Leave the tray/menu callback before restoring focus and painting.
            BeginInvoke(new Action(delegate {
                openPosted = false;
                if (IsDisposed || Disposing || allowClose || exitRequested) return;
                if (recoveryPending && (busy || completing)) return;
                if (recoveryPending)
                {
                    recoveryPending = false;
                    restoringWindow = true;
                    try { Hide(); RecreateHandle(); }
                    finally { restoringWindow = false; }
                }
                OpenWindow();
                DrainPending();
            }));
        }
        private void OpenWindow()
        {
            if (IsDisposed || Disposing || allowClose || exitRequested) return;
            restoringWindow = true;
            try
            {
                Rectangle restoredBounds = OnScreenRestoreBounds();
                WindowState = FormWindowState.Normal;
                Bounds = restoredBounds;
                Show();
                // Managed Visible can be true even if native capture/UI code hid HWND.
                Native.ShowWindow(Handle, 9); // SW_RESTORE
                // A hidden minimized HWND can retain Windows' -32000 placement
                // even after managed Bounds/WindowState already look normal.
                Native.SetWindowPos(Handle, IntPtr.Zero, restoredBounds.X, restoredBounds.Y,
                    restoredBounds.Width, restoredBounds.Height, 0x0040); // TOP, SHOW, restore native bounds
                BringToFront(); Activate(); Native.SetForegroundWindow(Handle);
                Invalidate(true); Update();
                Native.RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, 0x0585);
            }
            finally { restoringWindow = false; }
        }
        private Rectangle OnScreenRestoreBounds()
        {
            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            if (bounds.Width <= 0 || bounds.Height <= 0) bounds.Size = Size;
            Rectangle title = new Rectangle(bounds.Left, bounds.Top, Math.Min(bounds.Width, 160), Math.Min(bounds.Height, 40));
            foreach (Screen screen in Screen.AllScreens)
                if (screen.WorkingArea.Contains(title)) return bounds;
            Screen target = Screen.FromPoint(Cursor.Position);
            Rectangle area = target.WorkingArea;
            return new Rectangle(area.Left + Math.Max(0, (area.Width - bounds.Width) / 2),
                area.Top + Math.Max(0, (area.Height - bounds.Height) / 2), bounds.Width, bounds.Height);
        }
        private void HideToTray()
        {
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, Tr("Stream Privacy resta attivo"), Tr("Per riaprirlo, fai doppio clic sullo scudo vicino all'orologio."), ToolTipIcon.Info);
            }
        }
        private void StartupChanged(object sender, EventArgs args)
        {
            if (syncing || preview) return;
            try { StartupRegistration.SetEnabled(startup.Checked); SetStatus(startup.Checked ? "Avvio con Windows attivato: partirà vicino all'orologio al prossimo accesso." : "Avvio con Windows disattivato.", false); }
            catch (Exception error)
            {
                syncing = true; startup.Checked = !startup.Checked; syncing = false;
                SetStatus("Impossibile cambiare l'avvio automatico: " + error.Message, true);
            }
        }
        private void AutomaticChanged(object sender, EventArgs args)
        {
            if (syncing) return;
            settings.AutoProtect = automatic.Checked; trayAutomatic.Checked = automatic.Checked;
            if (SaveSettings()) SetStatus(automatic.Checked ? "Protezione automatica attiva per le app memorizzate." : "Protezione automatica in pausa. Le finestre già protette restano escluse fino al ripristino.", false);
            UpdateSummary(); RefreshWindows();
        }
        private bool SaveSettings()
        {
            if (preview) return true;
            if (store == null) { SetStatus("Salvataggio disabilitato: il file delle impostazioni non è leggibile. " + settingsError, true); return false; }
            try { store.Save(settings); return true; }
            catch (Exception error) { SetStatus("Impossibile salvare le app scelte: " + error.Message, true); return false; }
        }
        private void SetStatus(string text, bool error)
        {
            statusSource = text; status.Text = Tr(text); statusError = error;
            Theme.SetState(status, settings == null || settings.DarkMode, error);
        }
        private string Tr(string text) { return ui == null ? text : ui.Translate(text); }
        private void ApplyLanguage()
        {
            ui.SetLanguage(settings.Language); ui.Apply(this, tray.ContextMenuStrip);
            status.Text = Tr(statusSource);
            filterHint.SetToolTip(chatOnly, Tr("Include Signal, WhatsApp, Telegram, Discord, Teams, Slack e altre chat note. Disattiva il filtro per mostrare tutte le app rilevate."));
            foreach (ListViewItem row in windows.Items)
            {
                row.SubItems[2].Text = Tr(row.SubItems[2].Text);
                row.SubItems[3].Text = Tr(row.SubItems[3].Text);
            }
            foreach (ListViewItem row in savedApps.Items) row.SubItems[1].Text = Tr(row.SubItems[1].Text);
            UpdateSummary();
        }
        private void ApplyTheme()
        {
            Theme.Apply(this, settings.DarkMode);
            Theme.SetState(status, settings.DarkMode, statusError);
            Theme.ApplyMenu(tray.ContextMenuStrip, settings.DarkMode);
        }
        private void UpdateSummary()
        {
            summary.Text = Tr(settings.ProtectedApps.Count + " app memorizzate");
            tray.Text = Tr(settings.AutoProtect ? "Stream Privacy · automatico" : "Stream Privacy · in pausa");
        }
        private static void SetupList(ListView list)
        {
            list.Dock = DockStyle.Fill; list.View = View.Details; list.FullRowSelect = true; list.HideSelection = false; list.MultiSelect = false;
            list.BorderStyle = BorderStyle.None; list.ShowItemToolTips = true;
        }
        private static Button MakeButton(string text, bool primary) { Button button = new ThemedButton(); SetupButton(button, text, primary); return button; }
        private static void SetupButton(Button button, string text, bool primary)
        {
            button.Text = text; button.AutoSize = true; button.Height = 34; button.Padding = new Padding(9, 3, 9, 3); button.FlatStyle = FlatStyle.Flat;
            button.Tag = primary ? "primary" : "secondary";
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(35, 86, 184) : Color.FromArgb(190, 200, 215);
            button.BackColor = primary ? Color.FromArgb(35, 86, 184) : Color.White; button.ForeColor = primary ? Color.White : Color.FromArgb(28, 39, 55); button.Margin = new Padding(0, 0, 10, 0);
        }
        private AppRow Selected() { return windows.SelectedItems.Count == 0 ? null : (AppRow)windows.SelectedItems[0].Tag; }
        private void UpdateButtons()
        {
            bool available = (!busy || scanning) && pendingActions.Count == 0 && !pendingPause && !exitRequested;
            protect.Enabled = available && activeTab == 0 && windows.Items.Count > 0;
            remove.Enabled = available && (activeTab == 0 ? Selected() != null && !String.IsNullOrEmpty(Selected().Path) : savedApps.SelectedItems.Count > 0);
            pause.Enabled = available;
        }
        private void FillSavedApps()
        {
            string selected = savedApps.SelectedItems.Count > 0 ? (string)savedApps.SelectedItems[0].Tag : null;
            savedApps.BeginUpdate(); savedApps.Items.Clear();
            foreach (string path in settings.ProtectedApps)
            {
                ListViewItem row = new ListViewItem(new string[] { Path.GetFileNameWithoutExtension(path), Tr("Automatica quando l'app viene aperta") });
                row.Tag = path; row.ToolTipText = path; savedApps.Items.Add(row); row.Selected = String.Equals(selected, path, StringComparison.OrdinalIgnoreCase);
            }
            savedApps.EndUpdate(); UpdateSummary();
        }
        private sealed class RowPlan
        {
            internal string Key;
            internal AppRow App;
            internal string[] Values;
            internal bool Checked;
        }
        private void DisplayWindows(List<WindowItem> items)
        {
            Dictionary<string, AppRow> groups = new Dictionary<string, AppRow>(StringComparer.OrdinalIgnoreCase);
            foreach (WindowItem item in items)
            {
                if (settings.ChatOnly && !IsChatApp(item.App)) continue;
                string key = String.IsNullOrEmpty(item.ExecutablePath) ? "unreadable:" + item.Pid : item.ExecutablePath;
                AppRow app;
                if (!groups.TryGetValue(key, out app))
                {
                    app = new AppRow { App = item.App, Path = item.ExecutablePath };
                    groups.Add(key, app);
                }
                app.Windows.Add(item);
            }
            List<RowPlan> plans = new List<RowPlan>();
            foreach (KeyValuePair<string, AppRow> pair in groups)
            {
                AppRow app = pair.Value; int confirmed = 0; int unknown = 0; bool pending = false;
                foreach (WindowItem item in app.Windows)
                {
                    uint affinity; bool known = Native.GetWindowDisplayAffinity(item.Handle, out affinity);
                    if (!known) unknown++;
                    else if (affinity == 0x11) confirmed++;
                    if (session.Windows.ContainsKey(item.Handle) && (!known || affinity != 0x11)) pending = true;
                }
                string state = confirmed == app.Windows.Count ? "Esclusione confermata" : confirmed > 0 ? "Escluse " + confirmed + " su " + app.Windows.Count : pending ? "Richiesta non confermata" : unknown > 0 ? "Stato non leggibile" : "Nessuna esclusione";
                bool chosen = false;
                if (!String.IsNullOrEmpty(app.Path) && !stagedApps.TryGetValue(app.Path, out chosen))
                { chosen = settings.ContainsApp(app.Path); stagedApps[app.Path] = chosen; }
                if (String.IsNullOrEmpty(app.Path)) state = "Percorso non leggibile";
                plans.Add(new RowPlan { Key = pair.Key, App = app, Checked = chosen,
                    Values = new string[] { app.App, app.Windows.Count.ToString(), Tr(state), settings.ContainsApp(app.Path) ? Tr("Sì") : "No" } });
            }
            plans.Sort(delegate(RowPlan a, RowPlan b) { int order = StringComparer.OrdinalIgnoreCase.Compare(a.App.App, b.App.App); return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key); });
            bool changed = liveRows.Count != plans.Count;
            foreach (RowPlan plan in plans)
            {
                ListViewItem row;
                if (!liveRows.TryGetValue(plan.Key, out row)) { changed = true; continue; }
                row.Tag = plan.App;
                if (row.Checked != plan.Checked) changed = true;
                for (int index = 0; index < plan.Values.Length; index++)
                    if (row.SubItems[index].Text != plan.Values[index]) changed = true;
            }
            if (!changed) return;
            updatingChoices = true; windows.BeginUpdate();
            try
            {
                foreach (string key in new List<string>(liveRows.Keys))
                    if (!groups.ContainsKey(key)) { liveRows[key].Remove(); liveRows.Remove(key); }
                for (int index = 0; index < plans.Count; index++)
                {
                    RowPlan plan = plans[index]; ListViewItem row;
                    if (!liveRows.TryGetValue(plan.Key, out row))
                    {
                        row = new ListViewItem(plan.Values); row.Tag = plan.App;
                        windows.Items.Insert(index, row); liveRows.Add(plan.Key, row);
                    }
                    else
                    {
                        row.Tag = plan.App;
                        for (int column = 0; column < plan.Values.Length; column++)
                            if (row.SubItems[column].Text != plan.Values[column]) row.SubItems[column].Text = plan.Values[column];
                    }
                    if (row.Checked != plan.Checked) row.Checked = plan.Checked;
                }
            }
            finally { windows.EndUpdate(); updatingChoices = false; }
            UpdateButtons();
        }
        private void RunWork(Action action, Action done, bool backgroundScan = false)
        {
            if (busy || allowClose) return;
            busy = true; scanning = backgroundScan; UpdateButtons();
            ThreadPool.QueueUserWorkItem(delegate {
                Exception failure = null; try { action(); } catch (Exception error) { failure = error; }
                try { BeginInvoke(new Action(delegate {
                    busy = false; scanning = false; completing = true;
                    try
                    {
                        if (failure != null) SetStatus(failure.Message, true);
                        else if (done != null) done();
                    }
                    finally { completing = false; }
                    if (IsDisposed) return;
                    DrainPending(); UpdateButtons();
                })); } catch (InvalidOperationException) { }
            });
        }
        private void QueueOrRun(Action action)
        {
            if (busy || completing)
            {
                pendingActions.Enqueue(action); UpdateButtons();
                SetStatus("Selezione ricevuta: attendo la richiesta in corso e applico le modifiche.", false);
            }
            else action();
        }
        private void DrainPending()
        {
            if (busy || completing || IsDisposed) return;
            if (exitRequested) { pendingActions.Clear(); pendingPause = false; RequestExit(); return; }
            if (recoveryPending) { RequestOpenWindow(true); return; }
            if (pendingPause) { pendingPause = false; pendingActions.Clear(); PauseAndRestore(); return; }
            if (pendingActions.Count > 0)
            {
                Action next = pendingActions.Dequeue(); next();
                if (!busy) DrainPending();
            }
        }
        private void RefreshWindows()
        {
            if (busy || completing || recoveryPending || pendingActions.Count > 0 || pendingPause || preview || exitRequested || !IsHandleCreated) return;
            bool apply = settings.AutoProtect; List<string> paths = new List<string>(settings.ProtectedApps);
            List<WindowItem> items = null; int failures = 0;
            RunWork(delegate {
                items = EnumerateWindows(); session.Prune();
                if (apply)
                {
                    foreach (WindowItem item in items)
                    {
                        if (!Matches(paths, item.ExecutablePath)) continue;
                        DateTime next; if (retryAfter.TryGetValue(item.Handle, out next) && DateTime.UtcNow < next) continue;
                        uint affinity; if (Native.GetWindowDisplayAffinity(item.Handle, out affinity) && affinity == 0x11) continue;
                        try { session.Protect(item); retryAfter.Remove(item.Handle); }
                        catch (Exception) { failures++; retryAfter[item.Handle] = DateTime.UtcNow.AddSeconds(15); }
                    }
                }
                HashSet<IntPtr> alive = new HashSet<IntPtr>(); foreach (WindowItem item in items) alive.Add(item.Handle);
                foreach (IntPtr handle in new List<IntPtr>(retryAfter.Keys)) if (!alive.Contains(handle)) retryAfter.Remove(handle);
            }, delegate {
                DisplayWindows(items);
                if (failures > 0) SetStatus("Non è stato possibile proteggere " + failures + " finestre. Le richieste verranno ritentate; controlla lo stato nell'elenco.", true);
            }, true);
        }
        private static bool Matches(List<string> paths, string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            foreach (string saved in paths) if (String.Equals(saved, path, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        private void ProtectSelected()
        {
            if (activeTab != 0 || allowClose || exitRequested) return;
            Dictionary<string, bool> snapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (ListViewItem row in windows.Items)
            {
                AppRow app = row.Tag as AppRow;
                if (app != null && !String.IsNullOrEmpty(app.Path)) snapshot[app.Path] = row.Checked;
            }
            QueueOrRun(delegate { ApplySelection(snapshot); });
        }
        private void ApplySelection(Dictionary<string, bool> snapshot)
        {
            List<string> original = new List<string>(settings.ProtectedApps);
            bool wasAutomatic = settings.AutoProtect;
            List<string> removed = new List<string>();
            foreach (KeyValuePair<string, bool> choice in snapshot)
            {
                if (choice.Value) settings.AddApp(choice.Key);
                else if (settings.ProtectedApps.RemoveAll(delegate(string path) { return String.Equals(path, choice.Key, StringComparison.OrdinalIgnoreCase); }) > 0) removed.Add(choice.Key);
            }
            settings.AutoProtect = true;
            if (!SaveSettings())
            {
                settings.ProtectedApps.Clear(); settings.ProtectedApps.AddRange(original); settings.AutoProtect = wasAutomatic;
                return;
            }
            syncing = true; automatic.Checked = true; syncing = false; trayAutomatic.Checked = true;
            foreach (KeyValuePair<string, bool> choice in snapshot) stagedApps[choice.Key] = choice.Value;
            FillSavedApps();
            List<WindowItem> items = null;
            RunWork(delegate {
                List<string> errors = new List<string>();
                foreach (ProtectedWindow tracked in new List<ProtectedWindow>(session.Windows.Values))
                    if (Matches(removed, tracked.Window.ExecutablePath))
                        try { session.Restore(tracked.Window.Handle); } catch (Exception error) { errors.Add(error.Message); }
                items = EnumerateWindows(); session.Prune();
                foreach (WindowItem item in items)
                {
                    bool wanted;
                    if (String.IsNullOrEmpty(item.ExecutablePath) || !snapshot.TryGetValue(item.ExecutablePath, out wanted) || !wanted) continue;
                    try { session.Protect(item); retryAfter.Remove(item.Handle); }
                    catch (Exception error) { errors.Add(error.Message); retryAfter[item.Handle] = DateTime.UtcNow.AddSeconds(15); }
                }
                if (errors.Count > 0) throw new InvalidOperationException("Scelte salvate, ma alcune finestre non hanno confermato la modifica. " + errors[0]);
            }, delegate {
                DisplayWindows(items);
                SetStatus("Selezione applicata e memorizzata. Le altre finestre delle app scelte verranno rilevate automaticamente.", false);
            });
        }
        private void RemoveSelectedApp(bool savedTab)
        {
            if (savedTab && savedApps.SelectedItems.Count == 0) return;
            AppRow item = Selected(); string path = savedTab ? (string)savedApps.SelectedItems[0].Tag : item == null ? null : item.Path;
            if (String.IsNullOrEmpty(path)) return;
            QueueOrRun(delegate { RemoveApp(path); });
        }
        private void RemoveApp(string path)
        {
            bool removed = settings.ProtectedApps.RemoveAll(delegate(string saved) { return String.Equals(saved, path, StringComparison.OrdinalIgnoreCase); }) > 0;
            if (!SaveSettings()) { if (removed) settings.AddApp(path); return; }
            stagedApps[path] = false;
            FillSavedApps();
            RunWork(delegate {
                List<string> errors = new List<string>();
                foreach (ProtectedWindow tracked in new List<ProtectedWindow>(session.Windows.Values))
                    if (String.Equals(tracked.Window.ExecutablePath, path, StringComparison.OrdinalIgnoreCase))
                        try { session.Restore(tracked.Window.Handle); } catch (Exception error) { errors.Add(error.Message); }
                if (errors.Count > 0) throw new InvalidOperationException("App rimossa, ma ripristino incompleto. Chiudila e riaprila. " + errors[0]);
            }, delegate { SetStatus("App rimossa dall'elenco; ripristinate le modifiche di questa sessione.", false); });
        }
        private void PauseAndRestore()
        {
            syncing = true; automatic.Checked = false; syncing = false;
            settings.AutoProtect = false; trayAutomatic.Checked = false;
            bool saved = SaveSettings();
            UpdateSummary();
            if (busy || completing) { pendingPause = true; pendingActions.Clear(); UpdateButtons(); if (saved) SetStatus("Pausa richiesta: attendo l'operazione in corso e ripristino le finestre.", false); return; }
            RunWork(delegate {
                List<string> errors = session.RestoreAll();
                if (errors.Count > 0) throw new InvalidOperationException("Ripristino incompleto: chiudi e riapri le app interessate. " + errors[0]);
            }, delegate { if (saved) SetStatus("Protezione in pausa e finestre ripristinate. L'elenco delle app resta memorizzato.", false); RefreshWindows(); });
        }
        internal void RequestExit()
        {
            exitRequested = true; timer.Stop();
            if (busy) { SetStatus("Uscita in corso: attendo l'ultima richiesta e ripristino le finestre.", false); return; }
            exitRequested = false;
            List<string> restoreErrors = null;
            RunWork(delegate { restoreErrors = session.RestoreAll(); }, delegate {
                if (restoreErrors.Count > 0) MessageBox.Show(this, Tr("Alcune finestre non sono state ripristinate. Chiudi e riapri le app interessate."), "Stream Privacy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                allowClose = true; Close();
            });
            exitRequested = false;
        }
        private void OnClosing(object sender, FormClosingEventArgs args)
        {
            if (preview || allowClose) return;
            if (args.CloseReason == CloseReason.TaskManagerClosing)
            { args.Cancel = true; RequestExit(); return; }
            if (args.CloseReason == CloseReason.WindowsShutDown)
            { timer.Stop(); tray.Visible = false; return; }
            args.Cancel = true; HideToTray();
        }
        internal static List<WindowItem> EnumerateWindows(bool includeHidden = false)
        {
            List<WindowItem> result = new List<WindowItem>(); Dictionary<uint, string> paths = new Dictionary<uint, string>();
            uint current = (uint)Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr window, IntPtr data) {
                if (!includeHidden && !Native.IsWindowVisible(window)) return true;
                uint pid; uint thread = Native.GetWindowThreadProcessId(window, out pid);
                if (pid == current || pid == 0) return true;
                // Normal operation does not read window titles. Developer tests opt
                // into hidden-window enumeration to locate their harmless demo HWNDs.
                System.Text.StringBuilder title = new System.Text.StringBuilder(512);
                if (includeHidden) Native.GetWindowText(window, title, title.Capacity);
                try {
                    string path; if (!paths.TryGetValue(pid, out path)) { path = Native.ExecutablePath(pid); paths[pid] = path; }
                    using (Process process = Process.GetProcessById((int)pid))
                        result.Add(new WindowItem { Handle = window, Pid = pid, Thread = thread, App = process.ProcessName, Title = title.ToString(), ExecutablePath = path });
                } catch (Exception) { }
                return true;
            }, IntPtr.Zero);
            result.Sort(delegate(WindowItem a, WindowItem b) { int order = StringComparer.OrdinalIgnoreCase.Compare(a.App, b.App); return order != 0 ? order : a.Handle.ToInt64().CompareTo(b.Handle.ToInt64()); });
            return result;
        }
        internal static bool IsChatApp(string processName)
        {
            if (String.IsNullOrEmpty(processName)) return false;
            string name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName.Substring(0, processName.Length - 4) : processName;
            string[] known = {
                "whatsapp", "whatsapp.root", "telegram", "telegramdesktop", "unigram",
                "discord", "discordcanary", "discordptb", "discorddevelopment",
                "signal", "signalbeta", "signal-beta", "signal-desktop",
                "slack", "teams", "ms-teams", "msteams", "microsoftteams",
                "element", "element-desktop", "elementnightly", "riot",
                "viber", "wechat", "weixin", "messenger", "messengerdesktop",
                "line", "beeper", "mattermost", "rocketchat", "rocket.chat",
                "wire", "threema", "zulip", "skype", "franz", "ferdi"
            };
            foreach (string chat in known)
                if (String.Equals(name, chat, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
