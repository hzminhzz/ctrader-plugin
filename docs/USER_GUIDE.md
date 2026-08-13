# Prop Risk Manager User Guide

This guide explains how to install, configure, operate, test, and troubleshoot Prop Risk Manager in cTrader Desktop.

## 1. Operating model

Prop Risk Manager is a native cTrader plugin. It adds blocks to cTrader's Active Symbol Panel (ASP), normally under the **Symbol** tab. The plugin operates on the currently selected account and active chart symbol, while several protection services run account-wide.

The main blocks are:

- **Trade Execution** — order entry, sizing, preview, chart lines, and pre-trade gates.
- **Advanced Protection** — break-even and trailing protection.
- **Position Management** — close/cancel/partial-close actions.
- **Partial Take Profit** — up to five TP-triggered reductions.
- **Partial Stop Loss** — up to five SL-triggered reductions.
- **Prop Firm Guardian** — account rule tracking and emergency protection.
- **Trading Statistics** — history and performance analysis.
- **Smart Position Manager** — explicit enrollment, smart alerts, management parameters, and dashboard scopes.

SPM is part of `PropRiskManager`; do not look for a separate plugin named SPM.

## 2. Install and locate the plugin

### Install a package

1. Download `PropRiskManager.algo` from a successful CI artifact or release source.
2. Open it with cTrader Desktop.
3. Enable `PropRiskManager` in the plugin list.
4. Open a chart and select **Trade** if necessary.
5. Open the Active Symbol Panel and select **Symbol**.
6. Scroll the ASP vertically; plugin blocks can be below the initially visible blocks.
7. Expand **Smart Position Manager**.

If the plugin list contains `PropRiskManager` but not `Smart Position Manager`, installation is normally correct. SPM is a panel block, not a plugin registration.

### Confirm a healthy start

Expected state:

- cTrader account connection is authenticated;
- `PropRiskManager` is enabled and running;
- no crash indicator is present;
- the ASP contains the expected plugin blocks;
- the runtime log contains a start message and no exception.

If the plugin starts with a stale package, stop it, replace the `.algo` file, and start it again. Recheck the package timestamp/hash when testing a newly built artifact.

## 3. Trade Execution

### Sizing modes

Trade Execution supports:

- `% Equity`;
- `% Balance`;
- `% Free Margin`;
- `Fixed $`;
- `Fixed Lots`.

Risk sizing depends on entry-to-stop distance, symbol pip/lot metadata, commission estimate, and configured risk. Confirm the displayed volume is valid for the symbol's minimum, maximum, and step constraints.

### Order workflow

1. Select the chart symbol.
2. Configure side, entry, stop, target, sizing mode, and risk.
3. Review spread, pip value, lot size, commission, and R:R.
4. Check the chart Entry/SL/TP lines.
5. Submit the order only after the preview is correct.

The plugin can classify an order as market, limit, or stop from the relationship between requested entry and current price. Invalid risk, spread, lot, or max-lot conditions are rejected by the pre-trade gates.

### Chart controls

- Entry, SL, and TP lines can be dragged on the active chart.
- `Shift+E` sets entry to the chart cursor price.
- Switching the active chart changes symbol-scoped UI context; account-wide protection continues independently.

## 4. Advanced Protection

Advanced Protection can provide:

- custom trailing stop;
- cTrader server-side trailing;
- break-even trigger and offset;
- account-wide monitoring independent of the active chart.

Safety rules:

- protection should improve a stop, never loosen it through an automated action;
- server trailing requires a valid broker stop;
- a configured stop distance must respect broker minimum stop distance;
- verify the resulting broker SL in Trade Watch.

## 5. Position Management

Position Management provides explicit scope controls:

- current symbol or all symbols;
- Close All;
- Close Profit;
- Close Loss;
- Cancel All, Cancel Buy, and Cancel Sell pending orders;
- partial close by percentage;
- move the selected scope to break-even.

Review the symbol scope before using destructive actions. `Close All` is account-wide for the selected management scope; it is not equivalent to closing only the active chart symbol.

## 6. Partial Take Profit and Partial Stop Loss

Each panel supports up to five levels. A level can be configured by:

- points/pips distance; or
- percentage of the configured TP/SL distance.

Close sizing can use:

- percentage of original volume;
- percentage of remaining volume;
- fixed lots.

The plugin persists fired-level state by position. It normalizes requested reduction volume to broker constraints and avoids sending a broker request when normalization would produce no volume change. It also prevents a partial action from increasing exposure.

Recommended test:

1. Use a demo position at a volume large enough for the intended reduction.
2. Configure one level first.
3. Confirm the broker volume changes once.
4. Reconcile the position and restart the plugin.
5. Confirm the same level does not fire again.
6. Test the final small remainder separately; it may normalize to a no-op.

## 7. Prop Firm Guardian

Guardian tracks and displays:

- initial balance reference;
- profit target;
- daily profit cap and consistency;
- daily and total drawdown;
- static or trailing drawdown references;
- UTC reset offset and reset hour;
- account/equity high-water values;
- max-lot restrictions;
- new-trade locks;
- hard-breach emergency cleanup.

The worst-case pre-trade guard can account for:

- existing open-position risk from current price to SL;
- pending-order entry-to-SL risk;
- proposed trade risk;
- configured safety buffer;
- optionally, existing exposure without a stop.

### Configure conservatively

1. Set the exact initial-balance reference required by the firm.
2. Set target, cap, daily DD, total DD, and reset values.
3. Select static/trailing references according to the account contract.
4. Decide whether positions/orders without SL should be blocked.
5. Test each lock and breach on demo with small limits.

Guardian is a local desktop safety layer. It cannot guarantee liquidation during disconnection, process shutdown, broker rejection, or machine failure.

## 8. Smart Position Manager

### Dashboard layout

The SPM block contains:

- account open-position card;
- active-symbol P&L/position card;
- last-update card;
- armed smart-alert count;
- contextual symbol/account action card;
- active-symbol performance mini-series;
- position alert levels;
- monitor control;
- alert-detail and remove-alert controls;
- management-parameter editor.

The dashboard's account values are account-wide. P&L, performance, and symbol close behavior are active-symbol scoped. Alert and management state is position scoped.

### Explicit enrollment

SPM does not automatically manage every account position. To enroll eligible positions:

1. Select the chart symbol matching the positions.
2. Expand Smart Position Manager.
3. Configure management parameters.
4. Click **MONITOR [SYMBOL]** / **MONITOR POSITIONS**.
5. Confirm the status reports the number enrolled.
6. Confirm the smart-alert count and position alert levels.

Manual trades and positions created by other algos can be enrolled. Enrollment is the permission boundary; position labels are not the ownership rule after enrollment.

If the button reports `0 eligible`, either no open position exists for the active symbol or it is already managed. If the dashboard shows `1 managed` but `0 smart alerts`, alerts may be disarmed; use the management controls and monitor flow rather than assuming the position is untracked.

### Management modes

SPM supports **Points** and **Percentage** management modes. Configure:

- SL trail toggle;
- pre-break-even trail toggle;
- break-even toggle;
- post-break-even trail toggle;
- break-even trigger;
- break-even adjustment;
- pre-BE adjustment;
- post-BE adjustment;
- first partial profit;
- Multi-PP;
- partial spacing;
- partial adjustment/close percentage.

Use **Save Parameters** to save a profile and **Load Defaults** to resolve defaults. Profile scopes include symbol, asset class, and account where configured.

### Alerts

The ordinary alert types are:

- Partial Profit;
- Break Even;
- Stop Loss.

The displayed smart-alert total counts currently armed definitions, not historical events and not a fixed `3 × positions` formula. Alert events are persisted with position, symbol, side, trigger, observed price, timestamp, and outcome. Duplicate events are suppressed. Popup delivery originates from the same alert event recorded in history.

**SHOW ALERT DETAILS** displays the active-symbol history feed. **REMOVE ALERTS** disarms monitoring alerts without closing or changing the broker position.

### Monitoring and reconciliation

The broker position is the source of truth. SPM reconciles on position lifecycle events and on a lower-frequency safety pass. External SL, TP, and volume changes update current references while preserving original enrollment references where appropriate.

Financial actions are conservative:

- invalid or ambiguous state produces diagnostics instead of unsafe actions;
- stop changes are idempotent;
- partial volume is broker-normalized;
- a no-op normalized reduction is not recorded as a successful partial;
- closed positions leave active monitoring while history remains auditable.

### Restart behavior

SPM persists account runtime state, including original references, phase, alert definitions, fired partial stages, and history. After restarting cTrader/plugin:

1. Confirm the same account is selected.
2. Confirm the dashboard returns.
3. Confirm monitored positions and alert state are restored.
4. Confirm an already-triggered alert or partial does not replay.
5. Confirm external broker changes reconcile.

## 9. Trading Statistics

Statistics can filter by:

- Today, 7d, 30d, 90d, or All;
- current symbol or all symbols.

Displayed metrics include total trades, W/L/BE, win rate, net profit, profit factor, expectancy, average win/loss, approximate R:R, best/worst trade, max drawdown, recovery factor, streaks, long/short distribution, duration, and best/worst day.

Partial closes are aggregated by PositionId so they do not inflate trade count. Break-even outcomes are excluded from win/loss classification.

## 10. Persistence and account isolation

Settings and runtime state are persisted per account. When switching accounts:

- do not assume the prior account's guardian limits apply;
- verify the account number and balance reference;
- verify SPM profiles and monitored positions;
- verify the active chart symbol and broker constraints.

Test persistence by setting distinctive values, restarting cTrader, and confirming values restore only for the same account.

## 11. Controlled DEMO acceptance workflow

Use this order for integration testing:

1. Confirm DEMO account and flat starting state.
2. Confirm plugin running/not crashed.
3. Open the smallest valid position with a label/comment identifying the test.
4. Verify broker position, volume, spread, SL, and TP.
5. Enroll it in SPM.
6. Verify armed alerts and dashboard scopes.
7. Modify SL/TP/volume externally and verify reconciliation.
8. Test a single alert/partial stage; confirm one-shot behavior.
9. Restart cTrader/plugin and verify persistence/no duplicate action.
10. Test symbol close versus account-wide close only when multiple symbols are available.
11. Close all test positions and cancel pending orders.
12. Verify zero positions/orders and plugin healthy.
13. Review runtime logs for exceptions, rejected actions, and affected position/rule diagnostics.

MCP can assist with account reads, market data, order placement, amendments, closing, plugin start/stop, layout, and chart operations. MCP does not expose every embedded cTrader control, so SPM enrollment and visual interaction require the cTrader UI.

## 12. Troubleshooting

### SPM is missing from the plugin list

Expected: only `PropRiskManager` appears. SPM is embedded in that plugin. Open the Active Symbol Panel's Symbol tab and scroll to the **Smart Position Manager** block.

### Plugin starts but crashes or panels are blank

- Stop and restart the plugin.
- Confirm the artifact is the intended build.
- Check the runtime log.
- Confirm the plugin package includes the current source build.
- Rebuild with the documented Release command.

### Monitor button says zero eligible

- Confirm the active chart symbol matches the position symbol.
- Confirm a position is open.
- Confirm it is not already in managed state.
- Confirm management parameter fields contain valid values.

### An SL/TP change is not reflected

- Confirm the broker amendment succeeded in Trade Watch/MCP.
- Wait for the event or safety reconciliation pass.
- Check the symbol is available to the plugin.
- Review the runtime log for a diagnostic.

### Partial close repeats or does nothing

- Check broker minimum/step volume.
- Check whether the requested normalized reduction is zero.
- Confirm the prior action is reconciled before retrying.
- Review persisted fired-stage state.

### Guardian blocks a trade

Review target, cap, daily/total DD, max-lot, spread, stop-loss, existing exposure, and safety buffer. The block is intentional until the violated condition is corrected or the demo profile is adjusted.

## 13. Development and verification

Run:

```bash
dotnet test tests/PropRiskManager.Tests/PropRiskManager.Tests.csproj -c Release --no-restore
dotnet build src/PropRiskManager/Project/PropRiskManager.csproj -c Release
```

The pure domain seam is:

```text
SmartPositionEngine.Evaluate(positionSnapshot, settings, persistedState)
    -> actions + alerts + nextState
```

Keep this seam independent of cTrader runtime objects. Adapter code converts broker state into snapshots, executes validated actions, persists state, and logs diagnostics.

Before merging a change:

- run tests and Release build;
- inspect the generated `.algo` package;
- perform a DEMO smoke test when runtime/UI behavior changes;
- verify final DEMO flatness;
- never commit generated `bin/` or `obj/` output.
