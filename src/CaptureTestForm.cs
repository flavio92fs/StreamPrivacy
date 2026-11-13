using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamPrivacy
{
    // A deliberately separate mode from --demo: this window protects itself,
    // without adding a rule for StreamPrivacy.exe or changing saved choices.
    internal sealed class CaptureTestForm : Form
    {
        private readonly Label state = new Label();
        private bool setterSucceeded;
        private int setterError;
        private readonly bool dark;
        private readonly UiLanguage ui;

        internal CaptureTestForm(bool dark = true, string language = "it")
        {
            this.dark = dark;
            ui = new UiLanguage(language);
            Text = ui.Language == "it" ? "Stream Privacy - verifica cattura" : "Stream Privacy - capture test";
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(246, 248, 251);
            ForeColor = Color.FromArgb(28, 39, 55);
            ClientSize = new Size(650, 420);
            MinimumSize = new Size(650, 440);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StreamPrivacy.Icon"))
                if (stream != null) Icon = new Icon(stream, new Size(32, 32));

            TableLayoutPanel layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
                Padding = new Padding(24)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            Controls.Add(layout);

            layout.Controls.Add(new Label {
                Text = "CONTENUTO DI PROVA", Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 22, FontStyle.Bold)
            }, 0, 0);
            layout.Controls.Add(new Label {
                Text = "Sul tuo monitor questo testo deve restare visibile.\nLa verifica riguarda solo ciò che appare nello stream o nella registrazione.",
                Dock = DockStyle.Fill, Padding = new Padding(0, 5, 0, 0)
            }, 0, 1);

            state.Name = "captureState"; state.Dock = DockStyle.Fill;
            state.Padding = new Padding(12);
            state.BackColor = Color.FromArgb(228, 236, 247);
            state.Text = "Richiesta di esclusione a Windows in preparazione…";
            layout.Controls.Add(state, 0, 2);

            layout.Controls.Add(new Label {
                Text = "Apri una registrazione OBS oppure chiedi a chi guarda su Discord.\n\nCon un metodo di cattura compatibile, questa finestra non appare nel video.",
                Dock = DockStyle.Fill, Padding = new Padding(0, 14, 0, 0)
            }, 0, 3);
            layout.Controls.Add(new Label {
                Text = "Se resta visibile nel video anche quando Windows conferma l'esclusione,\nil metodo di cattura utilizzato non sta rispettando questa protezione.\nQuesta finestra non contiene dati personali e non salva impostazioni.",
                Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0),
                ForeColor = Color.FromArgb(85, 95, 110), Font = new Font("Segoe UI", 9)
            }, 0, 4);
            Theme.Apply(this, dark);
            Theme.SetState(state, dark, false);
            ui.Apply(this, null);
            state.Text = ui.Translate(state.Text);
        }

        // This event precedes the first visible frame. Do not wait for Shown.
        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            setterSucceeded = Native.SetWindowDisplayAffinity(Handle, 0x11);
            setterError = setterSucceeded ? 0 : Marshal.GetLastWin32Error();
            Theme.ApplyTitleBar(this, dark);
            UpdateState();
        }

        internal uint ObservedAffinity
        {
            get {
                uint observed;
                return IsHandleCreated && Native.GetWindowDisplayAffinity(Handle, out observed)
                    ? observed : UInt32.MaxValue;
            }
        }

        internal bool ExclusionConfirmed
        {
            get { return setterSucceeded && ObservedAffinity == 0x11; }
        }

        private void UpdateState()
        {
            if (state == null || !IsHandleCreated) return;
            uint observed;
            bool readSucceeded = Native.GetWindowDisplayAffinity(Handle, out observed);
            int readError = readSucceeded ? 0 : Marshal.GetLastWin32Error();
            bool confirmed = setterSucceeded && readSucceeded && observed == 0x11;
            Theme.SetState(state, dark, !confirmed);
            if (confirmed)
                state.Text = "Esclusione confermata da Windows (0x11).\nLa compatibilità dello stream va verificata nel video ricevuto.";
            else if (!setterSucceeded)
                state.Text = "Windows non ha confermato la richiesta.\nCodice errore: " + setterError + ". Stato letto: " + (readSucceeded ? "0x" + observed.ToString("X") : "non disponibile") + ".";
            else if (!readSucceeded)
                state.Text = "Richiesta inviata, ma lo stato non è leggibile.\nCodice errore di lettura: " + readError + ". Non considerare verificata la protezione.";
            else
                state.Text = "Windows non conferma l'esclusione completa.\nStato letto: 0x" + observed.ToString("X") + " (atteso 0x11).";
            state.Text = ui.Translate(state.Text);
        }
    }
}
