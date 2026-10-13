# E-006: A124 online-player race path

- Ghidra headless analysis and the SLES instruction stream identify an A124-only branch in `AIActionRace::Update` that follows an online player's extrapolated car.
- The reconstructed branch queries `IOnlinePlayer`, obtains its `ExtrapolatedCar`, checks the backing pointer at offset `0x1154`, selects the racing lane, and refreshes the path, position, and direction when the road navigator is not already finding a path.
- A124 `ISimable::GetOwner` uses virtual descriptor offsets `0x48/0x4c`. Reordering the six older entity/player methods after `GetOwner` under `EA_BUILD_A124` reproduces that slot without changing later platforms.
- A124 `IOnlinePlayer::GetExtrapolatedCar` uses virtual descriptor offsets `0x18/0x1c`. Placing it in the second declared interface slot reproduces those exact instructions.
- SLES rebuilt successfully. `AIActionRace::Update` increased from `82.872154%` before the reconstructed path to `85.97225%`; target size is `4036` bytes and reconstructed size is `4116` bytes.
- SLUS rebuilt successfully with the A124 path excluded. Its `AIActionRace::Update` result remains `99.03877%` with target size `3920` bytes, confirming the platform guard does not regress the later PS2 variant.
- The names and ordering of unobserved `IOnlinePlayer` methods remain provisional; only the `GetExtrapolatedCar` slot and the observed call behavior are binary-constrained here.
