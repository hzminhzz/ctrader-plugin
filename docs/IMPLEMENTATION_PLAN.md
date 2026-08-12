# Prop Risk Manager implementation plan

Goal: build an original cTrader native plugin with feature parity to the supplied Risk Manager Pro workflow, using public cTrader APIs and independently implemented logic.

## Phase 1 - Foundation and domain model

- [x] Native .NET 6 cTrader Plugin project
- [x] TradePlan domain model
- [x] Five sizing modes: % Equity, % Balance, % Free Margin, Fixed Amount, Fixed Lots
- [x] Commission-aware position sizing
- [x] Shared pre-trade risk gate
- [ ] Per-account persisted settings/state
- [ ] Account-wide runtime service independent of active chart

Acceptance: all later modules consume the same TradePlan and account state contracts.

## Phase 2 - Smart trade execution and chart interaction

- [ ] Symbol Tab execution panel matching the supplied workflow
- [ ] Live symbol / spread / commission / pip-value display
- [ ] BUY / SELL one-click execution
- [ ] Optional manual entry price
- [ ] Auto-detect Market / Limit / Stop from direction + entry price
- [ ] SL and TP enable/disable controls
- [ ] Draggable Entry / SL / TP chart lines
- [ ] Live sizing and R:R preview
- [ ] Shift+E entry-price cursor hotkey
- [ ] Max-spread and max-risk pre-trade validation

Acceptance: a planned order produces the same sizing and order type regardless of whether values are changed in the panel or by dragging chart lines.

## Phase 3 - Advanced protection and position management

- [ ] Server-side trailing option
- [ ] Custom trailing engine
- [ ] Auto break-even trigger + offset
- [ ] Manage all open positions account-wide, not only active chart
- [ ] Current-symbol / all-symbol filter
- [ ] Close All / Profit / Loss with live P&L
- [ ] Cancel All / Buy / Sell pending orders
- [ ] Partial close
- [ ] Move selected scope to break-even

Acceptance: changing charts cannot stop protection for already-open positions.

## Phase 4 - Partial TP / SL automation

- [ ] Five configurable TP levels
- [ ] Five configurable SL levels
- [ ] Trigger unit: pips or % of original TP/SL distance
- [ ] Close sizing: % original, % remaining, fixed lots
- [ ] Idempotent trigger state per position
- [ ] Safe normalization for broker min/step volume
- [ ] Clean handling of oversized final levels and untradeable leftovers

Acceptance: each level fires at most once per position and never attempts an invalid broker volume.

## Phase 5 - Prop Firm Guardian

- [ ] Initial-balance reference
- [ ] Profit target
- [ ] Daily profit cap / consistency metric
- [ ] Daily drawdown limit
- [ ] Maximum drawdown limit
- [ ] Static and trailing variants
- [ ] Broker-time daily rollover
- [ ] Account-wide equity peak tracking
- [ ] Floating P&L, commission and swap-aware loss calculations
- [ ] Configurable safety buffer
- [ ] Pre-trade block when a proposed trade exceeds remaining loss room
- [ ] Auto-close all and cancel pending at configured breach boundary
- [ ] Persist day-start, peak, and automation state per account

Acceptance: guardian formulas are unit-tested against explicit prop-firm rule examples before live use.

## Phase 6 - Trading statistics and UI polish

- [ ] Today / 7d / 30d / 90d / All filters
- [ ] Symbol filter
- [ ] Total trades, W/L/BE, win rate, net profit, profit factor, expectancy
- [ ] Avg win/loss, avg R:R, best/worst trade
- [ ] Max drawdown, recovery factor
- [ ] Streaks, long/short distribution
- [ ] Average duration, long/short duration, best/worst day
- [ ] Theme-adaptive colors
- [ ] Collapsible sections
- [ ] Detachable window

Acceptance: stats are derived from cTrader history and match independently calculated test fixtures.

## Validation policy

1. Compile after each phase before adding the next.
2. Use demo accounts first.
3. Keep prop-firm limits separate from normal trade-risk limits.
4. Treat automatic close/block behavior as a local safety layer, not a broker/server guarantee.
5. Never depend on the active chart for account-wide protection state.
