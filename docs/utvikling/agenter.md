# Agenter og felles utviklingsdatabase

Fire agenter ligger i `.claude/agents/`. De bruker de samme kildene, slik at ingen starter uten historikk.

| Agent | Brukes til |
|---|---|
| `showcue-utvikler` | Nye funksjoner, likt på Windows og Mac, med tester og dokumentasjon |
| `showcue-lyd` | Lydmotor, lydenheter, ASIO, tidskode, latens |
| `showcue-robusthet` | Show-sikkerhet: krasj, ugyldige data, avslutning, regresjonstester |
| `showcue-ux` | Brukervennlighet og GUI. Viser forslag som bilde før noe endres |

## Felles utviklingsdatabase
Databasen er repoet selv, slik at alt følger koden og kan slås opp med referanser:

- `docs/utvikling/`: arkitektur, regler, beslutninger, fallgruver, testing, bygg og historikk.
- `docs/utvikling/historikk.md`: versjon og PR-nummer. Detaljer hentes med `gh api repos/trondmorset-lab/Showcue/pulls/<nr>` og `git log -- <fil>`.
- `docs/utvikling/agentlogg.md`: en linje per arbeidsøkt (dato, agent, gren/PR, hva, åpne punkter). Hver agent leser slutten av den først og skriver til den sist.
- Kopi i Claude-prosjektet «Showcue» (`showcue/utvikling/`) slik at den også er tilgjengelig utenfor repoet.

## Arbeidsrutine for alle agenter
1. Les reglene og logg før arbeid. 2. Egen gren fra `main`. 3. Små endringer med tester på begge plattformer.
4. Oppdater dokumentasjon og logg i samme PR. 5. Aldri merge; eieren ber om det.
