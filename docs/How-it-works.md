# How it works

## It is the explosions' smoke

[Volumetric Explosions](https://github.com/IshiakiZ/ksp-volumetric-explosions) already has smoke that is
somewhere: puffs in a patch of simulated air, with wind, eddies, the ground as it really lies, buildings
and ships to go round, put on a grid and drawn as one volume that the sun's light is followed through.
Since its version 0.4.0 that air takes smoke from other mods too. This mod is the one that says where
the rocket engines are and what each is putting out; everything the smoke then does is done there. That
mod's page [engines' smoke and flames](https://github.com/IshiakiZ/ksp-volumetric-explosions/wiki/Engines-smoke-and-flames)
has the whole of it; what follows is the short form.

## What an engine puts into the air

One source for each rocket engine: the middle of its nozzles' mouths, as wide as all of them together,
measured from the engine's own model. Every frame it says where that is, which way the jet goes, how the
ship is moving, how hard the engine is running, and what its smoke is like:

| What burns | How much smoke | How light | Hangs, in thick still air |
| --- | --- | --- | --- |
| solid fuel | 1 | white, a little grey in it | 16 s |
| kerosene | 0.24 | grey-brown | 9 s |
| hydrogen | 0.13 | white (it is steam) | 5 s |
| methane | 0.12 | pale grey | 5 s |
| the fuels that keep | 0.14 | a dark haze with brown in it | 6 s |
| monopropellant | 0.06 | white | 3 s |
| fuel heated by a reactor | none | | |

Which fuel each of the game's engines is taken as burning is the same choice the
[Engine Flames](https://github.com/IshiakiZ/ksp-engine-flames) mod makes, and the same config changes it
for both.

Since 0.2.0 (with Volumetric Explosions 0.5.0) each engine also says what ship it is on, and the smoke is
made in the jet itself, at the nozzle, in the patch of air that goes along with the ship and holds its
flames: hot, so that it shows nothing while it is in the flame, and coming to show as it cools, over the
flame's second half. So the column turns out of the flame where the flame dies and stays on it as the
rocket climbs. Since Volumetric Explosions 0.6.1 it is the air's from the first: it goes back past the
rocket at the rocket's own speed, widening by how far it has gone, and about one and a half flame lengths
back it gives way to the trail laid in the air the rocket leaves behind, the one going as the other comes
(see that mod's page on engines' smoke for how, and for what was wrong before: a row of balls behind a
fast rocket, and a dome on the flame high up). After that it spreads,
rises a little, and goes with the wind. It thins sooner behind a fast rocket, which tears it apart, and
sooner where the air is thin; in a vacuum there is none. (Before, the smoke began towards the end of the
flame in the trail's own patch, thrown on down the jet at thirty-five to a hundred and ten metres a second,
and seen close to its top came on in lumps and did not join the flame.)

## The pad

On a launch pad (0.2.0) the jet goes down through the grate in the pad's middle, and the smoke comes out at
the flame trench's two ends, low in the sides of the pad: fast, along the ground and a little up the ramp
each end is, rolling out and up on both sides, with the fire down in the trench lighting it. The trench
takes a moment to fill when the engines light and to empty when they stop, and the deck round the rocket
stays clear until the rocket is high enough for its jet to spread over the pad. (Volumetric Explosions
finds every pad's grate and trench ends from the game's own pad smoke; see its page on engines' smoke.)

Off the pad, where the jet would go into the ground the smoke begins on the ground instead, still being driven down:
the air turns that along the ground on every side, and the smoke rolls up from it. It begins half as
wide again there, rises faster, and off bare ground or water takes a share of dust or spray with it, in
the ground's own colour. (On the launch pad, the runway and the space centre's paving there is none to
take.)

While its smoke is drawn for an engine, the game's own smoke for that engine is hidden: every particle
effect on the part with "smoke" in its name. So is the haze of dust the game draws flat on the ground
under an engine, which would lie in the middle of ours. Both come back a moment after the engine stops,
or when the mod is switched off.

## A trail kilometres long

The smoke is laid along the way the nozzle went since the last frame, a puff for every so much of the
way, so that a rocket going at any speed leaves a trail and not a row of balls. Behind a fast rocket the
puffs are wider and further apart: the trail is that much wider by the time anyone is near enough to see
it.

A patch of air has a grid of the same number of cells however much it holds, so a trail is left in one
patch after another, each taking three and a half seconds of flight. Two things keep it fine where it
matters, near the rocket:

* **The grid of a trail lies along the trail.** A climbing rocket's smoke would cross a box that lay
  east, up and north cornerwise, in cells metres across. The box is turned to lie along the way the
  rocket is going, and its cells across the trail are as fine as the trail is narrow.
* **None of it is drawn ahead of the engine.** A grid is a frame or two old, and in that time a rocket
  doing three hundred metres a second has gone its own length. The smoke is laid a little way ahead of
  the engine, where it is about to be, and the shader is told every frame exactly where the engine is:
  ahead of that, nothing is drawn yet. So the head of the trail stays where the flame ends, however fast
  the rocket and whatever the frame rate.

The air holds nine patches at once, explosions' among them. When a trail needs one more, its far end is
let thin away over a couple of seconds.

## The light

The sun's light is followed through the smoke, so one side of the column is lit and the other is in its
own shade, and thin smoke in front of the sun shines. The sky lights it from above. And each engine is a
light of its own on its smoke: at night the head of the trail is lit by the flame it comes from, in that
flame's colour. Since Volumetric Explosions 0.5.0 the lamps round about (the floodlights, the pad's own
lights, buildings' lamps) light it too; where none reach, it is dark.

## What it costs

One flight each way of a Kerbal X (a Mainsail and six Swivels) from the pad, on an Apple M5 Pro at 1280 by
720 with Scatterer, EVE, TUFX, Waterfall and Natural Light installed, the game drawing as fast as it could:

| | Standing on the pad | Leaving it | Climbing, from the chase camera |
| --- | --- | --- | --- |
| Neither mod | 50 frames a second | 39 | 45 |
| Engine Flames | 49 | 42 | 49 |
| Rocket Smoke | 50 | 38 | 43 |
| Both | 49 | 39 | 46 |

So with liquid engines it is within what one flight differs from the next by. The smoke was 4,500 puffs
in four patches of air at the end of that climb, and the flames 3,400 bits of gas in one. The work is in
Volumetric Explosions, most of it on other threads than the game's own (see that mod's
[what it costs](https://github.com/IshiakiZ/ksp-volumetric-explosions/wiki/What-it-costs)). Not measured:
solid boosters, whose smoke is four times a liquid engine's and hangs twice as long; the camera inside the
smoke, where every pixel is a walk through it; a slower computer.

## What it does not do

* Smoke where Volumetric Explosions cannot draw a volume: there the mod says so and leaves the game's smoke
  as it is. (On Windows that mod's shaders are built for Direct3D, and the smoke was seen there on 2026-10-10, by
  day and by night, of liquid engines and of solid boosters; and on 2026-10-11, by day, the smoke as reworked in
  Volumetric Explosions 0.6: out of the trench's two ends, the column, its shadow.)
* Smoke in a vacuum, or from jets, ion engines and engines that burn something it does not know.
* Shadows of the ship or of buildings on the smoke.
* Nobody has tried it at a landing, on a plane, or with several rockets in the air at once.
