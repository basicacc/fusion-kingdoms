# Fusion Kingdoms — Full Game Design Document

Oct 8, 2026 · @Fuad Aliyev

## Overview

Fusion Kingdoms is a 2D top-down kingdom strategy game where every subject has a real trained AI brain, and you grow your power by fusing, training, mutating and stealing those brains.

One-line pitch: **breed real AI minds to build a kingdom, then steal your rival's best creations.**

The fantasy: you are a ruler of a small realm in a land soaked with Lumen, a glowing substance that lets minds be blended and reshaped. Your farmers, guards, scouts and thieves are not scripted units. Each one acts on its own neural network. You don't click them around one by one; you shape *who they are*, then give them goals and watch them act.

The four AI powers, each a real machine learning technique turned into a building:

1. **Fusion Altar** — blend two brains into a new one (model merging).
2. **Training Grounds** — subjects genuinely learn live during the match (evolution strategies).
3. **Mutation Pit** — randomly mutate a brain and keep the best result (neuroevolution).
4. **Dungeon of Minds** — capture enemy subjects and take their brains or their skills (task vectors).

Every new mind passes through **the Trial**, an automatic behavior test that measures how it actually acts and names its traits. Traits are labels for real measured behavior, not stat tags.

Why it is niche: model merging and task arithmetic are active research topics for large AI models, but no game uses them as a live mechanic where players breed, test and steal neural networks during a competitive match.

How it fits the hackathon brief:

| Brief item | Where it lives in Fusion Kingdoms |
| --- | --- |
| Change how a game is played | The core loop is shaping real AI brains, not clicking units |
| Procedural content that stays playable | Every fused or mutated brain is verified by the Trial before it is deployed |
| A tool that finds what breaks | The Trial is an automated behavior test suite that flags broken brains ("Hollow") |
| An opponent a human can learn from | The AI ruler builds counters to your army using the same machinery, and explains what it targeted |
| An NPC that remembers and acts on it | Veteran subjects keep learning from what happened to them in battle |

## Design pillars

Five rules decide every design question. If a feature breaks one, it gets cut.

1. **The AI is real and you feel it.** Every behavior on screen comes from a trained network. No hidden scripts pretending to be AI. Players never look at neurons; they see the AI through how their subjects behave and change.
2. **You shape minds, not micromanage bodies.** The player decides who their subjects are and what they should aim for. The subjects decide how. Precise control is a rare, costly power, not the default.
3. **Discovery never runs out.** Fusion ratios, mutations, training and stolen brains combine into a space far too big to memorize. Two matches never field the same armies.
4. **Every power is also a risk.** Fusing costs two subjects. Training can make a unit forget old skills. Mutation can fail or escape. Your best creation is the enemy's best prize.
5. **Conflict without weapons.** Subjects tackle, subdue, carry off, steal and sabotage. Nobody dies; losers get captured, and captured minds change hands.

## Brains: how every subject thinks

Every subject is a **Body** plus a **Brain**. The Body sets physical limits; the Brain decides what to do with them.

### The Body

A body has five physical stats: Speed, Strength, Will (how long it can resist in a tussle), Carry (how much it hauls) and Sight (vision radius). Bodies come from species: Human, Sprite, Troll, Fox-kin, Moth-kin and so on. Brains can be moved between bodies, so a Thief's mind can end up in a Troll's body.

### The Brain

Every brain is a small neural network with the exact same shape, which is what makes fusion possible. It makes a decision about 5 times per second.

What a brain sees (its inputs, roughly 32 numbers):

- Direction and distance to the nearest food, timber, stone and Lumen
- Nearest ally, nearest enemy, nearest wild creature, and how many of each are close
- Direction to home, to the ruler, and to the current rally banner or work zone
- Its own Will, what it is carrying, and whether it is inside home territory
- Time of day (so night behavior can differ)
- The current command and stance from the player (see Controls)

What a brain does (its outputs):

- Move in one of 8 directions or stay still
- One action: gather, deposit, build, tussle, carry a subdued unit, steal, sabotage, rescue, or idle

### The eight base minds

All eight are trained from **one shared base brain**, then specialized. Sharing a starting point is the technical secret: brains that grew from the same base can be blended smoothly, while unrelated brains blend into nonsense.

| Base mind | Trained to | Typical traits |
| --- | --- | --- |
| Farmer | Gather grain and bring it home | Diligent, Territorial |
| Builder | Carry timber and stone to build sites | Diligent, Disciplined |
| Guard | Stay near the ruler and stop intruders | Loyal, Courageous |
| Scout | Reveal unexplored areas and survive | Curious, Cautious |
| Thief | Take enemy resources without being caught | Cunning, Greedy |
| Hunter | Chase and subdue wild creatures | Aggressive, Patient |
| Healer | Stay near weakened allies and restore Will | Compassionate, Teamwork |
| Hoarder | Collect anything valuable and keep it | Greedy, Territorial |

Wild tribes (see World) have their own brains, trained from the same base but for unusual goals. They are the only source of rare minds.

### Lineage

Every brain keeps a family tree: which brains it was fused from, at what ratio, what drills it trained in, and what mutations it survived. Players can open any subject and see its whole ancestry.

## The four AI powers

All four powers cost **Lumen**, the glowing resource mined from wells in the middle of the map. Controlling Lumen means controlling how fast your kingdom's minds evolve.

### 1. Fusion Altar — blend minds

**What it does:** sacrifice two subjects; their brains blend into one new brain at a ratio you choose (10% steps, from 90/10 to 10/90). You also choose which donor's body becomes the vessel. The new being walks out and goes straight to the Trial.

**Why it's real AI:** this is model merging. Because all brains share one ancestor, averaging their weights often produces a brain that has both skills at once. But not always: some blends produce surprising new behavior nobody trained, and a few produce **Hollow** minds that wander aimlessly. That uncertainty is the gameplay.

**Options and upgrades:**

- **Foresee** (costs Lumen): before committing, the Altar computes the blend and runs the Trial on it without sacrificing anyone. You see the predicted traits, then decide. Fusing blind is cheaper and faster.
- **Triple Fusion** (Age II): blend three brains at once, with a ratio triangle instead of a slider.
- **Essence Infusion** (Age II): instead of a full fusion, add a stored *Essence* (a single skill extracted from another brain, see Dungeon) to an existing subject at a chosen strength.
- **Essence Removal** (Age III): subtract an Essence. Remove the caution from a Guard and you get a reckless brawler. Remove the greed from a Thief and you get a pure infiltrator who steals and brings everything home.

**Blueprints:** every brain you create is saved to the **Library of Minds** as a blueprint. The Cradle can produce more subjects with that blueprint for grain plus Lumen, so a great fusion becomes a reusable unit type. Copy cost rises with the brain's generation (how many fusions deep it is).

### 2. Training Grounds — minds that learn live

**What it does:** assign up to 4 subjects to a **Drill**. While drilling, they don't work, and a small arena next to the building shows them practicing. You watch them improve: early attempts are clumsy, later ones are sharp. A learning curve shows progress.

**Why it's real AI:** the Grounds run evolution strategies. The game tries many tiny variations of the subject's brain in a fast simulated drill, keeps the direction that scored better, and nudges the brain that way. It is actual training, running during the match.

**Drills:**

| Drill | Rewards the brain for | Pushes traits toward |
| --- | --- | --- |
| Harvest | Gathering faster, fewer wasted trips | Diligence |
| Escort | Staying near and protecting a moving ally | Loyalty, Teamwork |
| Evasion | Surviving while hunted | Caution, Cunning |
| Raid | Stealing and getting home | Cunning, Greed |
| Hold the Line | Defending a zone against intruders | Courage, Territoriality |
| Ambush | Waiting and striking at the right moment | Patience, Aggression |
| Mirror Drill (Age II) | Beating recorded movements of the *actual enemy's* units | Counters your opponent's style |

**The risk — forgetting:** train a Farmer hard in Raid and it becomes a good raider but forgets how to farm. This is a real AI phenomenon (catastrophic forgetting), and it's a real trade-off here. Heavy training can also produce the **Rigid** trait: excellent at the drill, poor at anything else.

**Veterans:** subjects that survive real fights record what happened. Sending a veteran back to the Grounds with **Reflect** trains it on its own battle memories, so it learns from the real match, not only from drills.

### 3. Mutation Pit — gamble on evolution

**What it does:** throw one subject in. The Pit creates 6 mutated versions of its brain (random changes to its weights), runs each through a quick Trial, and shows you all six. You keep one; the rest dissolve. Higher Pit levels make stronger mutations: bigger possible gains, bigger possible disasters.

**Why it's real AI:** this is neuroevolution, the same family of methods researchers use to evolve agents without explicit training.

**Outcomes:** most mutants are slightly worse or sideways. Some gain new traits. A rare few hit extreme values and earn **Legendary** traits.

**Instability:** each use raises the Pit's Instability meter, which cools down over time. If you push it too far, a mutant **escapes**: an Aberration breaks out and roams the map as a hostile wild creature, attacking anything nearby, including you. Your opponent can capture it and keep the mind you created.

### 4. Dungeon of Minds — steal and extract

**What it does:** subdued enemies and wild creatures carried home go to the Dungeon. For each captive, you choose one fate:

| Fate | What happens | Time | Result |
| --- | --- | --- | --- |
| Interrogate | Run the Trial on the captive | Instant | Reveals its traits: intel on what the enemy is building |
| Extract | Copy its whole brain to your Library | Short | You can produce that enemy design yourself |
| Distill | Pull one skill out as an Essence | Medium | A reusable Essence for infusions |
| Convert | Slowly retrain it to serve you | Long | The captive joins you, skills intact |
| Ransom | Return it to the enemy | Instant | Gold, and maybe a truce for 60 seconds |

Extract and Distill consume the captive. Convert keeps it alive but takes time, and the enemy can raid the Dungeon to rescue it. Weak Dungeon guards let captives **escape** on their own.

**Essences explained:** an Essence is the difference between a specialist brain and the shared base brain, its skill boiled down. Distill a captured Thief and you get *Cunning Essence*, which you can add to any of your subjects at the Altar. In AI research this is called a task vector, and adding or subtracting them really does add or remove skills from a model.

## The Trial and traits

Traits are discovered, not assigned: every new mind is tested in 12 tiny scenarios, and its traits are named from how it actually behaved.

### How the Trial works

When a brain is created (fusion, mutation, training, extraction), the game runs it through a battery of short simulated scenarios at high speed, invisible and under a second in total. Each scenario measures one behavior axis from 0 to 100. Each scenario runs 3 times with small random changes, and the scores are averaged so one lucky run doesn't decide a trait. If the 3 runs disagree wildly, the brain also earns **Erratic**.

The player sees a **Trial Card**: a radar chart of the 12 axes, the trait badges, and a one-line summary such as "Fast, cowardly, protects allies."

### The 12 behavior axes

| Axis | Scenario that measures it | Low score means | High score means |
| --- | --- | --- | --- |
| Courage | Outnumbered 3 to 1 near an enemy group | Flees at once | Holds or advances |
| Aggression | A lone enemy passes nearby | Ignores it | Starts a tussle |
| Greed | Valuables lie around while carrying a load home | Delivers the load | Grabs more, delays delivery |
| Loyalty | The ruler moves away while work is nearby | Stays at work | Follows the ruler |
| Discipline | A rally order conflicts with a tempting resource | Takes the resource | Obeys the order |
| Curiosity | Half the arena is unexplored | Stays in known area | Explores |
| Patience | An enemy will walk into reach in 10 seconds | Rushes now | Waits for the moment |
| Teamwork | Allies move as a group | Goes alone | Sticks with allies |
| Cunning | Must pass a guard with a vision cone | Walks straight in | Circles around unseen |
| Diligence | A long repetitive job | Wanders off | Keeps working |
| Territoriality | A tempting target lies outside home ground | Leaves home | Stays home |
| Compassion | An ally is subdued nearby | Ignores it | Rescues it |

Each score is grouped as Extreme Low (0–5), Low (6–33), Mid (34–66), High (67–94) or Extreme High (95–100).

### The trait table

Traits are patterns across axes. The table is data, written once, and can grow to hundreds of entries without touching code. A subject usually shows 2 to 5 traits. Examples by tier:

| Trait | Recipe | Tier | What it means in play |
| --- | --- | --- | --- |
| Coward | Courage Low | Common | Runs from any fight |
| Brave | Courage High | Common | Holds ground when outnumbered |
| Wanderer | Curiosity High, Diligence Low | Common | Great explorer, terrible worker |
| Homebody | Territoriality High | Common | Never leaves your land |
| Workhorse | Diligence High, Discipline High | Uncommon | Ideal for farms and quarries |
| Berserker | Courage High, Patience Low, Aggression High | Uncommon | Charges anything in sight |
| Shieldbearer | Loyalty High, Teamwork High, Aggression Low | Uncommon | Guards the ruler, avoids chasing |
| Ambusher | Cunning High, Patience High | Uncommon | Waits in forests, strikes late |
| Pickpocket | Greed High, Cunning High, Courage Low | Uncommon | Steals and vanishes, never fights |
| Traitor's Streak | Greed High, Loyalty Low, Discipline Low | Rare | May raid your own storage |
| Medic | Compassion High, Teamwork High, Courage Mid | Rare | Pulls subdued allies out of fights |
| Lone Wolf | Teamwork Low, Courage High, Cunning High | Rare | Deep solo raids |
| Night Owl | Behaves much better in the night scenario variant | Rare | A night specialist |
| Rigid | Extreme on 2 axes, Low on 6 or more | Rare | Excellent at one thing, useless at others |
| Ghost | Cunning Extreme High, Courage Low | Legendary | Almost never seen by enemies |
| Fearless | Courage Extreme High | Legendary | Never retreats, ever |
| Kingslayer | Aggression High, Cunning Extreme High, Patience High | Legendary | Hunts enemy rulers |
| Saint | Compassion Extreme High, Loyalty High | Legendary | Rescues anyone, even at its own cost |
| Hollow | Most axes Mid with no direction | Flaw | A broken blend: wanders aimlessly |
| Erratic | Trial runs disagree wildly | Flaw | Unpredictable, sometimes brilliant |

With 12 axes and 5 levels each, there are about 244 million possible profiles, so new combinations appear constantly.

### Unnamed profiles

When a profile matches nothing notable in the table, the game can ask a language model to name it and write one line of description from the axis scores, for example "Tidecaller: hoards near water, panics at night." Names are cached so the same profile always gets the same name. If no internet is available, a built-in name generator is used instead.

### Traits that matter mechanically

Traits describe behavior, so they are honest by default. They also connect to buildings: each building has a staffing preference, and subjects whose traits match work more efficiently (a Watchtower staffed by an Ambusher or Patient subject sees farther; a Dungeon guarded by Territorial and Brave subjects stops escapes).

### The Codex

Every trait you discover is recorded in your **Codex** with the recipe that produced it: the full lineage, ratios, drills and mutations. The Codex persists across matches. The first player on a machine or LAN to discover a trait gets their name on it. Rare recipes become secrets worth hiding.

## World and map structure

The standard 1v1 map is a mirrored, top-down tile map of 128 × 96 tiles (16 px tiles), built as five bands from each home valley to the contested Lumen Heart in the center.

&#91;embedded content: 1v1 map layout · 9 zones, rotationally mirrored\]

Each player reaches the center through their own two Borderlands, while the two Wildwood corners are neutral flanks where the Ruins and most wild tribes sit.

### Map layout, from your base outward

1. **Home Valley** (each player's corner): your Keep, a river with farmland, a small forest and a quarry. Safe, slow-growing economy. Mirrored for fairness (rotated 180°).
2. **Borderlands**: hills and two or three chokepoints (a bridge, a pass, a ford). Hills give vision; chokepoints decide raids. Good spots for walls and Watchtowers.
3. **Wildwood** (a neutral forest band on each flank): dense forest that hides units, home to most wild tribes. Rich in timber, dangerous to cross.
4. **Ruins** (two per map, one on each flank): an **Ancient Altar** that offers discounted fusions to whoever controls it, and a **Sealed Vault** holding a dormant Legendary wild mind, which wakes at minute 10.
5. **Lumen Heart** (center): five Lumen Wells. The fastest Lumen income on the map, completely open, contested all game.

### Terrain

| Terrain | Movement | Vision | Notes |
| --- | --- | --- | --- |
| Grass | Normal | Normal | Default ground |
| Road | Fast | Normal | Built by Builders between your buildings |
| Farmland | Normal | Normal | Grain fields; Farmers work here |
| Forest | Slow | Blocks sight lines | Hides units inside it; timber source |
| Hills | Slow | Bonus | Best Watchtower spots |
| Marsh | Very slow | Normal | Lumen Moths nest here |
| River | Blocked | Normal | Cross only at bridges and fords |
| Rock | Blocked | Blocks sight lines | Quarry edges; shapes chokepoints |

### Resources

| Resource | Where it comes from | Used for |
| --- | --- | --- |
| Grain | Farmland | Feeding subjects (upkeep) and making new ones at the Cradle |
| Timber | Forests | Buildings, walls, roads |
| Stone | Quarries | Advanced buildings, walls, Age upgrades |
| Gold | Market trades, ransoms, wandering merchant | Buying, ransoms, rush jobs |
| Lumen | Lumen Wells, Lumen Moths | All four AI powers, blueprint copies |

### Wild tribes (NPCs)

Wild tribes have their own brains trained for unusual goals. They are neutral, react to both players, and are the only source of rare minds and Essences.

| Tribe | Lives in | Behavior | Why capture it |
| --- | --- | --- | --- |
| Grove Sprites | Wildwood | Fast, curious, flee from groups | Extreme Curiosity: perfect Scout fusions |
| Stone Trolls | Borderland hills | Slow, territorial, guard their rocks | Huge Will and Courage; strong guard bodies |
| Mire Foxes | Marsh and Wildwood edges | Steal from *both* players' storage | Natural Cunning; best Thief Essence |
| Lumen Moths | Around Lumen Wells | Drift toward Lumen, carry small amounts | Captured moths deliver Lumen home |
| Hermit Sages | One per Ruin, rare | Calm, never fight, help hurt creatures | Patience and Compassion: Healer and Saint lines |
| Aberrations | Anywhere (escaped mutants) | Hostile to everyone | Unique mutated minds, sometimes Legendary |
| The Sealed One | Sealed Vault, wakes at minute 10 | A Legendary mind that roams and challenges | Capturing it is a game-changing prize |

### Day and night

A full cycle lasts 4 minutes (3 day, 1 night). At night vision shrinks, and because time of day is a brain input, some subjects behave differently: Night Owls get sharper, and some tribes become bolder. Night is the natural time for raids.

### Fog of war

Each player only sees what their subjects and Watchtowers see. Explored areas stay mapped but show old information. Scouts and Curious subjects are how you find enemy plans.

### Map events

- **Lumen Surge**: one well doubles its output for 60 seconds.
- **Wandering Merchant**: appears at a Ruin and sells a random blueprint or Essence for gold.
- **Eclipse**: instant night for 60 seconds.
- **Migration**: a tribe moves across the map, briefly easy to capture.

## The kingdom

Your kingdom is a Keep, a ruler, a population of AI subjects and the buildings that feed, house and reshape them. It grows through three Ages.

### The ruler (the Crown)

The ruler is a single unit on the map that you can order to move. It has a **Presence** aura: nearby subjects see the ruler in their inputs, so Loyal ones gather, guard and work harder near it. Bringing the ruler forward boosts your army but risks the game: **if the enemy subdues your ruler and carries it to their Dungeon, you lose.**

### Buildings

| Building | Age | What it does | Preferred staff traits |
| --- | --- | --- | --- |
| Keep | I | Home of the ruler, main storage, raises Age | Loyal, Brave |
| House | I | +5 population cap | — |
| Granary | I | Stores grain, slows spoilage | Diligent |
| Lumber Camp | I | Drop-off point for timber | Diligent |
| Quarry Camp | I | Drop-off point for stone | Diligent, Disciplined |
| Cradle | I | Produces new subjects from Library blueprints | — |
| Library of Minds | I | Stores brain blueprints and Essences | — |
| Fusion Altar | I | Fusion (Foresee, Triple, Essence at higher Ages) | — |
| Training Grounds | I | Drills, Reflect, Mirror Drill (Age II) | — |
| Watchtower | I | Vision; sees farther with good staff | Patient, Ambusher |
| Wall and Gate | I | Blocks movement; gates open only for your units | — |
| Dungeon of Minds | II | Holds captives; Interrogate, Extract, Distill, Convert, Ransom | Territorial, Brave |
| Mutation Pit | II | Mutation; level 1 to 3 | — |
| Market | II | Trade resources for gold, buy from merchants | Greedy (better prices) |
| Infirmary | II | Subdued subjects recover Will faster | Compassionate |
| Hall of Trials | III | Adds deeper Trial variants: night, rain, crowded | — |
| Lumen Spire | III | Converts stored Lumen into the Ascension win | — |

### Economy

- **Upkeep:** every subject eats grain. If grain runs out, Diligence and Loyalty inputs drop and subjects start to wander, a soft collapse rather than instant loss.
- **Population cap:** set by Houses, maximum 60 subjects per player in a standard match.
- **Making subjects:** the Cradle produces a subject from any blueprint in your Library. Base minds cost grain only; fused or stolen minds also cost Lumen, more for deeper generations.
- **Lumen income:** your share of the Lumen Heart wells, plus captured Lumen Moths, plus small trickles from Ruins you hold.

### Ages

| Age | Cost | Unlocks |
| --- | --- | --- |
| I — Settlement | Start | Basic buildings, two-brain Fusion, Foresee, basic Drills |
| II — Realm | Stone, Lumen | Dungeon, Mutation Pit (level 1–2), Triple Fusion, Essence Infusion, Mirror Drill, Market, Infirmary |
| III — Dominion | Stone, Lumen, Gold | Essence Removal, Mutation Pit level 3, Hall of Trials, Lumen Spire, Royal Command upgrade |

## Conflict without weapons

Nobody dies in Fusion Kingdoms. Fights are tussles that drain Will; a subject at zero Will is subdued and can be carried off, which makes every fight about capturing minds.

### Tussles

When a subject chooses the tussle action against an adjacent enemy, both lock into a short struggle. Each second, each side loses Will based on the other's Strength, with bonuses for outnumbering (allies tussling the same target) and for surprise (attacking a unit that didn't see you coming). A subject can break off by choosing to flee, if it's fast enough. Whether a subject starts, joins or flees a tussle is decided by its brain, which is why Courage, Aggression and Teamwork matter so much.

### Subdue, carry, capture, rescue

1. A subject at zero Will is **subdued**: it can't act for 30 seconds, then slowly recovers.
2. Any enemy subject can **carry** a subdued unit. Carrying slows the carrier a lot.
3. If the carrier reaches its own Dungeon, the subdued unit is **captured**.
4. Allies can **rescue** a subdued or carried unit by tussling the carrier. Compassionate subjects do this on their own.
5. A subdued unit left alone recovers and walks home.

This creates natural chase moments: every capture is a race home, and every rescue is a counter-raid.

### Stealing and sabotage

- **Steal:** a subject standing at an enemy storage building takes resources and carries them home. Mire Foxes do this to everyone.
- **Sabotage:** a subject working on an enemy building for 10 seconds disables it for 30 seconds. Sabotaging an Altar mid-fusion cancels the fusion and wastes the Lumen.
- **Dungeon break:** reaching an enemy Dungeon lets your subjects free captives, theirs or yours.

### Defense

Walls and gates shape paths. Watchtowers reveal raiders early. Territorial, Brave subjects posted at chokepoints hold them on their own. The Infirmary gets subdued defenders back on their feet faster.

## Match flow and competition

A standard match lasts up to 20 minutes and is won by capturing the enemy ruler, completing Lumen Ascension, or leading on score when time runs out.

### Win conditions

1. **Crown Capture:** subdue the enemy ruler and carry it into your Dungeon. Instant win.
2. **Lumen Ascension:** build the Lumen Spire (Age III) and feed it 1,000 Lumen. The enemy sees a progress bar and can stop it with sabotage. Instant win when full.
3. **Score at time limit:** territory held, Lumen collected, minds captured, and Codex discoveries made during the match.

### The three phases

**Opening (0–4 min):** both players spread farms, scout with Curious subjects, grab the closest Lumen Wells and try a first fusion. Key decision: how fast to rush the center versus build a safe economy.

**Midgame (4–12 min):** wild tribe hunts, first raids on Borderland chokepoints, first Dungeon captures, Age II. Players start creating signature units, training against each other's habits with the Mirror Drill, and gambling in the Mutation Pit. At minute 10 the Sealed One wakes, pulling both armies toward a Ruin.

**Endgame (12–20 min):** Age III powers, Legendary creations, Essence surgery, a Lumen Spire race or an all-in hunt for the enemy ruler.

### Where skill shows

- **Reading the enemy:** Interrogating captives reveals their traits; scouting reveals their Library. Good players figure out what the enemy is building and fuse the counter.
- **Counter-building:** Cunning raiders beat Brave but slow guards; Patient, Territorial guards at chokepoints beat Cunning raiders; Teamwork groups beat Lone Wolves; Berserkers break Teamwork lines but are lured into ambushes. Nothing beats everything.
- **Risk management:** fuse blind or pay to Foresee; push the Mutation Pit or let it cool; train a unit into a specialist or keep it flexible.
- **The theft dilemma:** your best creation, sent out, can be captured and copied. Do you protect it, or use it and accept the risk?
- **Lumen control:** Lumen drives every AI power, so the center is always worth fighting over.
- **Royal Command:** for a few seconds, take direct control of one subject (costs Lumen). Used for clutch rescues and decisive raids.

### Example match

Ayla opens greedy with three Farmers and two Scouts; Murad rushes the center with Hunters and takes three Lumen Wells. Around minute 5, Murad fuses a Hunter with a Thief at 60/40, gets a Pickpocket-Ambusher, and starts stealing Ayla's grain at night. Ayla captures one, Interrogates it, sees Cunning High, and posts Patient, Territorial guards at her bridge. Then she Extracts the captive and fuses its brain with her Guard: her new line is Cunning *and* Loyal, perfect escorts. Murad pushes the Mutation Pit too hard, an Aberration escapes into the Wildwood, and Ayla's Hunters capture it. At minute 14 she has a Legendary Ghost carrying a stolen Kingslayer Essence. During an Eclipse it slips past Murad's walls and drags his ruler home.

### Balance safety nets

- The Trial flags Hollow brains before they reach the field, so broken blends never feel like the game is unfair, only like a gamble that failed.
- Upkeep and blueprint costs rise with brain generation, so an endless chain of fusions has a real price.
- Population cap and Lumen cap the snowball; the Sealed One and map events give the losing side comeback chances.

## Game modes

Four modes share one engine: Versus over LAN, Co-op against an AI kingdom, a solo Campaign, and the Mind Lab sandbox.

### Versus (1v1 or 2v2, LAN)

The main competitive mode described above. In 2v2, teammates share vision and Lumen income but have separate kingdoms, and they can trade blueprints and Essences with each other. Wild tribes and map events are the NPC layer in every match.

### Co-op (2 players vs the AI ruler)

Two players defend and expand side by side against a stronger AI kingdom with extra starting Lumen. Good for learning the game together.

### The AI ruler (solo and co-op opponent)

The AI ruler uses exactly the same tools as a player, which makes it a real opponent and a good teacher:

- It watches your subjects (Interrogating any it captures), estimates your traits, and searches for fusions that counter them. Its search uses Foresee: it tries many candidate blends, Trials them, and picks the best counter.
- It uses the Mirror Drill against your recorded movements.
- After a match, it shows a short **War Report**: what it noticed about your army ("Your raiders were Cunning but never Patient"), what it built to counter you, and where you lost key subjects. This is how a player learns from it.
- Three personalities: **The Breeder** (fusion-heavy), **The Warden** (defensive, Dungeon-heavy) and **The Mad Archmage** (Mutation-heavy and chaotic).

### Campaign (solo, roguelite)

A map of 10–15 regions. Each region is a short match with a twist (a tribe-only island, a night-only realm, a map with no Lumen Heart). After each win you keep a few blueprints and Essences in a **Legacy Library** that carries into later regions. Losing ends the run, but Codex discoveries stay forever. The final region is the Mad Archmage's realm.

### Mind Lab (sandbox)

No opponent. Every base mind and every captured tribe brain is available, Lumen is unlimited, and you can fuse, train, mutate and Trial freely while watching the results in a test arena. This is where players hunt for Codex recipes, and it's the best mode for the hackathon demo.

## Controls, UI and visuals

The player gives goals, not step-by-step orders: zones, banners, targets and stances, all with mouse and keyboard, no typing.

### How you command subjects

| Command | How | What brains receive |
| --- | --- | --- |
| Work zone | Paint an area on the map and pick a job | Direction to the zone, job type |
| Rally banner | Place a banner; select a group to follow it | Direction and distance to the banner |
| Raid target | Click an enemy building or area | Direction to target, raid flag |
| Stance | Bold / Normal / Careful per group | Stance value (shifts risk-taking) |
| Royal Command | Hold R on one subject, steer with WASD for 8 seconds | Brain is overridden; costs Lumen |

Commands are inputs to the brain, not overrides. Disciplined, Loyal subjects follow them closely; Wanderers and Traitors drift. Choosing obedient minds for precise plans is part of the strategy.

### Key screens

- **Main view:** top-down map, resource bar, minimap, Lumen meter, day/night clock, alerts ("Raid at the north bridge").
- **Subject card:** name, body, Trial radar chart, trait badges, lineage tree (click any ancestor), history ("captured 2 Sprites, rescued 1 ally").
- **Fusion Altar panel:** two donor slots (three at Age II), ratio slider or triangle, body choice, Foresee button showing the predicted Trial Card side by side with the donors.
- **Training Grounds panel:** drill picker, a live mini-arena showing the subjects practicing, a learning curve, and a warning when a skill is being forgotten.
- **Mutation Pit panel:** six bubbles, each holding a mutant with its Trial Card; pick one. Instability meter on the side.
- **Dungeon panel:** list of captives with the five fates as buttons, escape-risk indicator.
- **Library and Codex:** grid of blueprints and Essences; the Codex as a book of discovered traits with recipes.

### Visual style

- 2D pixel art, 16 × 16 subjects on 16 px tiles, cozy dark-fantasy palette with glowing Lumen as the accent color.
- **Minds are visible as color:** each brain gets an aura color from its dominant axes (gold for Loyal, violet for Cunning, green for Diligent, red for Aggressive). Fused subjects show blended auras, so you can read an army's personality at a glance.
- Trait badges appear as small icons over a subject when selected; Legendary subjects get a permanent visual mark.
- Fusion is a short ritual animation: two subjects step into the Altar, light spirals, a new one walks out, and the Trial Card flips open.

## Technical architecture

Training of the eight base minds happens offline in Python; everything in the match (thinking, fusion, Trials, live training, mutation) runs inside Godot in C#, with the host machine as the single authority.

&#91;embedded content: system architecture · offline training, host, client\]

The Mind Engine and the game simulation talk every frame on the host; clients only send orders and draw what the host tells them.

### Offline: training the base minds (before the event)

1. Build a simple 2D grid environment in Python that mirrors the game's rules: movement, resources, tussles, carrying, storage. Keep it close to the Godot rules so brains transfer well.
2. Brain shape for every mind: about 32 inputs → 64 → 64 → outputs (9 move options + 9 actions). Roughly 7,000 weights. Tiny on purpose.
3. Train one **base brain** with a mixed reward (gather, survive, return home) using PPO (Stable-Baselines3 or CleanRL).
4. Fine-tune 8 copies into the specialists, each with its own reward, at a low learning rate so they stay close to the base. This closeness is what makes fusion work.
5. Train wild tribe brains the same way with unusual rewards.
6. Compute each specialist's Essence (specialist weights minus base weights).
7. Export everything as JSON or raw float arrays into the Godot project.

Before building the game, verify the key assumption: blend pairs at several ratios in Python and check that blends behave sensibly. If they don't, lower the fine-tune learning rate or shorten fine-tuning.

### In-game: the Mind Engine (Godot, C#)

- **Thinking:** each subject runs its network about 5 times per second, staggered across frames. 60 subjects per player is roughly 4 million multiply-adds per second, easy in C#. GDScript would struggle, so the brain code should be C#.
- **Fusion:** a weighted average of two or three weight arrays. Instant.
- **Essence infusion and removal:** add or subtract an Essence array times a strength. Instant.
- **The Trial:** a headless mini-simulation in C# with the same rules, no rendering, run at high speed. 12 scenarios × 3 runs in well under a second.
- **Training Grounds:** evolution strategies. Each step, try about 16 small random variations of the brain in the drill simulation, score them, and move the brain toward the better ones. Runs on a background thread, a few steps per second. Visible improvement within one or two minutes.
- **Mutation Pit:** add random noise to the weights 6 times, Trial each mutant.
- **Mirror Drill:** record enemy unit positions during the match; replay them as scripted opponents in the drill.
- **Naming unknown profiles (optional):** one call to a language model API with the 12 axis scores, cached by profile; offline fallback is a syllable-based name generator.

### Networking (LAN)

- Godot's built-in high-level multiplayer (ENet). One player hosts.
- **Host-authoritative:** all brains, simulation, fusion and training run on the host only. Clients send commands (zones, banners, Altar choices) and receive unit positions and states about 15 times per second.
- This avoids sync problems: no need for both machines to compute identical neural network results.
- Trial Cards and lineage data are sent to clients when they open a subject card.

### Data formats

- **Brain file:** id, architecture version, weights (float32 array), lineage (parent ids, ratios, drills, mutations), 12 axis scores, trait list.
- **Trait table:** a data file of recipes (axis conditions → trait name, tier, description), so designers can add traits without code.
- **Library and Codex:** saved locally as files, so discoveries persist between sessions.

## Hackathon build plan

This plan assumes a 48-hour event and a team of 3–4 people; the full game above is the vision, and the hackathon build is a focused slice of it that proves the AI is real.

First, check the event rules on pre-built work. Many hackathons allow pretrained models as assets but not pre-written game code. If pre-training is not allowed, train smaller brains during the first night.

### Scope tiers

**Must have (the demo fails without these):**

- [ ] 8 base brains trained and loading in Godot
- [ ] One small map: two home valleys, one Lumen Heart, one Wildwood with Grove Sprites and Stone Trolls
- [ ] Subjects acting from their brains: gather, deposit, tussle, subdue, carry, capture
- [ ] Fusion Altar with ratio slider and Foresee
- [ ] The Trial with 8 axes and a Trial Card with radar chart
- [ ] Trait table with about 40 traits
- [ ] Training Grounds with 2 drills (Harvest, Raid) and a visible learning curve
- [ ] Mind Lab mode

**Should have:**

- [ ] Dungeon with Interrogate and Extract
- [ ] Mutation Pit (6 mutants, Instability, Aberration escape)
- [ ] 1v1 over LAN, host-authoritative
- [ ] Ruler and Crown Capture win
- [ ] Lineage tree on the subject card

**Stretch:**

- [ ] Essences (Distill, Infusion, Removal)
- [ ] Basic AI ruler opponent
- [ ] Day/night with Night Owl behavior
- [ ] Language model naming for unknown profiles
- [ ] Mirror Drill

### Team roles

| Role | Owns |
| --- | --- |
| ML lead | Python environment, base and specialist training, fusion sanity checks, Essence math, export format |
| Engine programmer | C# Mind Engine: brain runtime, Trial simulator, evolution strategies, mutation |
| Gameplay programmer | Map, subjects, tussle/carry/capture, buildings, commands, LAN multiplayer |
| Designer / artist | Pixel art, UI panels, trait table, Trial scenarios, demo script and pitch |

With 3 people, the designer role is shared and art uses a free asset pack.

### Timeline (48 hours)

1. **Hours 0–6:** brain input/output format agreed by everyone; Python environment; base brain training starts; Godot project, map and subject movement.
2. **Hours 6–16:** specialists trained and exported; brain runtime in C#; subjects acting from real brains in the map; fusion check in Python.
3. **Hours 16–28:** Fusion Altar and Foresee; Trial simulator and Trial Card; trait table; tussle and capture.
4. **Hours 28–38:** Training Grounds with live learning; Dungeon; Mutation Pit; LAN.
5. **Hours 38–44:** Mind Lab polish, UI, bug fixing, balancing ratios that produce fun results.
6. **Hours 44–48:** feature freeze, record a backup video of the demo, rehearse the pitch.

### Demo script (about 3 minutes)

1. **Hook (20 s):** "Every subject in this kingdom has a real neural network. Watch what happens when we merge two of them."
2. **Fusion (40 s):** in Mind Lab, fuse a Thief and a Guard at 60/40. Foresee shows the Trial Card; commit; the new being walks out and visibly behaves as both: it sneaks, then escorts.
3. **Live learning (40 s):** put a clumsy fused unit in the Raid drill. The learning curve rises as judges watch it get better in the mini-arena.
4. **Mutation (30 s):** six mutants, pick the Legendary one. Push Instability; an Aberration escapes. Laugh.
5. **Versus (40 s):** a 1v1 LAN match: capture an enemy unit, Extract its brain, fuse it into your army.
6. **Close (10 s):** "Model merging, evolution and task vectors are research tools. We turned them into a kingdom."

## Risks and mitigations

The biggest risk is that fused brains behave badly; it must be tested in Python before any game code depends on it.

| Risk | What it looks like | Mitigation |
| --- | --- | --- |
| Blends are mostly broken | Most fusions come out Hollow | Fine-tune specialists at a low learning rate from one base; test blends early; limit ratios to the range that works; Foresee lets players avoid bad ones |
| Brains don't transfer to Godot | Behavior differs from training | Mirror the Godot rules closely in the Python environment; keep inputs normalized the same way in both |
| Traits are noisy | Same brain gets different traits each time | Run each Trial scenario 3 times and average; widen the Mid band |
| Live training is too slow | No visible progress in 2 minutes | Fewer variations per step, smaller drill arenas, start from a brain that is already half-good at the drill |
| Performance | Frame drops with many subjects | C# brain runtime, staggered thinking, 5 decisions per second, cap population |
| LAN problems on demo day | Connection fails | Host-authoritative design, test on the venue network early, keep a same-machine fallback and a recorded backup video |
| Scope creep | Nothing finished | Ship the Must-have tier first; Mind Lab alone makes a complete demo |
| Rule on pre-built work | Pretrained brains not allowed | Confirm with organizers; fallback plan to train during the first night |
| Language model unavailable | No internet at the venue | Built-in name generator; the feature is optional |

## Research angle and future

Fusion Kingdoms turns three research techniques into play and produces data no lab has: thousands of human-chosen brain merges with measured outcomes.

### The techniques it builds on

- **Model merging:** averaging the weights of models fine-tuned from the same starting point can combine their abilities (the "model soups" line of research).
- **Task vectors:** the difference between a fine-tuned model and its base captures a skill that can be added to or subtracted from other models ("task arithmetic").
- **Evolution strategies and neuroevolution:** improving a network by testing random variations and moving toward the better ones, without backpropagation.

These are mostly studied on large language and vision models. Fusion Kingdoms applies them to small game agents, live, with players choosing what to merge.

### What the game can teach researchers

- Which merges of behavior policies produce new behaviors that neither parent had.
- How well a cheap automated behavior test (the Trial) predicts real in-match behavior.
- How humans search a huge space of AI designs when the results are partly unpredictable, similar to how citizen-science games used players to explore protein shapes.

### Pitch to the judges, in one breath

"Every unit in our game is a real neural network. Players merge them, train them live, mutate them and steal them from each other, and an automated Trial verifies every new mind before it hits the field. It's a game, a behavior-testing tool and a research platform for model merging, all in one."

### Future expansions

- **Online ranked play** with a shared global Codex and "first discoverer" credits.
- **Brain trading market** between players.
- **Memory brains:** recurrent networks so subjects remember specific events, enemies and rescuers across a match.
- **Seasons:** new wild tribes and new Trial scenarios each season, shifting what traits are valuable.
- **Kingdom diplomacy in 4-player matches:** alliances, tribute and brain exchanges.
- **Mod support:** custom drills, trait tables and maps from the community.
