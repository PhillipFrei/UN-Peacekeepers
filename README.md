# UN Peacekeepers – 0.2.3 alpha

Stabilization only, based on the 0.2.2 local package. The source tree does not include the PNG/WAV assets; the full distribution is the separate ZIP. Build workflow produces the authorization DLL. Existing defNames and package ID unchanged.

- Standalone UN faction: no Outlander parent, no inherited pirate caravan trader or mod xenotypes/extensions. Explicit stock vanilla trader types and raid curves; direct patches by other mods can still affect these defs.
- New game default neutral goodwill becomes 0–10 only for UN/player. Existing relations retained. A newly inserted faction in an existing save may start at vanilla 0.
- Fixed leader kind; retry vanilla leader generation for missing UN leader only.
- Resistance 15–24, will 3–5. UN combat weights retained from 0.2.2 (human 180, combatPower 70; mechs unchanged). Damage 32, armor penetration 300%, helmet +3; shield unchanged.
- Authorization: 900,000 consecutive ticks at +100, persisted in save; normal research cannot grant authorization. After grant remains permanent. Scoped recipe and bill gates. Existing save progress preserved if present.
- In-game integration tests still required, especially on a modded save.
