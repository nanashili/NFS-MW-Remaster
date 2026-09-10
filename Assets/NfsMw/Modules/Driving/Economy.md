# Economy settlement implementation

## Status

Implemented: compiled policy/catalog snapshots, durable activity registration,
checked integer payout calculations, first/repeat rewards, reputation, pursuit
bounty banking/forfeiture, capped fines, unlocks, configured vehicle grants,
atomic catalog purchases, itemized receipts, duplicate/conflicting-ID handling,
profile v1-to-v2 migration, ledger reconciliation and bounded seeded simulation.

**Not yet end-to-end in the existing game scenes.** Legacy race payouts, shop
checkout and bounty banking have not been replaced. Do not bind both systems to
the same save slot. Blacklist authority, milestone banking, marker choices,
reservations, impound consequences, mechanical repairs, results-screen binding,
full cohort/Blacklist balance and Windows validation remain open.

## Authoring

Use **Assets > Create > Driving > Career > Economy Definition**. Author policies,
items and optional simulation scenarios. Select **Run configured economy scenarios**
in the Inspector for income/spending/fine totals, wallet timeline and unacquired
vehicles. This uses in-memory storage, never the player's save directory.

All prices are catalog-owned. `positionCash[0]` is first place; difficulty modifiers
are basis points (10000 = 100%). The difficulty index is captured at activity start.
For repeat/non-winning payouts, apply the repeat ratio first, then difficulty,
rounding down after each integral multiplication/division. Winning bonuses follow.
First victory is distinct per event ID; changing a policy does not reset that claim.
No cooldown, daily restriction or automatic bounty-to-cash conversion is applied.

Fine = min(heat/infraction assessment, maximumFine, wallet percentage,
cash above walletFloor). A wallet below the floor remains unchanged, not topped up.
Cost-to-State is not a repair bill. Cosmetic damage incurs no charge.

Upgrade/customization purchases require a known compatible target vehicle but
ownership currently follows the existing global product inventory. They do not
install parts. Vehicle grants preserve the configured performance/customization
IDs. Granting a vehicle does not implicitly unlock its dealership listing.

## Integration contract

`EconomySettlement(initialProfile, definition, storage)` takes a detached full-profile
snapshot and the existing `ICareerProfileStorage` seam. It must exclusively own
that slot while active. It is a main-thread module, not a multi-writer database.
Call `TryBegin` before play, with a fresh stable activity ID, event, vehicle, policy,
difficulty and start timestamp. Registration persists the frozen policy. The event
controller must retain that ID across retries of the same delivery.

Call `TrySettle` with an authoritative terminal outcome. Consumers must not submit
arbitrary player-provided win/bounty claims. Abort/restart/invalid outcomes create
terminal zero-reward receipts. A second different outcome for that ID is rejected.
Restarting actual play requires a new registered activity. Call `TryPurchase` with
a stable command ID and catalog item ID, never a client-supplied price.

On success, present the returned receipt. It includes operation/outcome, source,
vehicle, profile, timestamp, sequence, previous/new balances and itemized reasons.
Do not credit on animation completion. Returned receipts and snapshots are copies;
mutating them does not mutate authoritative state.

Each successful operation publishes one whole-profile snapshot through storage;
only afterwards does the engine change its live state. False from storage must
mean definitely not committed. If storage throws, the engine tries to reconcile
the exact candidate by reading it back. Otherwise it freezes with `RequiresReload`;
reload from storage before retrying, never from the stale in-memory snapshot.
Do not retry uncertain outcomes with new IDs.

An integration host must switch all legacy writers together and publish the
committed snapshot to wallet, garage, bounty and career consumers. The existing
`CareerProfileSystem` is not that host yet. Its periodic captures must not overwrite
the engine's snapshot. No automatic scene mutation is supplied here.

## Saves and limits

Profile version 2 adds `economy`. Version 1 imports opening cash and existing
completion IDs solely to suppress retroactive first-win rewards. No reputation,
unlock or rival victory is silently granted. Older game builds will reject v2.

The ledger reconciles cash, reputation and bounty on load and rejects conflicting
or malformed chains. Historical item IDs must remain in the catalog or be explicitly
migrated; missing references fail with a diagnostic. This is corruption detection,
not cryptographic protection from local save editing.

Money calculations use checked Int64 intermediates; existing wallet/profile cash
and bounty have an explicit Int32 maximum. Oversized settlements fail atomically.
Reputation uses Int64. Full 64-bit wallet consumer migration is not implemented.
The ledger is not pruned: blindly dropping receipts would permit duplicate claims.
Long-career compaction requires a retained claim index and snapshot checkpoint.

## Simulation and verification

Scenarios configure ordered event IDs, win/bust chances, pursuit exposure, duration,
starting funds and desired vehicles. The simulator invokes the actual engine,
reports exhaustion rather than looping forever, and caps configured race attempts
at 1000. Unsuccessful synthetic race attempts currently abort without placement
payout. Desired vehicles are attempted in authored order after a win. These are
explicit synthetic policies, not measured human archetypes or a full career solver.

`EconomySettlementTests` exercises first/repeat, reload duplicates, conflicting
outcomes, failed storage, lost acknowledgements, locked/insufficient/incompatible
purchases, authored grants, fines, bounty, overflow, detached receipts, migration,
ledger reconciliation and reproducible simulations. A real local-file test checks
save/reload receipt persistence with the project's atomic file adapter.

These checks do not prove power-loss behavior, Windows filesystem behavior,
scene integration, economic recovery after impound, or full #15-to-#1 balance.
See `EconomyDesign.md` for the research and rollout gates.
