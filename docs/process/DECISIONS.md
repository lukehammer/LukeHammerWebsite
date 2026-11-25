# Decision log

Newest first. Append only: a changed decision gets a new entry; old entries are never edited. Photo shoot decisions made before this log are in [docs/photoshoot/POC-DECISIONS.md](../photoshoot/POC-DECISIONS.md).

## 2026-11-18: ShowStill demo linked on the Projects page
- **Decided (Luke):** the ShowStill card on `/projects` has a "Demo" badge and two links, both opening in a new tab with no `nofollow`: "See how it works (preview)" (brave-cliff-03cdb131e.6.azurestaticapps.net/demo.html) and "View the photo library" (the same site's home page, 12 finished, labelled photos). The text says the app itself is still being built.
- **Replaces:** the 2026-11-14 choice that ShowStill had a description only and no link. ShowStill stays the last card.

## 2026-11-14: Public Projects page at /projects
- **Decided (Luke):** a public page at `/projects` with a "Projects" link in the left menu, listing his projects with a short plain-words summary and a direct link. Links are ordinary backlinks (no `nofollow`), opening in a new tab.
- **Entries, in order:** Lucky Luke Magic (luckylukemagic.com, his main site); Capture Checklist (capturechecklist.com); Show Planner (its Azure address); Greg Moreland's website; ShowStill.
- **Greg Moreland:** links the preview address (gregmoreland-preview.azurewebsites.net), not gregmoreland.com, which is still the old site.
- **ShowStill:** description only, marked "In development", no link.

## 2026-11-14: Sports test uses a fixed date; fixes QA-1
- **Decided:** `BainSoccer_schedule_is_upcoming_only_and_ordered` passes a fixed "today" (2026-09-01) to `SportsSchedules.GetUpcoming` through a new optional `today` parameter, and now also checks the list is not empty. It no longer fails when the real date passes the seeded games.
- **Same fix for the Baseball and Combined schedule tests** (fixed 2026-09-01, plus a not-empty check), so none of the schedule tests depend on the real date.
- **Live site unchanged:** the parameter defaults to `DateTime.Today`. The "known date-dependent test" note is removed from the project profile.

## 2026-11-13: Uses the shared agent-team plugin, version 1.2.0+202611132243
- **Decided (Luke):** this project uses the shared `agent-team` plugin (`lukehammer/agent-team-playbook`). The main Claude session is a dispatcher that hands work to child sessions and pushes only after Luke's OK.
- **Project facts:** `docs/process/PROJECT_PROFILE.md`. The plugin is switched on in `.claude/settings.json`.
- **Retired copies:** none; the project had no copied playbook or review skills.
