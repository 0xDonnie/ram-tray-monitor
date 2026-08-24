# CLAUDE.md

Istruzioni per lavorare su questo repository con Claude Code.

## Cos'e'

Un solo eseguibile Windows, `ClaudeRamTray.exe`, scritto in C# WinForms e contenuto
tutto in `src/ClaudeRamTray.cs`. Mostra la RAM usata nell'area di notifica e apre un
pannello di allarme quando la memoria sta per finire, con la lista dei processi e un
pulsante per chiuderli. Gira nel contesto utente, senza privilegi di amministratore.

## Come si compila, ed e' l'unica cosa da sapere davvero

    .\build.ps1

Si compila con `csc.exe` della .NET Framework 4, che si trova in
`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` e c'e' su qualunque Windows.
**Non usare dotnet, non usare MSBuild, non aggiungere un csproj, non aggiungere NuGet.**
Il punto di questo progetto e' che si ricompila su qualsiasi macchina senza installare
niente. Se serve una libreria esterna, quasi sempre la risposta giusta e' non usarla.

Riferimenti passati al compilatore: System.dll, System.Drawing.dll,
System.Windows.Forms.dll, System.Core.dll, System.Management.dll. Nient'altro.
System.Management serve solo a leggere la riga di comando dei processi con WMI, che e'
l'unico modo decente per distinguere i WebView2 di Office dagli altri.

`build.ps1` cancella l'eseguibile prima di compilare. Non e' pulizia: senza, una
compilazione fallita lasciava in `bin\` la copia precedente e lo script annunciava "OK"
mentre stavi ancora provando la versione di ieri. E' successo.

## Vincoli duri, da rispettare sempre

Il sorgente deve restare **ASCII puro**. Niente lettere accentate, niente simboli tipo
il trattino lungo, niente emoji, nemmeno nelle stringhe dell'interfaccia. Il file viene
generato e trasferito attraverso strumenti diversi e gli accenti si rompono. Si scrive
"attivita" e "piu'" senza accento, oppure con l'apostrofo.

L'interfaccia e' **in italiano** e va tenuta in italiano.

Il programma deve restare **un file solo**. Se cresce troppo si valuta di dividerlo, ma
prima bisogna aggiornare build.ps1 e install.ps1 che passano un singolo sorgente a csc.

Deve continuare a **non richiedere l'elevazione**. Se una funzionalita' richiede
l'amministratore, si scarta o si rende opzionale.

Il consumo del programma stesso deve restare basso: e' un monitor di memoria, sarebbe
ridicolo se pesasse. Appena avviato WinForms si porta dietro una trentina di MB, quindi
`Mem.Sgombera()` chiama `SetProcessWorkingSetSize(-1,-1)` al terzo tick e poi ogni
cinque minuti, e ogni volta che il pannello si chiude: il working set scende a 4 MB
subito dopo l'avvio e **a regime oscilla fra 10 e 18 MB**, con il rientro visibile a
ogni sgombero (misurato per sette minuti il 23/08/2026). Se un giorno lo si vedesse
stare stabilmente sopra i 25 MB, il primo sospetto e' un handle o un `Bitmap` non
liberato nel disegno dell'icona. Non e' un trucco per far bella
figura col Gestione attivita', e' esattamente quello che il programma chiede di fare
agli altri. Se un giorno si vedesse rallentare l'apertura del pannello, diradare lo
sgombero, non toglierlo.

## Architettura

Cinque classi dentro il namespace `ClaudeRamTray`, piu' `Voce`, che e' solo il record
di una riga della classifica: nome, megabyte, quanti processi con quel nome.

`Mem` e' la classe statica di utilita'. Legge la memoria con `GlobalMemoryStatusEx` via
P/Invoke, che e' immediato e non costa niente, al contrario di WMI. `Mem.Classifica(n)`
enumera i processi con `Process.GetProcesses()`, li raggruppa per nome sommando il
`WorkingSet64` e restituisce i primi n. Quella enumerazione costa qualche decina di
millisecondi con trecento processi, quindi **non va chiamata a ogni tick**: nel tray
viene chiamata una volta ogni dieci tick, cioe' ogni venti secondi. `Mem.Protetti` e'
l'insieme dei nomi di processo che il pannello si rifiuta di chiudere.

`Office` e' la classe statica che gestisce il componente aggiuntivo Claude di Excel,
Word e PowerPoint. `Cerca()` interroga WMI su `Win32_Process` e tiene solo i
`msedgewebview2.exe` la cui riga di comando corrisponde a
`Office\1[0-9]\.0\Wef\webview2`, cioe' il profilo WebView2 dei componenti aggiuntivi.
**Il filtro sul nome del processo non basta e non va usato**: lo stesso eseguibile lo
usano Widgets, Copilot, Outlook, Teams e Discord, e chiuderli tutti rompe roba che non
c'entra niente. Il controllo e' verificato su nove righe di comando reali. La query WMI
costa qualche centinaio di millisecondi, quindi `Cerca()` prima guarda con
`GetProcessesByName` se esista almeno un WebView2 e nel caso normale esce subito; non va
messa dentro un tick.

**Non usare mai il blocco IFEO**, cioe' `Debugger=systray.exe` sotto
`HKLM\...\Image File Execution Options\msedgewebview2.exe`. Gira in rete come rimedio,
impedisce del tutto a WebView2 di partire e il 24/08/2026 ha rotto il componente
aggiuntivo con "Non e' possibile avviare questo componente aggiuntivo",
`CO_E_SERVER_EXEC_FAILURE`. Qui si chiudono processi e non si tocca il registro.

`TrayApp` estende `ApplicationContext` ed e' il cuore. Ha un `NotifyIcon`, un
`System.Windows.Forms.Timer` da due secondi, e in `Aggiorna()` fa tutto: legge la
memoria, calcola il crollo rispetto alla lettura precedente, ridisegna l'icona, aggiorna
il tooltip e decide se far comparire il pannello.

`Pannello` estende `Form`. Si apre in basso a destra, e' TopMost, non compare nella barra
delle applicazioni e soprattutto **non ruba il fuoco della tastiera** grazie
all'override di `ShowWithoutActivation`. Ha un timer proprio da due secondi che ricarica
la lista mentre e' aperto, e si nasconde da solo quando si risale sopra 1,5 GB liberi.
La chiusura con la X non distrugge la finestra, la nasconde: c'e' un handler su
`FormClosing` che annulla e chiama `Hide()`, perche' la stessa istanza viene riusata.

Il pannello ha due modi, distinti dal campo `manuale`, che vale `true` solo quando
l'apertura viene dal doppio clic sull'icona o dalla voce di menu. **Il rientro
automatico sopra 1,5 GB liberi vale solo per il modo allarme**: il pannello aperto a
mano deve restare aperto, altrimenti sparisce due secondi dopo, che e' esattamente il
difetto che aveva alla prima versione. Il campo si azzera da solo su `VisibleChanged`,
cosi' non serve ricordarsi di resettarlo in ogni punto che chiama `Hide()`.

`Riempi()` **non ricostruisce quasi mai la lista**: riscrive il contenuto delle righe
che ci sono gia', nome compreso, e svuota la `ListView` solo quando cambia il numero di
righe. Questo perche' ricostruirla riporta la barra di scorrimento in cima, difetto
segnalato due volte dall'utente: la prima quando si ricostruiva a ogni tick, la seconda
quando bastava un cambio d'ordine, che con trenta processi succede in continuazione
perche' gli ultimi si scambiano di posto per un megabyte. Provato dall'esterno con
`LVM_GETTOPINDEX`: scorrendo fino alla riga 17, dopo quattro giri del timer era ancora
la 17.

La contropartita e' che la riga numero N puo' cambiare processo sotto il cursore, quindi
**la selezione segue il nome e non la posizione**: dopo l'aggiornamento, se la riga
selezionata non e' piu' quella di prima si cerca il nome altrove e lo si riseleziona, e
se e' sparito non resta selezionato niente. Senza quel pezzo "Chiudi questo" potrebbe
chiudere il processo che nel frattempo e' scivolato sotto la selezione.

I valori numerici delle colonne li produce il solo metodo `Numeri(Voce, totaleMB)`: se
si aggiunge una colonna si tocca solo quello e l'elenco di `Columns.Add`.

## Le soglie

Stanno tutte in cima a `TrayApp` come costanti:

    SOGLIA_ARANCIONE   = 80     icona arancione
    SOGLIA_ROSSA       = 90     icona rossa
    SOGLIA_ALLARME     = 95     apre il pannello
    LIBERI_CRITICI_MB  = 600    apre il pannello
    CROLLO_MB          = 800    apre il pannello se sparisce tanta RAM di colpo

Non sono numeri inventati, vengono da due giorni di misure sulla macchina reale.
**Prima di cambiarle leggere `docs/perche-esiste.md`.** In sintesi: la percentuale
assoluta conta poco, la macchina passa ore sopra il 90 per cento senza problemi; quello
che uccide e' la velocita' con cui la memoria sparisce. Per questo `CROLLO_MB` e' la
soglia piu' importante delle tre.

C'e' anche un antirimbalzo: il pannello non si riapre piu' di una volta al minuto
(`ultimoAllarme`), e il pulsante "Zitto 30 min" imposta `Pannello.RinviaFino`.

## Trappole gia' pagate, non ripeterle

L'icona del tray si genera disegnando un `Bitmap` e convertendolo con `GetHicon()`.
Quell'handle **va distrutto con `DestroyIcon`**, altrimenti si perdono handle GDI a ogni
tick e dopo qualche ora Windows smette di disegnare. Il codice attuale clona l'icona,
distrugge l'handle e libera l'icona del giro precedente. Non semplificare quel pezzo.

`NotifyIcon.Text` ha un limite storico di 63 caratteri, alzato a 127 dalla .NET
Framework 4. L'assegnazione e' dentro un try/catch con un ripiego corto. Lasciarlo.

`Timer` senza qualificazione risolve a `System.Windows.Forms.Timer` perche' non si
importa `System.Threading` ne' `System.Timers`. Se si aggiunge uno di quei using, tutti
i timer diventano ambigui.

La chiusura dei processi prova prima `CloseMainWindow()` per chi ha una finestra, e solo
dopo tre secondi forza con `Kill()` tramite un timer dedicato. Non mettere uno `Sleep`
sul thread dell'interfaccia: la finestra si congelerebbe proprio nel momento peggiore.

Il tooltip e la lista mostrano la somma per **nome** di processo, non per singolo
processo, perche' Brave gira con oltre venti processi e la classifica per singolo
processo non direbbe niente di utile.

Una `Mutex` chiamata `ClaudeRamTray_singola` impedisce due istanze contemporanee. Chi
volesse lanciare una copia di prova accanto a quella vera deve cambiare quel nome,
altrimenti la seconda esce subito senza dire niente.

`AllineaAvvio()` gira a ogni avvio e riscrive la chiave `HKCU\...\Run` con il percorso
dell'eseguibile che sta girando in quel momento. Serve perche' spostando la cartella il
registro restava a puntare al vecchio percorso e il monitor non ripartiva piu' al
riavvio del PC, senza che niente lo segnalasse. La voce di menu "Avvia con Windows"
continua a togliere e rimettere la chiave, ma al lancio successivo il programma si
riscrive: e' voluto, la richiesta era che parta sempre.

**Il flag `manuale` va assegnato DOPO `Show()`.** Assegnandolo prima veniva azzerato
dentro `VisibleChanged`, che WinForms fa scattare durante la creazione dell'handle, e
il pannello aperto a mano si richiudeva lo stesso dopo due secondi. Il difetto era
intermittente perche' dipende da quando l'handle viene creato: sembrava corretto e non
lo era. C'e' un commento sul posto, non spostare quella riga.

La geometria del pannello e' a coordinate fisse: `ClientSize` 440x490, la `ListView`
alta 306, la prima riga di bottoni a y=400 e la seconda a y=440. **Se si aggiunge un
bottone o una riga all'intestazione bisogna rifare i conti a mano**, non c'e' nessun
layout automatico.

La lista contiene `RIGHE` = 30 processi piu' una riga di riepilogo, e ne mostra tredici
per volta: le altre si raggiungono scorrendo. Lo scorrimento adesso e' stabile, quindi
la barra non e' piu' un problema come nella prima versione. La riga di riepilogo la
produce `Mem.ClassificaConCoda` e si riconosce da `Mem.ERiepilogo`, cioe' dal nome che
comincia con "(altri ": `Termina()` si rifiuta di chiuderla e `Riempi()` toglie la
selezione quando ricostruisce, altrimenti la ListView lascia selezionata proprio quella
riga e invita a premere "Chiudi questo" su una cosa che non e' un processo.

## Come si prova una modifica

Si compila con `.\build.ps1`, si chiude l'istanza in esecuzione se c'e' (build.ps1 lo
fa gia'), si rilancia. Per provare l'allarme senza aspettare che la RAM finisca si puo'
abbassare temporaneamente `LIBERI_CRITICI_MB` a un valore alto, per esempio 8000, oppure
usare la voce di menu "Mostra pannello adesso" che apre il pannello a comando.

Non esistono test automatici e per un programma di questo tipo non ha molto senso
aggiungerli. La verifica e' visiva.

## Cose che avrebbe senso aggiungere

Un piccolo grafico dell'andamento degli ultimi minuti dentro il pannello, per capire se
la memoria sta salendo piano o di colpo. Una lista di processi da sorvegliare in modo
speciale, tipo ffmpeg, con allarme anticipato appena compaiono. La possibilita' di
impostare le soglie da un file di configurazione invece che ricompilando. Un contatore
storico degli allarmi leggibile dal pannello, visto che il CSV gia' c'e'.

## Contesto piu' ampio

Questo programma e' nato dentro una diagnosi piu' grande sui blocchi di un portatile,
che comprende anche una pista sull'alimentazione USB-C indipendente dalla memoria. In
`docs/perche-esiste.md` c'e' il sottoinsieme che riguarda la RAM, che e' l'unica parte
che questo programma affronta. Quando si valuta una modifica alle soglie, tenere
presente che non tutti i blocchi osservati erano di memoria: attribuirgliene di piu' di
quelli che gli spettano porta a rendere il programma isterico.
