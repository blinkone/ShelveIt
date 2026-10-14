# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Detailer di Cities: Skylines II con migliaia di asset custom installati (decine di migliaia di asset piazzabili da centinaia di pacchetti PDX Mods). Costruiscono quartieri a tema e hanno bisogno di ritrovare in fretta insiemi coerenti di asset. Conoscono già Find It e lo usano ogni sessione. Confermato dall'autore: questo è il pubblico principale, i giocatori occasionali non sono una priorità.

## Product Purpose

Mod per Cities: Skylines II che scansiona gli asset installati e li organizza in librerie tematiche (es. "UK, residenziale medio", "Medium Old Town", "industria storica"). La funzione principale è il suggerimento automatico di librerie a partire dai metadati degli asset, senza lavoro manuale. Il successo è: il giocatore apre una città, trova pack specifici già pronti e piazza asset coerenti senza cercarli uno per uno.

## Positioning

Find It trova un asset alla volta e ha solo i preferiti. Questa mod lavora per insiemi: propone da sola pack specifici (regione, pack del creator, famiglia di edifici, set tematici) e li rende usabili anche dentro Find It con la ricerca `lib:<libreria>`.

## Operating Context

- Gira dentro l'interfaccia del gioco (motore Coherent Gameface, React e TypeScript, CSS limitato: niente grid, niente `:has`, niente shorthand con variabili CSS, niente `:disabled`).
- Il pannello convive con la UI del gioco e con il pannello di Find It, sopra la vista della città.
- Due modalità d'uso confermate dall'autore: una vista grande per organizzare e curare le librerie, e una vista compatta a tavolozza che resta aperta mentre si costruisce, con un comando per passare dall'una all'altra.
- Schermo di riferimento: 3840x2160. Il gioco ha una propria scala dell'interfaccia.
- Lingue: inglese e italiano.

## Capabilities and Constraints

- Albero di suggerimenti a tre livelli (regioni e stili, pack dei creator, set tematici), accettabili a qualsiasi livello.
- Librerie come tag: un asset può stare in più librerie.
- Filtri in stile Find It (tipo, zona, densità, regione, tema, asset pack, origine, pacchetto, livello, lotto), ricerca, ordinamento, azioni in blocco sul risultato filtrato.
- Clic su un asset per piazzarlo. Pulsante "Apri in Find It".
- Export e import JSON, export di contributo al database community.
- Thumbnail: icona reale, icona della zona, PNG fornito dall'utente, altrimenti casella generata. Nessuna generazione di immagini tramite rendering (scelta dell'autore).
- Vincolo di leggerezza: nessun lavoro quando il pannello è chiuso, righe e caselle economiche da disegnare, liste lunghe (centinaia di nodi, pagine da 60 asset).
- Find It non ha licenza: nessuna copia di codice, CSS o icone.
- Il nome della mod è "Shelve It" (scelto dall'autore).

## Brand Commitments

L'interfaccia deve essere coerente con quella del gioco (richiesta esplicita dell'autore): deve sembrare parte di Cities: Skylines II, usando i suoi componenti, le sue variabili CSS e le sue convenzioni, in tutti i temi dell'interfaccia. Nessun marchio o identità visiva propria oltre a un'icona per il pulsante.

## Evidence on Hand

- Dati di prova: fixture sintetiche generate da `ShelveIt/UI/preview/tools/make-sample-fixtures.js`; in alternativa una scansione locale (`ModsData/ShelveIt/scan-dump.json`), che resta sulla propria macchina e non va mai nel repository.
- CSS e risorse della UI del gioco nella cartella di installazione, versione 1.6.2f1.
- Nessuno screenshot del pannello in gioco è stato ancora raccolto.
- Prima release pubblica: 0.1.0 beta. Nessuna recensione, nessun dato d'uso.

## Product Principles

1. Insiemi prima dei singoli asset: ogni schermata aiuta a ragionare per pack.
2. Il suggerimento automatico deve dare pack specifici e subito utili, con il minimo di conferme.
3. Ospite discreto: la mod segue il gioco e Find It, senza introdurre abitudini nuove dove ne esistono già.
4. Leggera: nessun costo quando non è in uso.
