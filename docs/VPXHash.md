# VPX File Integrity Hash (the `MAC` checksum)

This document captures the analysis behind supporting **in-place corrections** of
`.vpx` tables (for example, unchecking the depth-mask flag flagged by
`BallShadowDepthMaskRule`). The blocker is not the byte edit itself — it's the
integrity checksum VPX stores and validates on load. Read this before attempting
any write-back feature.

> Status: **analysis + experiments complete.** No correction/writer code exists
> yet, but experiments **and the VPinballX source** confirm the `MAC` mismatch is
> **not enforced on load** (Windows or Standalone) — see "Experiment log" below.

---

## TL;DR

- A `.vpx` file is an OLE2 / MS-CFB compound file. We already read it with
  **OpenMcdf** (`Vpx/VpxCompoundFile.cs`, `Platforms/Vpx/VpxPlatform.cs`).
- Flipping a setting (e.g. the `ZMSK` / "Hide parts behind" flag) is a trivial,
  fixed-width byte edit inside a `GameStg\GameItemN` stream.
- VPX stores an integrity digest in `GameStg\MAC` (**MD2**, seeded with a constant
  `TABLE_KEY`). It is computed/written on save, but on load a mismatch only sets
  an `hr` that the caller **ignores** — no warning, no block. On **VPX Standalone**
  the check is compiled out entirely.
- **Confirmed:** in-place edits with a stale `MAC` load cleanly, even on a
  **locked** table. **We do not need to recompute the hash** for correction
  features on current builds. The only hard-fail case is a *missing* `MAC` stream.

---

## How a `.vpx` file is laid out

`.vpx` is a Microsoft Compound File (OLE2 / MS-CFB). The relevant pieces:

- `GameStg` storage
  - `Version` — file format version (Int32). Selects which hashing "generation"
	VPX uses.
  - `MAC` — 16-byte **MD5** digest (the integrity checksum).
  - `GameData` — table-level BIFF records, including the `CODE` script record.
  - `GameItem0..N` — one stream per element (primitive, flipper, light, ...).
  - `Image0..N`, `Sound0..N`, `Font0..N`, `Collection0..N` — binary/asset streams.
- `TableInfo` storage — author metadata (TableName, AuthorName, TableVersion).

### BIFF record layout (what we already parse)

Each `GameItemN` stream is `[Int32 itemType][BIFF records...]`, and each BIFF
record is:

```
[Int32 size][4 ASCII tag chars][size-4 bytes of payload]
```

See `Vpx/BiffReader.cs`. The depth-mask flag we want to correct is the `ZMSK`
record (a 4-byte bool) inside a Primitive GameItem. Because it's fixed-width,
editing it does **not** change the stream length — no re-layout required.

---

## The checksum — what actually happens

When VPX saves a table it walks a **specific, ordered list of streams and BIFF
records**, feeding their bytes into an **incremental MD5**. Crucially it does
**not** hash the entire file — it hashes a curated subset in a precise order and
granularity, then stores the final digest in `GameStg\MAC`.

On load, VPX recomputes the digest and compares it to the stored `MAC`:

- Standard builds pop a **"table checksum failed / table has been modified"**
  warning.
- Depending on the build, it may then refuse to load or load in a
  degraded/read-only state.

So editing `ZMSK` in place **without** updating `MAC` will, at best, produce a
scary warning and, at worst, block loading.

---

## What a correct fix requires

To safely write corrections we must reproduce VPX's exact hashing, which means:

1. Enumerate the same streams **in the same order** VPX uses when saving.
2. Feed the same bytes into MD5 at the same **granularity** — VPX hashes
   per-BIFF-record for `GameData`/`GameItems`, and per-stream for binary blobs.
   A single byte off the expected order/granularity breaks the digest.
3. Handle the two hashing **generations** (a legacy hash and a newer one, keyed
   off `GameStg\Version`).
4. Recompute and overwrite `GameStg\MAC`.

### Authoritative reference

The VPinballX source is the source of truth: `pintable.cpp` — look for
`AddToDigest`, `HashWriteData`, and `MAC` handling. Any implementation must be
validated against real files.

---

## Feasibility assessment

- **Effort:** Moderate. The MCF read/write plumbing is easy (OpenMcdf supports
  writing; we currently open read-only). The hard part is faithfully porting the
  digest order/granularity and covering both hash generations.
- **Risk:** High if subtly wrong — a mismatched MAC either warns users or blocks
  the table load. Requires a strong validation harness before shipping.
- **Architecture fit:** A correction feature is **VPX-platform-specific** and must
  stay out of `Core/` (Core is neutral — no OpenMcdf, no VPX types). A natural
  home is a new `Platforms/Vpx/` writer service (e.g. `VpxCorrectionWriter`) plus
  a `VpxMacDigest` helper, mirroring how `VpxCompoundFile` / `VpxPlatform` isolate
  the OpenMcdf dependency today.

---

## Recommended approach (staged)

1. **Read-only validator first.** Recompute the MAC for an *unmodified* table and
   assert it equals the stored `GameStg\MAC`. If we can reproduce the digest for
   untouched files across a sample set, in-place correction becomes viable. If we
   can't match it exactly, correction isn't safe yet.
2. **Round-trip identity test.** Open a VPX-saved file, rewrite it untouched, and
   confirm our recomputed MAC matches the original.
3. **Then implement corrections**, gated behind explicit user opt-in.

### Safeguards for the eventual writer

- Always write to a copy / create a `.bak`; never edit in place on the first pass.
- After writing, reopen and verify the recomputed MAC equals the freshly stored
  MAC, and that the edited record (e.g. `ZMSK`) round-trips.
- Gate behind explicit user opt-in (destructive operation), consistent with the
  `EnabledByDefault = false` convention for opt-in integrations.

---

## Experiment log

### 2024 — In-place `TMIN` edit without recomputing `MAC`

**Setup:** `Domino (Gottlieb 1968).vpx` (unprotected table). Using a throwaway
OpenMcdf 3.2.0 console tool, opened the file read-write, located the
`BallShadowUpdate` timer in `GameStg\GameItem720` (itemType=2 = Timer), and
overwrote its `TMIN` BIFF record payload from **10 → -1** (fixed-width Int32, no
change in stream length). `GameStg\MAC` was **deliberately left stale** (not
recomputed).

**Result:** The table opened in VPX with **no warning, no error, no issue**, and
the editor showed the timer interval as **-1** as written. Two observations:

- OpenMcdf 3.x writes are **non-transacted** — calling `RootStorage.Commit()`
  throws `NotSupportedException: Cannot commit non-transacted storage`. The write
  still persists on stream/root disposal; just don't call `Commit()`.
- The stale `MAC` was **not enforced** on load for this unprotected table.

**Interpretation:** The `MAC` digest is tied to the legacy **table protection**
feature. VPX computes/writes it on save, but the load path only acts on a
mismatch when the table is actually protected. For ordinary unprotected tables
the checksum is effectively "there but not used." This makes an in-place
correction feature far cheaper than feared — we likely don't need to reproduce
the MD5 digest for the common case.

**Open questions before relying on this:**

- Does it hold across **multiple tables** and **VPX builds/versions** (Standalone
  vs Windows, older vs newer)?
- Is there any table where protection is enabled — how do we **detect** that so we
  can refuse (or handle) it? (Inspect for a protection flag / `MAC` enforcement
  signal in `GameStg`.)
- Does the standalone (VPX on Linux/Android) loader behave the same?
- Does re-saving in the VPX editor silently "bless" our edit (recompute `MAC`)?

**Note:** If confirmed broadly, the staged plan above (reproduce-the-hash first)
can be **downgraded** — a correction writer may only need to (a) edit the record
in place and (b) optionally detect+refuse protected tables, rather than port the
full digest algorithm.

### 2024 — Same edit on a *locked* table + VPX source confirmation

**Setup:** Enabled "lock table" in the VPX editor on the same table, saved, then
re-ran the tool to change `BallShadowUpdate`'s `TMIN` to **25** — again leaving
`GameStg\MAC` stale.

**Result:** The file wrote fine (note: "lock table" is a VPX *editor-side* flag,
not an OS/file lock, so OpenMcdf opens it read-write regardless) and the table
loaded with the timer at 25 — **the stale MAC was not enforced even for the
locked table.**

**Root cause (confirmed in VPinballX source `src/parts/pintable.cpp`):**

- On load, `LoadGameFromFilename` computes the integrity hash and, on mismatch,
  sets `hr = APPX_E_BLOCK_HASH_INVALID` (line ~1977). **But that `hr` is simply
  returned at the end of the function and the caller does not abort the load on
  it** — the table is fully constructed regardless. So a mismatched `MAC`
  produces no warning and no block. (The only hard failure path is if the `MAC`
  stream is entirely *missing* → `APPX_E_CORRUPT_CONTENT`.)
- The hash comparison is wrapped in `#ifndef __STANDALONE__`, so **VPX Standalone
  (Linux / macOS / Android) never checks the MAC at all.**
- Hash validation can also be turned off globally on Windows via the editor
  setting `Editor_DisableHash` (`hashValidation = !GetEditor_DisableHash()`).
- The digest algorithm itself: `CryptCreateHash(..., CALG_MD2, ...)` seeded with a
  14-byte `TABLE_KEY`, then BIFF records are fed in as they're read/written
  (`BiffReader`/`BiffWriter` take the `HCRYPTHASH`). So it's **MD2**, not MD5 as
  originally assumed, and keyed with a constant.
- The "lock table" flag and the old password protection are separate concerns
  (`SECB` record / `ProtectionData.flags`, e.g. `DISABLE_SCRIPT_EDITING`); they
  gate *editor* behavior, not MAC enforcement on load.

**Conclusion:** For current VPX (Windows *and* Standalone), an in-place BIFF edit
with a stale `MAC` loads cleanly. **We do not need to recompute the hash** to make
correction features work on today's builds. Recomputing the MD2 digest (over the
exact record order) remains a "nice to have" for cleanliness/future-proofing, but
is **not** a blocker. The lock flag does not change this.

Additional confirmation: verified clean load of the edited table in **VPX 10.8.1
(BGFX)** as well — no warning, no block.

---

## Forward-compatibility risk & mitigations

**The risk:** today the mismatch `hr` is ignored, but a *future* VPX build could
promote it to a hard load failure. Tables we edited in place (stale `MAC`) would
then break. We want an approach that yields a **genuinely valid** file, not one
that merely depends on the check staying lax.

**Ship now (safe on all current builds):** in-place BIFF edits, always writing a
`.bak` first and **never removing the `MAC` stream** (a *missing* MAC is the one
hard-fail path → `APPX_E_CORRUPT_CONTENT`).

### Mitigation A — Recompute the MD2 digest ourselves (preferred durable fix)

Port the algorithm from `pintable.cpp`: `CALG_MD2` seeded with the 14-byte
`TABLE_KEY`, feed the BIFF records in the exact save order (`BiffWriter` hands each
record's bytes to the hash), then overwrite `GameStg\MAC`.

- **Pros:** self-contained, offline, no VPX install required (works for
  Standalone-only setups), unit-testable.
- **Cons:** must replicate the record ordering/granularity byte-for-byte.
- **Validation gate:** recompute the digest for an *untouched* table and confirm
  it equals the stored `MAC` before trusting the writer. Round-trip identity test.

### Mitigation B — Automate load/save via VPX itself (fallback / "bless")

VPX's `SaveInfo`/`Save` write a fresh `MAC` on every save, so driving
`VPinballX.exe` to load → save → exit rewrites a correct hash and re-serializes
canonically.

- **Pros:** guaranteed-correct, VPX-canonical output; no algorithm to maintain.
- **Cons:** requires a VPX install/path (we already track `_vpxExeBox` in
  `MainForm`); VPX has no clean headless "resave & exit" switch historically, so
  this likely needs brittle scripted UI automation. Prototype before committing.

### Recommendation

1. Implement in-place edits now (`.bak`, preserve `MAC` stream).
2. Add **Mitigation A** (MD2 recompute) as the durable hedge against a future
   strict build — primary path once validated.
3. Keep **Mitigation B** as an optional user-triggered "resave in VPX" step for
   those who have VPX installed and want a fully canonical file.

---

## Related code

- `Vpx/VpxCompoundFile.cs` — OpenMcdf read plumbing (script, table info).
- `Platforms/Vpx/VpxPlatform.cs` — loads `GameItem`/`Image` streams into elements.
- `Vpx/BiffReader.cs` — BIFF record parser (`[size][tag][data]`).
- `Platforms/Vpx/Model/VpxGameItem.cs` — parses `ZMSK` into `HidePartsBehindKey`.
- `Platforms/Vpx/Rules/BallShadowDepthMaskRule.cs` — the rule that flags `ZMSK`.
