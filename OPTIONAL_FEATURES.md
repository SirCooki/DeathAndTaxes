# Death & Taxes — optional features

Optional parts of the mod: built or planned, outside the main body (the main tables, production, window and config).
Each entry has an outline from the user and its status. Test these later, when the mod is further along.

## Decaying trophy system
- Status: idea, not built.
- Outline: every hour a random trophy decays from each table, never the unique ones.

## No roof influences the speed
- Status: built in 0.3.1, default off, not tested yet.
- Outline: players can choose to turn off the requirement for a roof, with an associated speed penalty.
- Config: `1 - Production` → `No roof allowed` (default false) + `No roof penalty` (0..1, default 0.5;
  speed = 4 ^ (value - 1), so 0 = four times slower, 1 = no penalty).
