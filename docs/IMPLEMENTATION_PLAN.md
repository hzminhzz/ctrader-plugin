# Prop Risk Manager implementation plan

Goal: build an original cTrader native plugin with feature parity to the supplied Risk Manager Pro workflow, using public cTrader APIs and independently implemented logic.

## Phase 1 - Foundation and domain model

- [x] Native .NET 6 cTrader Plugin project
- [x] Per-account persisted settings/state
- [x] Account-wide runtime service independent of active chart

Acceptance: later modules consume account state and broker position contracts without duplicating cTrader order entry.

## Phase 2 - Built-in cTrader order entry

Order entry is intentionally delegated to cTrader's built-in New order, chart trading, and Trade Watch controls. PropRiskManager consumes broker positions after entry and does not duplicate cTrader's execution or sizing workflow.

## Phase 3 - Advanced protection and position management

- [x] Server-side trailing option
- [x] Custom trailing engine
- [x] Auto break-even trigger + offset
- [x] Manage all open positions account-wide, not only active chart
- [x] Current-symbol / all-symbol filter
- [x] Close All / Profit / Loss with live P&L
- [x] Cancel All / Buy / Sell pending orders
- [x] Partial close
- [x] Move selected scope to break-even

Acceptance: changing charts cannot stop protection for already-open positions.

## Phase 4 - Partial TP / SL automation

- [x] Five configurable TP levels
- [x] Five configurable SL levels
- [x] Trigger unit: pips or % of original TP/SL distance
- [x] Close sizing: % original, % remaining, fixed lots
- [x] Idempotent trigger state per position
- [x] Safe normalization for broker min/step volume
- [x] Clean handling of oversized final levels and untradeable leftovers

Acceptance: each level fires at most once per position and never attempts an invalid broker volume.

## Phase 5 - Prop Firm Guardian

- [x] Initial-balance reference
- [x] Profit target
- [x] Daily profit cap / consistency metric
- [x] Daily drawdown limit
- [x] Maximum drawdown limit
- [x] Static and trailing variants
- [x] Configurable reset timezone offset and reset hour
- [x] Account-wide equity peak tracking
- [x] Real-time equity-based drawdown monitoring including floating account P&L
- [ ] Firm-specific commission/swap/reference-balance formula modes
- [x] Configurable safety buffer before the formal breach line
- [x] Pre-trade worst-case block based on remaining daily/total loss room
- [x] Account-wide existing risk-to-SL calculation for open positions and pending orders
- [x] Optional new-trade block when existing exposure has no stop loss
- [x] Auto-close all and cancel pending at configured drawdown breach boundary
- [x] Emergency liquidation retry throttling
- [x] Persist day-start, peak, and automation state per account
- [x] Pure guardian/state unit-test suite

Acceptance: guardian formulas are unit-tested against explicit prop-firm rule examples before live use. Exact funded-account use still requires mapping the selected firm's current rule definitions to the configurable engine.

## Phase 6 - UI polish

- [x] Native cTrader controls inherit the active light/dark theme
- [ ] Final semantic color/theme pass
- [ ] Single-panel collapsible-section polish matching screenshots
- [ ] Detachable floating window

## CI / packaging

- [x] Restore and build plugin on every pull request
- [x] Run pure risk/state MSTest suite
- [x] Produce `PropRiskManager.algo`
- [x] Upload `PropRiskManager-algo` build artifact

Build run 119 is the first fully green build/test/package pipeline.

## Validation policy

1. Compile after each phase before adding the next.
2. Run pure risk/state unit tests in CI.
3. Use demo accounts first for built-in cTrader trading and plugin UI validation.
4. Keep prop-firm limits separate from normal trade-risk limits.
5. Treat automatic close/block behavior as a local safety layer, not a broker/server guarantee.
6. Never depend on the active chart for account-wide protection state.
7. Do not merge the draft PR until cTrader Desktop smoke tests pass.
