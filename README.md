# Prop Risk Manager for cTrader

Native cTrader Desktop plugin for position management, prop-firm protection, partial exits, and the Smart Position Manager (SPM). Order entry and trade statistics use cTrader's built-in tools.

The plugin uses independently written C# logic and supported public cTrader APIs. It does not copy or decompile protected third-party code or assets.

## Status

- Builds as a cTrader `.algo` package in GitHub Actions.
- Pure risk/state test suite passes.
- Smart Position Manager dashboard is implemented inside the `PropRiskManager` plugin.
- DEMO validation completed for startup, dashboard rendering, restart recovery, external SL/TP reconciliation, plugin health, and cleanup.
- Never treat local desktop protection as a broker/server guarantee. Validate the exact prop-firm rules and broker behavior before funded use.

## Install

### Use a CI artifact

1. Open the latest successful GitHub Actions `build` run.
2. Download the `PropRiskManager-algo` artifact.
3. Extract `PropRiskManager.algo`.
4. Open the `.algo` file with cTrader Desktop.
5. Enable `PropRiskManager` in cTrader's plugin settings.
6. Open a chart, select the **Symbol** tab in the Active Symbol Panel, and scroll through the plugin blocks.
7. Expand **Smart Position Manager** for SPM controls.

SPM is not a separate plugin entry. It is a block rendered by `PropRiskManager` with the title **Smart Position Manager**.

### Build locally

```bash
dotnet restore src/PropRiskManager/Project/PropRiskManager.csproj
dotnet build src/PropRiskManager/Project/PropRiskManager.csproj -c Release
dotnet test tests/PropRiskManager.Tests/PropRiskManager.Tests.csproj -c Release --no-restore
```

The cTrader build workflow packages the compiler output as `PropRiskManager.algo`.

## Order entry

Use cTrader's built-in New order panel, chart trading controls, or Trade Watch to open and modify positions. PropRiskManager no longer renders a custom Trade Execution or Trading Statistics block.

After opening a position, use PropRiskManager for protection, guardian rules, position management, partial exits, and SPM enrollment.

## First-use safety checklist

Use a demo account first. Before opening a trade:

1. Confirm the cTrader account header says **Demo**.
2. Confirm the intended symbol, volume unit, stop loss, and take profit.
3. Confirm the plugin status is running and no runtime error is shown.
4. Set conservative drawdown limits, reset rules, and hard-breach cleanup settings.
5. Open the smallest broker-valid test position with cTrader's built-in order UI.
6. Verify the broker's actual position, SL, TP, and volume in Trade Watch.
7. Test restart recovery and external modifications before increasing size.
8. Close the test position and confirm zero positions and pending orders.

Do not use the plugin on a funded account until the target broker's symbol metadata, commission model, leverage, volume rules, prop-firm formulas, reset timezone, and liquidation policy have been independently verified.

## Main capabilities

- Advanced protection: custom/server trailing and break-even.
- Position management: symbol/all-symbol scope, close actions, pending-order cancellation, partial close, and move-to-break-even.
- Partial Take Profit and Partial Stop Loss: five levels each, percentage/points modes, persisted stages, and broker volume normalization.
- Prop Firm Guardian: profit target, daily cap, daily/total drawdown, high-water references, advisory rule status, and hard-breach cleanup.
- Smart Position Manager: explicit enrollment, account/symbol cards, smart alerts, alert history, management phases, percentage/points parameters, first/multi partial profit, profiles, reconciliation, and restart persistence.

## Documentation

Read the complete operator manual:

- [`docs/USER_GUIDE.md`](docs/USER_GUIDE.md)
- [`docs/IMPLEMENTATION_PLAN.md`](docs/IMPLEMENTATION_PLAN.md) for architecture and roadmap

## CI gates

The build workflow validates:

1. cTrader plugin restore/build;
2. pure MSTest risk/state suites;
3. `.algo` package generation and naming;
4. GitHub Actions artifact publication.

## Known limitations

- Prop-firm rules differ. Guardian settings must be mapped to the exact account contract.
- Daily-loss references and reset semantics differ between firms.
- Built-in cTrader order entry is not intercepted by PropRiskManager. Guardian block status is advisory for new entries; the plugin can only act on broker positions/orders after they exist, including configured hard-breach cleanup.
- UTC offset/hour configuration does not automatically model DST changes.
- Commission and sizing decisions belong to cTrader's built-in order workflow and must be checked against the target broker.
- Desktop automation cannot protect an account during process shutdown, disconnection, broker rejection, or machine failure.
- Smart financial actions require explicit position enrollment; monitoring and financial actions are separate concepts.
- Exact third-party SPM semantics not observable from public behavior are intentionally represented as documented PropRiskManager semantics, not claimed as proprietary parity.
