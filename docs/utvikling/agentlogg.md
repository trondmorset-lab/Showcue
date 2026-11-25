# Agentlogg

En linje per arbeidsøkt. Nyeste nederst. Format: dato · agent · gren/PR · hva · åpne punkter.

- 2026-11-24 · oppsett · `agenter` · Fire agenter og denne loggen opprettet · Ingen åpne punkter
- 2026-11-20 · utvikler · `gruppe-cuer` / #21 · Gruppe-cue (0.17.0) på Windows og Mac · Venter på at eieren ber om merge
- 2026-11-20 · ux · (ingen gren) · GUI-forslag vist som bilde · Venter på godkjenning før noe bygges
- 2026-11-24 · robusthet · `robusthet` / #25 · Ødelagte showfiler, OSC-blob, DMX «NaN», `\xHH`, avslutning (0.16.1) · Åpne: unntak i `CueEngine.Tick` (Windows), grenser ved lasting på Mac
- 2026-11-24 · lyd · `asio-inngang` / #24 · Windows: live-innganger via ASIO i samme økt som utgangen (0.18.0) · Må prøves på ekte ASIO-utstyr før den slås på som standard
- 2026-11-24 · utvikler · `forhandssjekk` / #26 · «Sjekk showet» på Windows og Mac: ren `PreflightCheck`, vindu og tester (0.19.0) · Må prøves på ekte maskin (enheter, skjermer); gruppe-cuer sjekkes ikke ennå
- 2026-11-24 · robusthet · `robusthet-2` / (åpen) · 0.19.1: unntak i `CueEngine.Tick` fanges per cue (Windows), Mac begrenser verdier ved lasting, «Sjekk showet» kjenner gruppe-cuer, innstillinger begrenses ved lasting; tester på begge plattformer · Kompilert bare i CI; unntaksløypa i `CueEngine.Tick` selv (WPF) er ikke enhetstestet, bare `SafeTick`
- 2026-11-24 · ux/utvikler · `ny-gui` / #28 · Nytt hovedvindu etter godkjent mockup: «spilles nå»/«neste»/deaktivert, kolonnen Gjenstår, faner i inspektøren, større GO og neste cue, bedre aktive cuer, på Windows og Mac (0.20.0) · Ikke kompilert lokalt (CI er eneste sjekk); skjermbildene i manualene må tas på nytt av arbeidsflyten «Skjermbilder»; Windows viser fremdriftslinjen under navnet, ikke over hele raden
- 2026-11-24 · utvikler · `klokke-trigger` / (åpen) · Klokkeslett-trigger (0.21.0): `ClockTrigger` per cue, `ClockTriggers`-logikk, innstillingen «Klokke-triggere aktive» (av), felt i Tidsstyring, advarsel i Sjekk showet, på Windows og Mac · Ikke kompilert lokalt (CI er eneste sjekk); selve veggklokka/tick-løkka (`CheckClockTriggers` i `MainWindow`, `AppModel.checkClockTriggers`) er ikke enhetstestet, bare den rene logikken
