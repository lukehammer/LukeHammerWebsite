# Lucky Luke Magic - Studio Photo Catalog

Seed data file: `Api/data/photoshoot-catalog.json` (catalog id `lucky-luke-studio-2026`, version 1).

## Purpose

This catalog is the shot list for a one-day professional studio session that should produce about **100 distinct, usable marketing photos** of Luke Hammer as *Lucky Luke Magic*: website, brochures, social media, advertising, and press kits.

It is imported into the Photo Shoot Planner app. The director and photographer read the setup and shot instructions live on set from their phones, so every field is written as short, direct, imperative sentences that can be read from a few feet away.

Design principles:

- **Luke is the subject, not the props.** Most images are Luke alone on clean white seamless. Many of the most valuable shots are just his face, beard, bow tie, and gestures.
- **Every shot has a job.** Each entry states what the photo looks like, why it is useful (`marketingPurpose`), the expression, body and hands, framing, crop, and an acting note for Luke.
- **Family-friendly, polished, expressive.** Comic and absurd shots stay clean, bright, and clearly theatrical.
- **Minimal changeovers.** Shots are grouped into 25 setups so lighting, background, camera, outfit, and props change as rarely as possible.

## Totals

| Metric | Count |
| --- | --- |
| Setups | 25 |
| Shots | 100 |
| MustHave | 32 |
| Standard | 46 |
| Bonus | 22 |
| Estimated shooting time | ~260 min of shots + ~183 min of changeovers |

## Setup organization

Array order in the JSON is the shooting order. Shots are grouped by setup, and within each setup MustHave shots come first, then Standard, then Bonus.

| # | Setup key | Name | Shots | Must-haves |
| --- | --- | --- | --- | --- |
| 1 | `01-white-tight-headshot` | White Seamless - Tight Headshot | 5 | 4 |
| 2 | `02-white-expressions-a` | Expression Marathon A - Close-Up | 7 | 4 |
| 3 | `03-white-expressions-b` | Expression Marathon B - Close-Up | 6 | 1 |
| 4 | `04-white-left-space` | White Seamless - Waist-Up, Space Camera Left | 5 | 4 |
| 5 | `05-white-right-space` | White Seamless - Waist-Up, Space Camera Right | 5 | 3 |
| 6 | `06-white-three-quarter` | White Seamless - Three-Quarter Body | 6 | 3 |
| 7 | `07-white-full-length-portrait` | White Seamless - Full Length Portrait | 3 | 2 |
| 8 | `08-white-full-length-comedy` | White Seamless - Full Length Comedy | 4 | 2 |
| 9 | `09-white-wide-hero` | White Seamless - Wide Hero Banner and Cover | 4 | 2 |
| 10 | `10-white-physical-comedy` | White Seamless - Upper Body Physical Comedy | 6 | 1 |
| 11 | `11-white-blank-sign` | White Seamless - Blank Sign and Whiteboard | 5 | 1 |
| 12 | `12-kids-volunteer-reactions` | Kids Volunteer Set - Reactions | 4 | 2 |
| 13 | `13-kids-volunteer-trick` | Kids Volunteer Set - Helping With a Trick | 4 | 1 |
| 14 | `14-white-magic-cards` | White Seamless - Playing Cards Close-Up | 3 | 1 |
| 15 | `15-white-magic-wand-silk` | White Seamless - Wand and Silks | 2 | 1 |
| 16 | `16-white-magic-classic-props` | White Seamless - Hat, Rope, Cups and Balls | 4 | 0 |
| 17 | `17-white-empty-hands-magic` | White Seamless - Empty Hands Magic | 3 | 0 |
| 18 | `18-white-absurd-props` | White Seamless - Absurd Comedy Props | 4 | 0 |
| 19 | `19-white-absurd-gone-wrong` | White Seamless - Trick Gone Wrong | 3 | 0 |
| 20 | `20-white-vest-sleeves` | Outfit Change - Vest and Rolled Sleeves | 4 | 0 |
| 21 | `21-teal-family` | Teal Backdrop - Family Event Color | 2 | 0 |
| 22 | `22-teal-reactions` | Teal Backdrop - Reaction Close-Ups | 3 | 0 |
| 23 | `23-soft-pink-gold-party` | Soft Pink and Gold Party | 2 | 0 |
| 24 | `24-burgundy-elegant` | Burgundy Backdrop - Elegant Events | 3 | 0 |
| 25 | `25-charcoal-seated-stool` | Charcoal Backdrop - Seated Stool Portraits | 3 | 0 |

### Why this order

1. **Must-haves first.** Setups 1-9 hold 25 of the 32 must-haves. If the day runs long, the commercially essential images are already captured.
2. **Camera moves outward in steps.** Close-up headshots and expression marathons (1-3) share one lighting and lens setup. The camera then backs out to waist-up (4-5), three-quarter (6), full length (7-8), and very wide (9), so each change is a small adjustment rather than a full re-light.
3. **All white seamless is contiguous (1-20).** Background paper is changed only once the white work is complete. The colored backdrops (21-25) come last, ordered teal, soft pink, burgundy, then charcoal so the darkest, low-key lighting is the final re-light.
4. **Kids at a fixed mid-session slot (12-13).** The children arrive at a scheduled time, shoot on the same white set with a small magic table, and leave. The table then stays for the adult magic-prop setups (14-16).
5. **Props escalate late.** Magic props (14-17) and absurd comedy props (18-19) come after the core portraits. The messy "trick gone wrong" setup is right before the outfit change so the suit gets reset anyway.
6. **Outfit change once.** The jacket-off vest and rolled-sleeves look (20) is the final white setup, and the full suit goes back on for the colored backdrops.

## Category breakdown

| Category | Count | What it covers |
| --- | --- | --- |
| EssentialPortrait | 15 | Headshots, three-quarter, full length, brand-color portraits |
| AdvertisingNegativeSpace | 15 | Pointing or looking left, right, and up into empty space, imaginary products, blank sign and whiteboard |
| FacialStorytelling | 16 | Astonishment, mischief, suspicion, delight, confusion, deadpan, frustration, and more |
| PhysicalComedy | 12 | Victory pose, bow, shrug, innocent look, imaginary disaster, leaning in |
| AbsurdComedy | 7 | Foam cleaver prop, rubber chicken, wilting wand, trick-gone-wrong aftermath |
| MagicianSpecific | 12 | Cards, wand, silks, top hat, rope, cups and balls, empty-hands magic |
| ChildVolunteer | 8 | Two child volunteers: amazement, shared laughter, helping with a trick |
| WebsiteSocial | 15 | Hero banner, Facebook cover, About page, birthday, wedding, corporate, Instagram square, press kit |

## Must-have strategy

The 32 must-haves are the images the business cannot do without:

- **Core headshots (4):** warm smile, confident entertainer, laughing naturally, direct trustworthy gaze.
- **Top expressions (5):** genuine astonishment, mischievous grin, suspicious side-eye, pure delight, big excitement.
- **Negative-space ads (6):** pointing to and looking toward empty space on the left AND right, presenting an imaginary product, and the blank whiteboard template.
- **Body portraits (3):** arms crossed, welcoming open arms, full-length standing.
- **Website and promo (6):** website hero banner, Facebook cover, About page portrait, birthday party ad, corporate event promo, wedding and cocktail-hour promo.
- **Physical comedy (3):** exaggerated victory pose, theatrical bow, leaning into camera.
- **Kids (3):** child amazed by the reveal, shared laughter, child helping with the wand.
- **Signature magic (2):** playing-card fan reveal and the signature wand pose.

Birthday, wedding, and corporate promos are deliberately shot on white early in the day instead of on colored backdrops later. White cut-outs work in nearly every layout, and keeping them early protects them if time runs short. The colored-backdrop setups at the end are Standard and Bonus variety.

## Child-volunteer plan

- **Dedicated setups only:** `12-kids-volunteer-reactions` and `13-kids-volunteer-trick`. Both have `requiresChildren: true`, and they run back to back so the children are on set for under 40 minutes (about 23 minutes of estimated shooting time).
- **Fixed arrival time:** schedule the children to arrive about 30 minutes before setup 12. Release them as soon as setup 13 is finished.
- **Consent first:** a **signed guardian consent and a signed model release for commercial use** must be on file for each child before they enter the set. This reminder appears in the setup notes and in every child shot's notes. A guardian stays on set at all times.
- **Minimal data about children:** the catalog refers to them only as "Child A (~7)" and "Child B (~4-5)". No names, descriptions, or personal details of the children are stored in the catalog or the planner. Keep signed releases in your normal business records, not in this app.
- **Kid-friendly direction:** real reactions over perfect posing, no hands on the children, short shots (2-4 minutes each), and breaks and snacks off set.

## References, inspirations, and search phrases

Each shot may include an `inspiration` (performers or characters such as Groucho Marx, Rowan Atkinson as Mr. Bean, Gene Wilder, or Christopher Lloyd as Doc Brown) and one or two `referenceSearchPhrases`.

- These are for **expression, energy, and composition only**. Luke is not impersonating anyone.
- Search phrases are meant for the director or Luke to look up references privately while preparing. The app does **not** scrape, download, or store copyrighted images.
- If visual references are wanted inside the planner, add **licensed images or photos you supply yourself** (for example test shots from a previous session) later as user content.
- Not every shot has an inspiration. Many are purely commercial and left blank on purpose.

## How to edit the catalog

1. Edit `Api/data/photoshoot-catalog.json`. It must stay valid JSON (no comments, no trailing commas) using the enum strings below.
2. **Keys are stable identifiers.** `setups[].key` and `shots[].key` are used for idempotent seeding. Never rename an existing key; changing it makes it a new item.
3. **Re-seeding only adds.** On import, the app adds setups and shots whose key is not already present. It **never overwrites** existing items, so edits made in the app on set (wording, priority, notes, order) are preserved.
4. **Bump `catalogVersion`** whenever you add new setups or shots, so the app knows there is new seed content to merge.
5. **Deletions are remembered.** If a seeded setup or shot is deleted in the app, its key is recorded and it will not be re-added on the next seed.
6. Keep the array order meaningful. Setups are in shooting order. Shots are grouped by setup in setup order, with MustHave, then Standard, then Bonus within each setup.
7. Every `shots[].setupKey` must match an existing `setups[].key`.

### Enum values

- `category`: `EssentialPortrait`, `AdvertisingNegativeSpace`, `FacialStorytelling`, `PhysicalComedy`, `AbsurdComedy`, `MagicianSpecific`, `ChildVolunteer`, `WebsiteSocial`
- `priority`: `MustHave`, `Standard`, `Bonus`
- `orientation`: `Vertical`, `Horizontal`, `Both`, `Square`
- `negativeSpace`: `None`, `Left`, `Right`, `Top`, `Bottom`

### Quick validation (PowerShell)

```powershell
$c = Get-Content -Raw -Encoding UTF8 'Api\data\photoshoot-catalog.json' | ConvertFrom-Json
$c.setups.Count; $c.shots.Count
($c.shots | Where-Object priority -eq 'MustHave').Count
$c.shots | Group-Object category | Select-Object Name, Count
($c.shots.key | Group-Object | Where-Object Count -gt 1).Name          # duplicate keys (should be empty)
($c.shots | Where-Object { $c.setups.key -notcontains $_.setupKey }).key  # unknown setupKey (should be empty)
```

### Brand colors used in colored setups

Burgundy `#831B3F`, Gold `#CC9511`, Teal `#08AAB9`, Soft Pink `#E4B6B6`, Charcoal `#2B2B2B`.
