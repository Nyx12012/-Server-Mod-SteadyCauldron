# SteadyCauldron

A server mod for A Township Tale that stops cauldrons from tipping over. Set one down and it
stands up straight on the ground and stays there, so a bump or a wobble can't spill your stew.
Pick it up with both hands and it comes free like normal.

Players don't need to install anything.

Idea and concept art by Hazy (@AnAdventures).

## How it works

- Let go of a cauldron within 1 m of the ground and it snaps down onto it, standing upright.
- One hand on it won't move it. Grab it with both hands to lift it.
- Throw it and it flies like normal, then settles where it stops.
- A cauldron already lying on its side gets stood back up the next time someone is near it.
- It stays put through a server restart.

## Install

1. Put `SteadyCauldron.dll` in your server's `Mods` folder.
2. Restart the server.

Needs MelonLoader, which TheModdingTavern launcher installs. Server only, players need nothing.

To remove it, delete `SteadyCauldron.dll` from `Mods`. Cauldrons it already set down stay put
until someone picks them up, then they act normal again.

## Settings

The first start writes `UserData\SteadyCauldron.txt`:

```
items = 45082
reach = 1
throw = 4
upright = on
log = on
```

- `items`: the item hashes to keep steady. 45082 is the Cauldron Medium.
- `reach`: how high above the ground, in metres, you can let go and still have it snap down.
- `throw`: let go faster than this, in metres a second, and it counts as a throw.
- `upright`: stand it up straight when it snaps. Off keeps whatever angle it landed at.
- `log`: write a line to the server log each time one snaps.

Change a value and save. The server picks it up in a couple of seconds, no restart.

## Good to know

- There's no outline when you set it down like there is for planting. That outline is drawn by
  the player's own game, and a server mod can't add one.
- Right after you let go, your game takes about a second to catch up with the server, so the
  cauldron can wobble a little before it pops upright.
- Nothing in the game is patched. The mod turns the cauldron's physics off once it's down, the
  same way the game holds planted crops still.

## Build

`build.ps1` compiles `SteadyCauldron.cs` against your game install (set `$game` at the top)
with the .NET Framework's `csc`, and writes `build\SteadyCauldron.dll`.

## Licence

MIT. See `LICENSE`.
