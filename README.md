# Prop Risk Manager for cTrader

Original native cTrader plugin implementing the supplied Risk Manager Pro-style workflow with independently written logic and public cTrader APIs.

> Status: **development / demo validation only**. The project is compiled and checked in CI, and pure prop-risk logic has unit tests. It has not yet completed hands-on cTrader Desktop smoke testing on the target broker/prop-firm account.

## Implemented

### Trade Execution

- BUY / SELL one-click execution
- Market / Limit / Stop auto-detection from entry price
- Five sizing modes: `% Equity`, `% Balance`, `% Free Margin`, `Fixed $`, `Fixed Lots`
- Commission-aware position sizing
- Live spread, configured commission, pip value, lot size and R:R
- Draggable Entry / SL / TP chart lines
- Shift+E sets entry to the current chart cursor price
- Max risk, max spread and max-lot pre-trade gates

### Advanced Protection

- Custom trailing stop
- cTrader server-side trailing stop option
- Automatic break-even trigger and offset
- Account-wide automation independent of active chart

### Position Management

- Current-symbol or all-symbol scope
- Close All / Close Profit / Close Loss with live P&L
- Cancel All / Buy / Sell pending orders
- Partial close by percentage
- Move selected scope to break-even

### Partial TP / SL

- Five Partial Take Profit levels
- Five Partial Stop Loss levels
- Triggers by pips or percentage of configured TP/SL distance
- Close by `% Original`, `% Remaining`, or `Fixed Lots`
- Per-position persisted trigger state
- Broker volume normalization
- Oversized/untradeable remainder handling
- No server request is sent when rounding would produce no volume change

### Prop Firm Guardian

- Initial-balance reference
- Profit target
- Daily profit cap / consistency display
- Daily drawdown
- Total drawdown
- Static or trailing DD references
- Configurable UTC reset offset and reset hour
- Account/daily equity high-water tracking
- Max-lot pre-trade restriction
- New-trade lock on target, daily cap or DD breach
- Auto-close all positions and cancel pending orders on hard DD breach
- Emergency liquidation retry throttling
- Worst-case pre-trade loss-room guard using:
  - existing open-position risk from current price to SL
  - existing pending-order entry-to-SL risk
  - proposed trade risk
  - configurable safety buffer
- Optional block when existing exposure has no stop loss
- Per-account persisted settings and runtime state

### Trading Statistics

- Today / 7d / 30d / 90d / All
- Current symbol / All symbols
- Total trades, W/L/BE, win rate, net profit, profit factor, expectancy
- Avg win/loss, approximate avg R:R, best/worst trade
- Max drawdown and recovery factor
- Streaks and long/short distribution
- Average duration and best/worst day
- Partial closes aggregated by PositionId so they do not inflate trade count
- Break-even positions excluded from win/loss classification

## Install a CI build

1. Open the latest successful GitHub Actions `build` run for the draft PR.
2. Download the `PropRiskManager-algo` artifact.
3. Extract `PropRiskManager.algo`.
4. Double-click the `.algo` file and open it with cTrader Windows or Mac.
5. Enable the plugin and locate its blocks in the Active Symbol Panel.

## Mandatory demo smoke test

Do not start with a funded account. Use a demo account and validate in this order:

1. **Startup / persistence**
   - Enable plugin.
   - Set distinctive risk, SL/TP and guardian values.
   - Restart cTrader/plugin and confirm values restore for the same account.
   - Switch accounts and confirm settings are isolated by account.

2. **Sizing**
   - Test all five sizing modes on FX, gold/index/CFD symbols used by the target prop account.
   - Independently verify calculated lot size against stop distance, pip value and commission.
   - Confirm volume obeys broker min/max/step.

3. **Execution**
   - Market BUY/SELL.
   - Buy/Sell Limit.
   - Buy/Sell Stop.
   - Confirm SL/TP arrive on the server at the intended distances.
   - Confirm max spread/risk/lot gates reject invalid orders.

4. **Chart interaction**
   - Drag Entry / SL / TP and confirm preview recalculates.
   - Move cursor and press Shift+E; confirm entry price/line moves correctly.
   - Switch charts and confirm the panel targets the active symbol.

5. **Position management**
   - Current vs All scope.
   - Close All / Profit / Loss.
   - Cancel all/buy/sell pending orders.
   - Partial close near minimum broker volume.
   - Move to break-even.

6. **Advanced protection**
   - Custom trailing only improves the stop.
   - Server trailing activates only with a valid SL.
   - Break-even triggers once threshold is reached.
   - Switch charts while a trade is open and confirm protection continues.

7. **Partial TP/SL**
   - Test all trigger and close-sizing modes.
   - Confirm each level fires once.
   - Restart plugin between levels and confirm fired state survives.
   - Confirm tiny/oversized final reductions do not create repeated no-op requests.

8. **Prop guardian**
   - Use deliberately small limits on demo.
   - Validate reset boundary using configured UTC offset/hour.
   - Validate static and trailing DD separately.
   - Confirm hard breach closes positions and cancels orders.
   - Confirm target/daily-cap locks new trades without emergency liquidation.
   - Confirm worst-case pre-trade guard includes other-symbol positions and pending orders.

9. **Statistics**
   - Compare a small hand-calculated history sample with panel output.
   - Include partial closes and break-even trades.

## Known limitations before funded use

- Prop-firm rule definitions vary. The generic guardian must be mapped to the exact current rules of the chosen firm/account type before relying on it.
- Daily-loss formulas can use different references (balance, equity, higher-of, fixed initial balance, realized/floating components). The current implementation primarily uses equity-based day-start/high-water references.
- Reset timezone uses a fixed UTC offset and hour. It does not automatically resolve DST rule changes for a named timezone.
- Commission uses the configured per-lot estimate for sizing/exposure. Exact broker-specific commission treatment must be verified on the target account.
- Automatic liquidation is a local desktop safety layer. Gaps, disconnections, broker rejection or process shutdown can prevent execution at the exact threshold.
- UI is functional but not yet the final single-window/collapsible visual match to the reference screenshots.

## Development gates

The draft PR should remain unmerged until:

- plugin build passes
- pure risk/state unit tests pass
- `.algo` artifact is produced
- cTrader Desktop demo smoke test passes
- exact prop-firm rule profile is reviewed and configured

See `docs/IMPLEMENTATION_PLAN.md` for the detailed roadmap.
