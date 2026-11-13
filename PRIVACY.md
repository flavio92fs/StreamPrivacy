# Privacy / Riservatezza

[English README](README.md) · [README italiano](README.it.md)

## English

**Stream Privacy does not collect data for its developers and makes no outgoing network connections.** It has no telemetry, analytics, advertising, accounts, remote logging, update checker or online activation. The application and its offline installer do not contact external services or download additional components.

The utility must process some information **locally** to identify and protect windows. “No data collection” does not mean “no local processing.”

| Information | Purpose | Storage or transmission |
| --- | --- | --- |
| App/process names, full executable paths, process/thread IDs and window handles | Display app choices, match saved rules and request capture exclusion | Processed locally in memory; never sent to developers or external services |
| Chosen executable paths, language, theme, app-filter and automatic-protection options | Remember your choices | XML file on your computer only |
| Original display-affinity settings and temporary window markers | Restore changes made by the current session | Kept locally for the current session |
| Optional Windows startup command | Start beside the clock at sign-in | Per-user Windows registry entry only |

During normal use, Stream Privacy does not read or save chat/window titles, messages, passwords, screenshots, audio or recordings. It does not inspect the content displayed in protected windows. The temporary native helper handles its own control message to request exclusion; it does not install keyboard or mouse hooks or record user input.

**Local files and settings:**

- Portable: `Data/settings.xml` in the application folder, selected by the bundled `portable.flag`.
- Installed: `%LOCALAPPDATA%\StreamPrivacy\settings.xml`.
- Optional startup: the `StreamPrivacy` value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

Executable paths may contain your Windows username. They remain in your local settings file. Do not publish that file if you want to keep those paths private. Normal operation creates no activity log. Explicit developer test commands may inspect window titles to locate demonstration windows and write **local** reports or test settings; they do not upload them.

There is no background synchronization or automatic update traffic. English and Italian translations are embedded in the application; switching language needs no network connection. Downloading a release from GitHub or following documentation links uses your browser and is separate from running Stream Privacy. This statement describes Stream Privacy's own code; Windows, your browser and the other apps on your computer have their own behavior and privacy policies.

To remove saved choices, exit Stream Privacy and delete its settings file. Disable **Start with Windows** before removing a portable copy to remove its startup entry. The installed version can be removed through Windows' installed-apps settings; uninstalling preserves local preferences by default. Delete the settings file separately if you want to remove them.

The complete source is available for inspection in [`src`](src). Capture exclusion is separate from privacy of application data: a successful Windows setting does not guarantee that every capture method hides a window. See the README's practical limits.

## Italiano

**Stream Privacy non raccoglie dati per gli sviluppatori e non effettua connessioni di rete in uscita.** Non contiene telemetria, analisi di utilizzo, pubblicità, account, registrazione remota, ricerca di aggiornamenti o attivazione online. Il programma e il suo installer offline non contattano servizi esterni e non scaricano componenti aggiuntivi.

Per identificare e proteggere le finestre deve elaborare alcune informazioni **in locale**. “Nessuna raccolta dati” non significa “nessuna elaborazione locale”.

| Informazione | Scopo | Salvataggio o trasmissione |
| --- | --- | --- |
| Nomi delle app/processi, percorsi completi degli eseguibili, identificativi di processi/thread e finestre | Mostrare le app, applicare le regole e richiedere l'esclusione dalla cattura | Elaborati nella memoria locale; mai inviati agli sviluppatori o a servizi esterni |
| Percorsi degli eseguibili scelti, lingua, tema, filtro delle app e opzioni di protezione automatica | Ricordare le scelte | Solo un file XML sul computer |
| Impostazioni originarie di esclusione e marcatori temporanei delle finestre | Ripristinare le modifiche della sessione | Mantenuti localmente durante la sessione |
| Comando facoltativo di avvio con Windows | Avviare il programma vicino all'orologio all'accesso | Solo una voce del registro del tuo utente |

Nell'uso normale Stream Privacy non legge né salva titoli di chat/finestre, messaggi, password, screenshot, audio o registrazioni. Non analizza il contenuto visualizzato nelle finestre protette. Il modulo nativo temporaneo gestisce il proprio messaggio di controllo per richiedere l'esclusione; non installa hook di tastiera o mouse e non registra ciò che digiti.

**File e impostazioni locali:**

- Portable: `Data/settings.xml` nella cartella del programma, selezionato dal file `portable.flag` incluso.
- Installato: `%LOCALAPPDATA%\StreamPrivacy\settings.xml`.
- Avvio facoltativo: valore `StreamPrivacy` in `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`.

I percorsi degli eseguibili possono contenere il nome del tuo utente Windows: rimangono nel file delle impostazioni locale. Non pubblicare quel file se vuoi mantenere privati i percorsi. L'uso normale non crea un registro delle attività. I comandi espliciti di verifica per sviluppatori possono esaminare i titoli delle finestre per individuare quelle dimostrative e scrivere rapporti o impostazioni di prova **locali**; non li caricano online.

Non ci sono sincronizzazioni in background o aggiornamenti automatici tramite rete. Le traduzioni in inglese e italiano sono incluse nel programma: cambiare lingua non richiede connessioni. Scaricare una release da GitHub o aprire i collegamenti nella documentazione utilizza il browser ed è separato dall'esecuzione di Stream Privacy. Queste dichiarazioni descrivono il codice di Stream Privacy: Windows, il browser e le altre app del computer hanno comportamenti e informative propri.

Per eliminare le scelte salvate, esci dal programma e cancella il file delle impostazioni. Disattiva **Avvia con Windows** prima di rimuovere una copia portable, per eliminare la sua voce di avvio. La versione installata può essere rimossa dalle impostazioni delle app di Windows: la disinstallazione conserva le preferenze locali per impostazione predefinita. Per rimuoverle, cancella separatamente il file delle impostazioni.

Il codice completo è consultabile in [`src`](src). L'esclusione dalla cattura è distinta dalla riservatezza dei dati del programma: la conferma di Windows non garantisce che ogni metodo nasconda la finestra. Consulta i limiti pratici nel README.
