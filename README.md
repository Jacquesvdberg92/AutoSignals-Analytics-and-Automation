# AutoSignals

AutoSignals is a polished hobby project that tracks public and private Telegram crypto signal groups, measures signal-level performance, and provides tools for automated and simulated order execution. Built with Razor Pages on .NET 8, the app blends practical engineering with an experimental, research-first mindset — suitable for developers, traders, and data enthusiasts who want transparent signal analytics and flexible automation hooks.

Live demo: https://autosignals.xyz

## Highlights

- Source-first, research-oriented hobby project focused on transparency and analytics.
- Tracks signal-level accuracy: entry, exit, stop loss, ROI and timing across multiple signal providers.
- Multi-exchange connector scaffolding (Binance, Bybit, Bitget, OKX, KuCoin) for live or simulated execution.
- Notification and distribution via a Telegram bot and optional email alerts.
- Admin UI with file uploads, phone input, image gallery and enhanced media utilities.
- Built-in telemetry: page view analytics and performance counters for feature usage.
- Lightweight security features: reCAPTCHA for public forms and application-level encryption for sensitive settings.

## Who this is for

This project is a personal/hobby effort aimed at developers and traders who want a transparent view into signal provider performance and an experimental platform for automation. It is NOT financial advice and is intended for learning, testing, and prototyping.

## Quick start

1. Prerequisites: .NET 8 SDK and a supported database (SQL Server by default).
2. Copy the sample configuration:
   - `appsettings - Sample.json` contains placeholder settings. Create `appsettings.json` from the sample and fill in connection strings and API keys (or use user-secrets / environment variables for secrets).
3. Restore and run:
   - `dotnet restore`
   - `dotnet run`
4. Open the site in your browser and sign in using the Identity pages (if enabled).

## Configuration

- Keep sensitive keys out of source control. Use `dotnet user-secrets` or environment variables for production credentials.
- The project reads configuration from `appsettings.json`; copy values from `appsettings - Sample.json` and replace placeholders.

## Development notes

- The UI is server-rendered Razor Pages. Look in the `Views` folder for landing pages, terms, and admin layouts.
- Background services that poll exchanges or parse Telegram messages should be run as hosted services — see `Program.cs` for service registrations.
- Tests: this repository currently has no formal test suite; contributions that add tests are welcome.

## Contributing

This is a hobby project. Contributions are welcome but expect a lightweight review process. Open issues and PRs that improve documentation, add tests, or fix bugs are appreciated.

## Important legal note

AutoSignals is a hobby/research project and not investment advice. Use at your own risk. Review and secure any credentials before connecting real exchange accounts.

