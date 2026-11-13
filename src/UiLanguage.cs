using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace StreamPrivacy
{
    internal sealed class LanguagePicker : Button
    {
        private string selected = "English";
        internal event EventHandler SelectedIndexChanged;
        internal string SelectedItem
        {
            get { return selected; }
            set {
                string next = value == "Italiano" ? "Italiano" : "English";
                if (next == selected) { Text = selected; return; }
                selected = next; Text = selected;
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        internal LanguagePicker()
        {
            Text = selected; Width = 110; Height = 32; FlatStyle = FlatStyle.Flat;
            Tag = "secondary"; AccessibleName = "Language";
        }
        protected override void OnClick(EventArgs args)
        {
            base.OnClick(args);
            ContextMenuStrip menu = new ContextMenuStrip();
            foreach (string language in new string[] { "English", "Italiano" }) {
                string choice = language;
                ToolStripMenuItem item = new ToolStripMenuItem(choice); item.Checked = choice == selected;
                item.Click += delegate { SelectedItem = choice; }; menu.Items.Add(item);
            }
            Theme.ApplyMenu(menu, BackColor.GetBrightness() < 0.5f);
            menu.Closed += delegate { menu.Dispose(); };
            menu.Show(this, new System.Drawing.Point(0, Height));
        }
    }

    // Local, embedded translations. No resources are fetched from the network.
    internal sealed class UiLanguage
    {
        private readonly Dictionary<Control, string> controls = new Dictionary<Control, string>();
        private readonly Dictionary<ColumnHeader, string> headers = new Dictionary<ColumnHeader, string>();
        private readonly Dictionary<ToolStripItem, string> menuItems = new Dictionary<ToolStripItem, string>();
        internal string Language { get; private set; }
        internal UiLanguage(string language) { SetLanguage(language); }
        internal void SetLanguage(string language) { Language = Normalize(language); }
        internal static string Normalize(string language) { return String.Equals(language, "it", StringComparison.OrdinalIgnoreCase) ? "it" : "en"; }

        private static readonly string[,] Texts = {
            {"Le tue chat, fuori dallo stream", "Your chats, out of the stream"},
            {"Spunta tutte le app da proteggere e premi Applica selezione. Le scelte vengono ricordate.\nIl tasto X lascia Stream Privacy attivo nell'area vicino all'orologio.", "Check the apps to protect, then click Apply selection. Your choices are remembered.\nClosing with X keeps Stream Privacy running beside the clock."},
            {"Avvia con Windows", "Start with Windows"}, {"Protezione automatica", "Automatic protection"},
            {"Tema scuro", "Dark theme"}, {"Solo app di chat", "Chat apps only"},
            {"Aggiorna", "Refresh"}, {"Finestra di prova", "Test window"},
            {"App aperte", "Open apps"}, {"App memorizzate", "Remembered apps"},
            {"App", "App"}, {"Finestre", "Windows"}, {"Stato Windows", "Windows status"},
            {"Regola salvata", "Saved rule"}, {"Riattivazione", "Reactivation"},
            {"Applica selezione", "Apply selection"}, {"Rimuovi app e ripristina", "Remove app and restore"},
            {"Pausa e ripristina tutte", "Pause and restore all"},
            {"Apri Stream Privacy", "Open Stream Privacy"}, {"Ripristina finestra", "Recover window"},
            {"Esci e ripristina", "Exit and restore"},
            {"La conferma di Windows va verificata nell'anteprima dello stream. Le app scelte vengono salvate sul PC.", "Check the actual shared video after Windows confirms exclusion. Your app choices stay on this PC."},
            {"Le nuove finestre vengono rilevate periodicamente: possono comparire per un breve intervallo prima della protezione.\nSolo app a 64 bit. Notifiche di Windows e audio esclusi. Per chiudere davvero usa Esci dal menu dell'icona.\nOffline: nessuna telemetria e nessuna connessione verso l'esterno.", "New windows are detected periodically and may appear briefly before protection is applied.\n64-bit apps only. Windows notifications and audio are not covered. Use Exit from the tray menu to quit.\nOffline: no telemetry and no outgoing network connections."},
            {"Include Signal, WhatsApp, Telegram, Discord, Teams, Slack e altre chat note. Disattiva il filtro per mostrare tutte le app rilevate.", "Includes Signal, WhatsApp, Telegram, Discord, Teams, Slack and other known chat apps. Turn this filter off to show all detected apps."},
            {"Automatica quando l'app viene aperta", "Automatic when the app opens"},
            {"Esclusione confermata", "Exclusion confirmed"}, {"Richiesta non confermata", "Request not confirmed"},
            {"Stato non leggibile", "Status unavailable"}, {"Nessuna esclusione", "No exclusion"},
            {"Percorso non leggibile", "Path unavailable"}, {"Sì", "Yes"},
            {"La prova richiede automaticamente l'esclusione. Leggi lo stato nella sua finestra: sul tuo monitor resta visibile, controlla il video trasmesso.", "The test requests exclusion automatically. Read its status: it stays visible on your monitor, so check the shared video."},
            {"Stream Privacy: controlla le impostazioni", "Stream Privacy: check settings"},
            {"Non è stato possibile leggere le impostazioni. Apri il programma dall'icona.", "Settings could not be read. Open the application from its tray icon."},
            {"Stream Privacy resta attivo", "Stream Privacy is still running"},
            {"Per riaprirlo, fai doppio clic sullo scudo vicino all'orologio.", "To reopen it, double-click the shield beside the clock."},
            {"Avvio con Windows attivato: partirà vicino all'orologio al prossimo accesso.", "Start with Windows enabled: the app will run beside the clock at your next sign-in."},
            {"Avvio con Windows disattivato.", "Start with Windows disabled."},
            {"Protezione automatica attiva per le app memorizzate.", "Automatic protection is enabled for remembered apps."},
            {"Protezione automatica in pausa. Le finestre già protette restano escluse fino al ripristino.", "Automatic protection is paused. Already protected windows remain excluded until restored."},
            {"Selezione ricevuta: attendo la richiesta in corso e applico le modifiche.", "Selection received: changes will be applied when the current request finishes."},
            {"Selezione applicata e memorizzata. Le altre finestre delle app scelte verranno rilevate automaticamente.", "Selection applied and remembered. Other windows of the chosen apps will be detected automatically."},
            {"App rimossa dall'elenco; ripristinate le modifiche di questa sessione.", "App removed from the list; this session's changes have been restored."},
            {"Pausa richiesta: attendo l'operazione in corso e ripristino le finestre.", "Pause requested: windows will be restored when the current operation finishes."},
            {"Protezione in pausa e finestre ripristinate. L'elenco delle app resta memorizzato.", "Protection paused and windows restored. Your remembered app list is retained."},
            {"Uscita in corso: attendo l'ultima richiesta e ripristino le finestre.", "Exiting: waiting for the last request and restoring windows."},
            {"Alcune finestre non sono state ripristinate. Chiudi e riapri le app interessate.", "Some windows could not be restored. Close and reopen the affected apps."},
            {"CONTENUTO DI PROVA", "TEST CONTENT"},
            {"Sul tuo monitor questo testo deve restare visibile.\nLa verifica riguarda solo ciò che appare nello stream o nella registrazione.", "This text should remain visible on your own monitor.\nOnly the content of the stream or recording is being tested."},
            {"Richiesta di esclusione a Windows in preparazione…", "Preparing the Windows exclusion request…"},
            {"Apri una registrazione OBS oppure chiedi a chi guarda su Discord.\n\nCon un metodo di cattura compatibile, questa finestra non appare nel video.", "Check an OBS recording or ask someone watching on Discord.\n\nWith a compatible capture method, this window will not appear in the video."},
            {"Se resta visibile nel video anche quando Windows conferma l'esclusione,\nil metodo di cattura utilizzato non sta rispettando questa protezione.\nQuesta finestra non contiene dati personali e non salva impostazioni.", "If it remains visible in the video after Windows confirms exclusion,\nthe capture method is not respecting this protection.\nThis window contains no personal data and saves no settings."},
            {"Esclusione confermata da Windows (0x11).\nLa compatibilità dello stream va verificata nel video ricevuto.", "Windows confirms exclusion (0x11).\nCheck the received video to verify capture compatibility."},
            {"Windows non ha confermato la richiesta.\nCodice errore: ", "Windows did not confirm the request.\nError code: "},
            {". Stato letto: ", ". Reported state: "}, {"non disponibile", "unavailable"},
            {"Richiesta inviata, ma lo stato non è leggibile.\nCodice errore di lettura: ", "Request sent, but its state cannot be read.\nRead error code: "},
            {". Non considerare verificata la protezione.", ". Do not treat protection as verified."},
            {"Windows non conferma l'esclusione completa.\nStato letto: 0x", "Windows does not confirm complete exclusion.\nReported state: 0x"},
            {" (atteso 0x11).", " (expected 0x11)."},
            {"Impostazioni non disponibili: ", "Settings unavailable: "},
            {"Windows non ha confermato l'esclusione della finestra di Stream Privacy. Codice: ", "Windows did not confirm exclusion of the Stream Privacy window. Code: "},
            {"Impossibile cambiare l'avvio automatico: ", "Unable to change Windows startup: "},
            {"Salvataggio disabilitato: il file delle impostazioni non è leggibile. ", "Saving is disabled because the settings file cannot be read. "},
            {"Impossibile salvare le app scelte: ", "Unable to save app choices: "},
            {"Non è stato possibile proteggere ", "Unable to protect "},
            {" finestre. Le richieste verranno ritentate; controlla lo stato nell'elenco.", " windows. Requests will be retried; check the list for their status."},
            {"Scelte salvate, ma alcune finestre non hanno confermato la modifica. ", "Choices saved, but some windows did not confirm the change. "},
            {"App rimossa, ma ripristino incompleto. Chiudila e riaprila. ", "App removed, but restoration is incomplete. Close and reopen it. "},
            {"Ripristino incompleto: chiudi e riapri le app interessate. ", "Restoration is incomplete: close and reopen the affected apps. "},
            {"La finestra è cambiata. Aggiorna l'elenco e selezionala di nuovo.", "The window has changed. Refresh the list and select it again."},
            {"Questa versione supporta le app a 64 bit. La finestra selezionata appartiene a un'app a 32 bit.", "This version supports 64-bit apps. The selected window belongs to a 32-bit app."},
            {"Impossibile leggere lo stato della finestra. ", "Unable to read the window state. "},
            {"Windows conferma già l'esclusione. Verifica comunque l'anteprima dello stream.", "Windows already confirms exclusion. Still check the actual shared video."},
            {"La finestra usa un'impostazione di protezione non riconosciuta.", "The window uses an unrecognized protection setting."},
            {"Protezione non confermata: ", "Protection not confirmed: "},
            {". Puoi usare Ripristina per annullare la richiesta.", ". You can use Restore to undo the request."},
            {"Windows non conferma l'esclusione. Non considerare protetta questa finestra.", "Windows does not confirm exclusion. Do not treat this window as protected."},
            {"Esclusione confermata da Windows. Ora controlla l'anteprima in OBS e su Discord.", "Windows confirms exclusion. Now check the actual OBS or Discord video."},
            {" app memorizzate", " remembered apps"}, {"Stream Privacy · automatico", "Stream Privacy · automatic"},
            {"Stream Privacy · in pausa", "Stream Privacy · paused"}, {"Escluse ", "Excluded "}, {" su ", " of "}
        };

        internal string Translate(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            for (int index = 0; index < Texts.GetLength(0); index++)
                if (String.Equals(text, Texts[index, 0], StringComparison.Ordinal) || String.Equals(text, Texts[index, 1], StringComparison.Ordinal))
                    return Texts[index, Language == "it" ? 0 : 1];
            // Fragments also cover variable counts, native error codes and composed status messages.
            for (int index = 0; index < Texts.GetLength(0); index++)
            {
                string italian = Texts[index, 0];
                if (italian.Length < 25 && !italian.EndsWith(" ", StringComparison.Ordinal) && italian != " app memorizzate" && italian != "non disponibile" && italian != " (atteso 0x11).") continue;
                string source = Texts[index, Language == "it" ? 1 : 0];
                string target = Texts[index, Language == "it" ? 0 : 1];
                if (source != target && text.IndexOf(source, StringComparison.Ordinal) >= 0) text = text.Replace(source, target);
            }
            return text;
        }

        internal void Apply(Control root, ContextMenuStrip menu)
        {
            ApplyControl(root);
            if (menu != null) foreach (ToolStripItem item in menu.Items) ApplyMenuItem(item);
            root.Invalidate(true);
        }
        private void ApplyControl(Control control)
        {
            if (control.Name != "statusText" && control.Name != "summaryText" && control.Name != "captureState" && control.Name != "languageSelector" && !(control is ComboBox))
            {
                string original; if (!controls.TryGetValue(control, out original)) { original = control.Text; controls.Add(control, original); }
                control.Text = Translate(original);
            }
            ListView list = control as ListView;
            if (list != null) foreach (ColumnHeader header in list.Columns)
            {
                string original; if (!headers.TryGetValue(header, out original)) { original = header.Text; headers.Add(header, original); }
                header.Text = Translate(original);
            }
            foreach (Control child in control.Controls) ApplyControl(child);
        }
        private void ApplyMenuItem(ToolStripItem item)
        {
            string original; if (!menuItems.TryGetValue(item, out original)) { original = item.Text; menuItems.Add(item, original); }
            item.Text = Translate(original);
            ToolStripDropDownItem nested = item as ToolStripDropDownItem;
            if (nested != null) foreach (ToolStripItem child in nested.DropDownItems) ApplyMenuItem(child);
        }
    }
}
