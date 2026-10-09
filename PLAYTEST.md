# Fusion Kingdoms — play-test checklist

Everything below has passed automated tests (`tools/headless`, `dotnet run -c Release -- <seed> 1 npctest`) and
screenshot checks, but nobody has played it by hand yet. Please tick what works and note what feels wrong.

## Showcase: AI vs AI (main menu → "Watch AI vs AI")
- [ ] Both rulers start, build farms, houses, mills and reach Age 2–3 within ~6 minutes.
- [ ] The auto camera finds fights, raids, captures, sabotage, Spire alerts; it cuts to a whole-map overview now and then.
- [ ] Banners for age-ups, fusions, research, Spire milestones are readable and not too frequent.
- [ ] WASD / mouse drag takes the camera; it gives control back after ~12 s; Tab toggles auto; 1 / 2 jump to Blue / Red.
- [ ] Speed buttons (||, 1x, 2x, 4x, 8x) and Space to pause work.
- [ ] The end screen shows the winner, reason, scores and both rulers' war reports; "Watch another match" starts a new map.
- [ ] Try all 9 personality pairings (Blue vs Red: Breeder / Warden / Archmage) and the map styles; note any boring or broken matches.

## Playing yourself
- [ ] The tutorial hints appear on a first match and tick off as you do each step (can be hidden; restart it in the menu).
- [ ] Box-select, Ctrl+A, `.` (idle), double-click (same class on screen), Ctrl+1–9 / 1–9 groups.
- [ ] Right-click: crop/tree/rock = gather, site = build, enemy = capture, enemy store = raid, other enemy building = sabotage, hurt friend = heal, ground = move, Shift = guard.
- [ ] Keys: G guard, C capture, Z hold, X stop, F Free Will, B build, H home, F1 help.
- [ ] Build menu (B): every building places with the green/red footprint preview; red cost = can't afford (click says what's missing).
- [ ] Walls: choose Wall, click-drag a line; gates let your people through and stop enemies; enemies break walls when stuck.
- [ ] Cradle: newborn job, rally point (right-click the map with the Cradle selected), cancel from the queue, Shift-click to repeat.
- [ ] Keep: research list (one at a time), Advance Age, barter. Upgrades on House/Farm/Watchtower/Guard Post/Granary.
- [ ] Fusion Altar, Training Grounds (incl. Mirror Drill and Brawl), Mutation Pit, Dungeon actions.
- [ ] Lumen Spire: charges slowly; sabotage drains it; win at 1,500.

## Look and sound
- [ ] Day/night, rain (with thunder), fog; fireflies at night; birds by day, crickets at night.
- [ ] Music: menu, day, night, battle; sound effects for work, fights, captures, building.
- [ ] Menu: living backdrop, settings (map size/style/resources/start/length/wildlife, difficulty), volume sliders.

## LAN
- [ ] Host and join on two machines; orders, building, research and settings (map style etc.) match on both sides.

## Notes
Write anything odd here: what you did, what you expected, what happened.
