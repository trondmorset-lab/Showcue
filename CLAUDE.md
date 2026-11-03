# ShowCue – for utviklere og KI-assistenter

Les `docs/utvikling/README.md` før større endringer: arkitektur, regler, bygg/utgivelse, testing, beslutninger og fallgruver.

Det viktigste:
- To utgaver som deler showformat og oversettelser: `Windows/` (C#/.NET 10/WPF/NAudio) og `Mac/` (Swift/SwiftUI/CoreAudio).
  Funksjoner og tester lages på begge.
- Ingen referanser til andre programmer, teknologier eller selskaper i kildekoden; nøytrale navn; kommentarer beskriver hva koden gjør.
- All brukertekst på norsk i koden, oversatt i `i18n/en.json` (`python3 tools/i18n.py` skal gi 0 feil).
- Egen gren + pull request; CI grønn på Windows og Mac; merge bare når eieren ber om det. Øk `VERSION` og skriv i begge endringslogger.
- Oppdater `docs/utvikling/` i samme PR når noe vesentlig endres.
