## English

Windows x64 release fixing windows remaining excluded from capture after Stream Privacy is forcibly closed.

- A separate local recovery process attempts to restore the original settings of windows changed by the main process when it exits unexpectedly.
- An atomic local recovery journal is written before applying protection. If the recovery process is terminated too, the next start checks abandoned journals before applying new protection.
- Recovery validates the window/process identity and session marker; protections already present before Stream Privacy intervened are preserved.
- English/Italian interface, dark/light themes, remembered app checkboxes, optional Windows startup and tray controls remain available.
- Portable ZIP and offline per-user installer; source and original icon under the MIT license.

**Before restoring or exiting, stop or pause your stream.** Restored windows can become visible in an ongoing recording or screen share. If both Stream Privacy processes are terminated, recovery waits until the next start; an unresponsive or inaccessible target can still require a full restart of that app. Exclusions left by v0.2.5 or earlier have no recovery journal: fully restart those apps while automatic protection is disabled or Stream Privacy is closed.

**Privacy:** no telemetry, analytics, outgoing network connections, update checks or information sent to developers. Recovery journals stay in portable `Data/Recovery` or installed `%LOCALAPPDATA%\StreamPrivacy\Recovery`. They contain original settings and local window/process identifiers, not window content, titles or app paths. Completed journals are removed after successful restoration. See [PRIVACY.md](https://github.com/flavio92fs/StreamPrivacy/blob/main/PRIVACY.md).

Download [the portable ZIP](https://github.com/flavio92fs/StreamPrivacy/releases/download/v0.2.6/StreamPrivacy-0.2.6-win-x64-portable.zip) or [the setup EXE](https://github.com/flavio92fs/StreamPrivacy/releases/download/v0.2.6/StreamPrivacy-0.2.6-win-x64-setup.exe) and check the attached `SHA256SUMS.txt`. Binaries are unsigned; checksums verify integrity and are not a digital signature.

**Capture compatibility still depends on the target app, Windows and the capture method.** API confirmation and development tests do not certify every OBS or Discord capture. Check a real recording or the video received by viewers before sharing private content.

## Italiano

Versione per Windows x64 che corregge il caso delle finestre rimaste escluse dalla cattura dopo la chiusura forzata di Stream Privacy.

- Un processo locale separato tenta di ripristinare le impostazioni originarie delle finestre modificate quando il processo principale termina in modo imprevisto.
- Un registro locale di recupero viene salvato in modo atomico prima di applicare la protezione. Se viene terminato anche il processo di recupero, all'avvio successivo vengono controllati i registri abbandonati prima di applicare nuove protezioni.
- Il recupero verifica identità di finestra/processo e marcatore della sessione; conserva le protezioni già presenti prima dell'intervento di Stream Privacy.
- Restano disponibili interfaccia inglese/italiana, temi scuro/chiaro, selezione delle app con spunte memorizzate, avvio facoltativo con Windows e comandi vicino all'orologio.
- ZIP portable e installer offline per utente; sorgenti e icona originale con licenza MIT.

**Prima di ripristinare le protezioni o uscire, interrompi o metti in pausa lo stream.** Le finestre ripristinate possono tornare visibili in una registrazione o condivisione ancora attiva. Se entrambi i processi di Stream Privacy vengono terminati, il recupero attende l'avvio successivo; un'app bloccata o non accessibile può comunque richiedere un riavvio completo. Le esclusioni lasciate dalla v0.2.5 o da versioni precedenti non hanno un registro di recupero: riavvia completamente quelle app con la protezione automatica disattivata o Stream Privacy chiuso.

**Riservatezza:** nessuna telemetria, analisi di utilizzo, connessione di rete in uscita, ricerca di aggiornamenti o informazione inviata agli sviluppatori. I registri di recupero restano in `Data/Recovery` nel portable oppure in `%LOCALAPPDATA%\StreamPrivacy\Recovery` nell'installato. Contengono impostazioni originarie e identificativi locali di finestre/processi, non contenuti, titoli o percorsi delle app. I registri completati vengono rimossi dopo il ripristino. Dettagli in [PRIVACY.md](https://github.com/flavio92fs/StreamPrivacy/blob/main/PRIVACY.md).

Scarica [lo ZIP portable](https://github.com/flavio92fs/StreamPrivacy/releases/download/v0.2.6/StreamPrivacy-0.2.6-win-x64-portable.zip) oppure [l'installer EXE](https://github.com/flavio92fs/StreamPrivacy/releases/download/v0.2.6/StreamPrivacy-0.2.6-win-x64-setup.exe) e verifica `SHA256SUMS.txt` allegato. I binari non sono firmati digitalmente: gli hash verificano l'integrità e non costituiscono una firma digitale.

**La compatibilità con la cattura dipende ancora dall'app, da Windows e dal metodo usato.** La conferma delle API e i test di sviluppo non certificano ogni cattura OBS o Discord. Prima di condividere contenuti privati, controlla una registrazione reale o il video ricevuto dagli spettatori.
