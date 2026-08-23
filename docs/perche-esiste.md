# Perche' esiste questo programma, e da dove vengono le soglie

## Il problema

Portatile Windows 11 Pro, Core Ultra, **15,46 GB di RAM saldata e non espandibile**. Blocchi della sessione grafica di durata variabile, da venti secondi a
venti minuti, senza BSOD, senza crash del kernel, senza errori hardware.

## La misura

Dal 21/08/2026 alle 12:32 al 23/08/2026 alle 12:43 e' stato tenuto attivo un
campionatore che ogni trenta secondi registrava RAM libera, percentuale occupata,
commit, peso dei singoli processi e sorgente di alimentazione. **5665 campioni.**

## Primo risultato: la percentuale, da sola, non significa niente

    sopra l'80%  ->  47,9% del tempo  (~22,6 ore su 48)
    sopra il 90% ->  18,0% del tempo  (~8,5 ore)
    sopra il 95% ->   7,5% del tempo  (~3,6 ore)
    sopra il 98% ->   2,2% del tempo  (~1,0 ore)

Otto ore e mezza sopra il novanta per cento senza un solo blocco. Il 22/08 la macchina
e' stata **76 minuti di fila con 153 MB liberi, cioe' al 99 per cento**, e ha retto:
la memoria si era consumata lentamente, nell'arco di ore, e Windows aveva avuto tutto il
tempo di comprimere e paginare con calma.

## Secondo risultato: uccide la velocita', non il livello

Fra due campioni consecutivi la RAM libera si muove tipicamente di **1 MB** (mediana),
e nel 95esimo percentile di **97 MB**. I crolli veri sono eventi rarissimi e violenti:

    23/08 00:07:54   -2796 MB in un colpo    ffmpeg a 4858 MB
    21/08 14:39:55   -2484 MB               Ferdium
    21/08 18:32:54   -2424 MB               Ferdium
    22/08 23:09:54   -2113 MB               brave
    22/08 23:49:55   -2027 MB               ffmpeg
    22/08 23:19:55   -1773 MB               ffmpeg, si scende a 627 MB liberi
    22/08 23:35:58   -1668 MB               ffmpeg, si scende a 453 MB liberi
    21/08 20:13:03   -1456 MB               vmmem + thunderbird, 361 MB liberi
    23/08 12:42:05   -1359 MB               ffmpeg, 456 MB liberi   <- blocco percepito

## Terzo risultato: la soglia vera sono i 600 MB liberi

I momenti in cui la macchina si e' effettivamente impallata hanno tutti in comune di
essere scesi **sotto i 600 MB liberi**, e di esserci arrivati **di colpo**. Su questa
macchina 600 MB corrispondono a circa il 96 per cento.

Il blocco del 23/08 alle 12:42 e' documentato al secondo: alle 12:41:55 c'erano 1815 MB
liberi, dieci secondi dopo 456, con `ffmpeg` comparso a 1956 MB. Venti secondi di
sessione ferma, il monitor si e' staccato e riattaccato cambiando risoluzione (quasi
certamente un reset del driver video Intel Arc, che e' fermo alla versione
31.0.101.5382 del 27/03/2024), poi tutto e' tornato normale.

## Da qui le soglie del programma

    SOGLIA_ARANCIONE   = 80     preavviso morbido
    SOGLIA_ROSSA       = 90     spia rossa con buon margine
    SOGLIA_ALLARME     = 95
    LIBERI_CRITICI_MB  = 600    la soglia fisica vera
    CROLLO_MB          = 800    la piu' importante: intercetta l'evento mentre accade

`CROLLO_MB` e' la ragione d'essere del programma. Il 23/08 avrebbe fatto comparire il
pannello con scritto "ffmpeg 1956 MB" mentre ffmpeg stava ancora allocando, cioe' in
tempo per chiuderlo.

## I mangiatori abituali, misurati

    ffmpeg            picco 4858 MB    in cima per 31 minuti fra il 22 e il 23
    WindowsTerminal   picco 3769 MB    in cima per 562 minuti il 22 (buffer di scorrimento)
    vmmem (WSL)       picco 3707 MB    nonostante .wslconfig imposti memory=2GB
    Ferdium           picco 1530 MB
    thunderbird       picco 1320 MB
    brave             4-5 GB su 23 processi, pesante ma stabile

Nota controintuitiva: **Brave non e' il colpevole**. E' il fondo costante, non produce
i picchi. I picchi li fanno ffmpeg e Windows Terminal.

## Cosa resta fuori da questo programma

Esiste una seconda causa, indipendente, per i blocchi lunghi da venti minuti: perdite
reali dell'alimentazione di rete documentate il 18/08 e il 21/08, con il portatile che
passa a batteria per 8, 66 e 152 secondi senza che nessuno tocchi il cavo. Quella pista
riguarda l'hub USB-C e non ha niente a che vedere con la memoria: se ne parla qui solo
perche' i due fenomeni si sommano, e chi legge i numeri qui sopra deve sapere che non
spiegano tutti i blocchi.
