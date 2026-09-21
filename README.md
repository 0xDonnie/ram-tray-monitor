# ram-tray-monitor

**Indicatore di RAM nell'area di notifica di Windows, con pannello di allarme che si
apre da solo quando la memoria sta per finire e ti lascia chiudere il processo colpevole
prima che la macchina si pianti.**

Un file sorgente, nessuna dipendenza, nessun installer: si compila con il compilatore C#
che sta gia' dentro Windows.

> Windows tray RAM monitor with a pop-up alarm panel that lets you kill the offending
> process before the machine freezes. Single C# source file, no SDK, no NuGet, builds
> with the .NET Framework compiler already present on every Windows. Interface and
> documentation are in Italian.

## Perche' non e' il solito monitor di RAM

Perche' non guarda la percentuale, guarda la **velocita'**.

Due giorni di campionamento ogni trenta secondi, 5665 misure, hanno mostrato una cosa
controintuitiva: quella macchina ha passato otto ore e mezza sopra il novanta per cento
di RAM occupata senza un solo problema, e settantasei minuti di fila al novantanove per
cento senza bloccarsi. Quando la memoria si consuma piano, Windows ha tutto il tempo di
comprimere e paginare e non se ne accorge nessuno.

A bloccare la macchina e' stato ogni volta un processo che si prendeva **oltre un giga in
dieci secondi**. Per questo la soglia piu' importante del programma non e' una
percentuale ma `CROLLO_MB`: il pannello compare mentre il crollo sta avvenendo, in tempo
per fermarlo. I numeri, con i grafici e i colpevoli con nome e cognome, stanno in
[`docs/perche-esiste.md`](docs/perche-esiste.md).

## Cosa fa

Nel tray compare un quadratino con la percentuale di RAM usata: verde sotto l'80 per
cento, arancione dall'80, rosso dal 90 oppure quando restano meno di 600 MB liberi.
Passando il mouse sopra si leggono i giga liberi e i tre processi piu' grossi.

Il pannello di allarme si apre da solo, in basso a destra e sopra tutte le finestre, in
tre casi:

| Condizione | Costante |
|---|---|
| RAM oltre il 95 per cento | `SOGLIA_ALLARME` |
| meno di 600 MB liberi | `LIBERI_CRITICI_MB` |
| oltre 800 MB spariti in pochi secondi | `CROLLO_MB` |

Elenca i **trenta** processi piu' grossi **raggruppati per nome** - Brave gira con
oltre venti processi, contarli separatamente non direbbe niente - con megabyte,
gigabyte, quota percentuale sulla RAM installata e numero di processi. I primi tredici
si vedono subito, gli altri scorrendo, e la barra di scorrimento resta dove l'hai
lasciata anche mentre i numeri si aggiornano. L'ultima riga, grigia, riassume tutto
quello che non ci sta: su una macchina normale i primi dodici nomi sono gia' l'ottanta
per cento della memoria occupata, e senza quella riga sembra che il conto non torni.

Si seleziona una riga e si preme "Chiudi questo": prova prima la chiusura educata con
`CloseMainWindow()` e solo dopo tre secondi forza. I componenti di Windows sono grigi e
protetti, non si chiudono.

## Il pannello Claude di Office

Il componente aggiuntivo Claude per Excel, Word e PowerPoint gira dentro WebView2 e
**resta in memoria anche quando lo chiudi**: sei processi per circa 470 MB misurati.

In fondo al pannello c'e' una riga che dice **se e' acceso**, senza dover premere
niente: "Claude Office ACCESO 470 MB" in arancione quando sta tenendo memoria, "Claude
Office spento" in verde quando non c'e'. Accanto, il comando che porta nell'altro stato.

**Spegni e libera** chiude quei processi e la riga di stato mostra quanti MB ha
recuperato. Chiude solo quelli: `msedgewebview2.exe` lo usano anche Widgets, Copilot,
Outlook, Teams e Discord, quindi il filtro non guarda il nome del processo ma la sua
riga di comando, e tiene solo chi lavora nel profilo dei componenti aggiuntivi di Office
(`...\Office\16.0\Wef\webview2`).

**Accendi in Office** porta Excel (o Word, o PowerPoint) in primo piano e manda
Ctrl+Alt+C, la scorciatoia del componente aggiuntivo, poi ricontrolla lo stato. Se non
c'e' nessun Office aperto il comando e' spento e lo dice, senza finestrelle.

Lo stato si aggiorna da solo ogni dieci secondi mentre il pannello e' aperto. Costa
quanto contare i processi, non quanto una query WMI: la classificazione dei PID resta in
cache e WMI si interroga solo quando compare un processo mai visto, oppure - sempre -
un attimo prima di chiudere qualcosa, perche' sui PID riciclati non si scherza.

I due comandi sono anche nel menu dell'icona nel tray.

Quello che il programma **non** fa e' il blocco IFEO, cioe' scrivere
`Debugger=systray.exe` sotto `Image File Execution Options\msedgewebview2.exe`. Quel
trucco gira in rete come soluzione, impedisce del tutto a WebView2 di partire e rompe il
componente aggiuntivo con "Non e' possibile avviare questo componente aggiuntivo"
(`CO_E_SERVER_EXEC_FAILURE`). Qui si chiudono dei processi, e basta: il registro non si
tocca.

Il pannello **non ruba il fuoco della tastiera**: se stai scrivendo continui a scrivere.
Aperto dall'allarme si richiude da solo quando si risale sopra 1,5 GB liberi; aperto a
mano col doppio clic sull'icona resta aperto finche' non lo chiudi tu. C'e' un pulsante
per farlo stare zitto mezz'ora. Ogni allarme viene annotato in
`%USERPROFILE%\Downloads\claude_ram_allarmi.csv`.

## Compilare

    .\build.ps1            # produce bin\ClaudeRamTray.exe
    .\build.ps1 -Avvia     # compila e lancia

Non serve nessun SDK, nessun NuGet, nessun Visual Studio: si usa `csc.exe` della .NET
Framework 4, che si trova in `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\` su
qualunque Windows dal 2010 in avanti. L'eseguibile che ne esce e' un file solo da 20 KB.

Questo e' un vincolo di progetto, non una pigrizia: il programma deve poter essere
ricompilato su una macchina qualsiasi, anche in mezzo a un guaio, senza prima installare
mezzo gigabyte di strumenti.

## Installare e disinstallare

    .\install.ps1     # compila, mette in avvio automatico (HKCU Run), lancia
    .\uninstall.ps1   # ferma e toglie dall avvio automatico

**Parte da solo a ogni accensione del PC.** A ogni avvio il programma riscrive la
propria voce in `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` con il percorso da
cui sta girando in quel momento: se sposti la cartella continua a partire lo stesso,
senza dover reinstallare niente. Chi non lo vuole lo toglie con `uninstall.ps1` o dalla
voce di menu "Avvia con Windows".

Niente UAC, niente servizi, niente scritture fuori dal profilo utente: gira tutto nel
contesto dell'utente che lo lancia.

## Quanto pesa

Da 10 a 18 MB a regime, 4 MB nei primi secondi dopo l'avvio. WinForms ne pretende una
trentina appena parte, cosi' il programma restituisce a Windows le pagine che non sta
usando: subito dopo la partenza, poi ogni cinque minuti, e ogni volta che il pannello si
chiude. Misurato per sette minuti di fila, il rientro si vede a occhio nella serie:

    20:03:43  16 MB
    20:04:13  10 MB   <- sgombero
    20:05:43  15 MB
    20:08:43  18 MB

Sarebbe stato ridicolo il contrario: un monitor di RAM che si tiene trenta MB per dirti
che la RAM sta finendo.

## Struttura

    src/ClaudeRamTray.cs      tutto il programma, file unico
    build.ps1                 compilazione con csc.exe
    install.ps1               build + avvio automatico + lancio
    uninstall.ps1             stop + rimozione avvio automatico
    docs/perche-esiste.md     i dati misurati da cui vengono le soglie
    CLAUDE.md                 istruzioni per lavorarci con Claude Code

## Requisiti

Windows 10 o 11, .NET Framework 4 (c'e' gia'). Niente altro.

## Licenza

MIT, vedi [LICENSE](LICENSE).
