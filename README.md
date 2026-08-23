# ram-tray-monitor

Indicatore di RAM nell'area di notifica di Windows, con pannello di allarme che si apre
da solo quando la memoria sta per finire e permette di chiudere i processi colpevoli
prima che la macchina si pianti.

Nato per il portatile (16 GB non espandibili), dove blocchi da venti secondi
si sono rivelati causati da singoli processi che si prendono oltre un giga in dieci
secondi. Il perche' delle soglie sta in `docs/perche-esiste.md`.

## Cosa fa

Nel tray compare un quadratino con la percentuale di RAM usata: verde sotto l'80 per
cento, arancione dall'80, rosso dal 90 oppure quando restano meno di 600 MB liberi.
Passando il mouse sopra si leggono i giga liberi e i tre processi piu' grossi.

Il pannello di allarme si apre da solo, in basso a destra e sopra tutte le finestre,
quando la RAM supera il 95 per cento, oppure restano meno di 600 MB liberi, oppure
qualcosa si e' preso piu' di 800 MB in pochi secondi. Elenca i dodici processi piu'
grossi raggruppati per nome, con quanti processi sono e quanti MB occupano. Si
seleziona una riga e si preme "Chiudi questo": prova prima la chiusura educata e dopo
tre secondi forza. I componenti di Windows sono grigi e protetti.

Il pannello non ruba il fuoco della tastiera, si richiude da solo quando si risale
sopra 1,5 GB liberi, e ha un pulsante per stare zitto mezz'ora. Ogni allarme viene
annotato in `%USERPROFILE%\Downloads\claude_ram_allarmi.csv`.

## Compilare

    .\build.ps1            # produce bin\ClaudeRamTray.exe
    .\build.ps1 -Avvia     # compila e lancia

Non serve nessun SDK, nessun NuGet, nessun Visual Studio: si usa `csc.exe` della .NET
Framework 4, che c'e' gia' su qualunque Windows.

## Installare e disinstallare

    .\install.ps1     # compila, mette in avvio automatico (HKCU Run), lancia
    .\uninstall.ps1   # ferma e toglie dall avvio automatico

Niente UAC, gira tutto nel contesto utente.

## Struttura

    src/ClaudeRamTray.cs      tutto il programma, file unico
    build.ps1                 compilazione con csc.exe
    install.ps1               build + avvio automatico + lancio
    uninstall.ps1             stop + rimozione avvio automatico
    docs/perche-esiste.md     i dati misurati da cui vengono le soglie
    CLAUDE.md                 istruzioni per lavorarci con Claude Code

## Licenza

Uso personale.
