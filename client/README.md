# Client

Angular 21 SPA for [AuctionSite](../README.md). See the root README for first-run setup, including
the local HTTPS certificate this dev server requires.

## Development server

```bash
npm start
```

The dev server runs over HTTPS at `https://localhost:4200/` and reloads on source changes. It
expects the API on `https://localhost:5001` (`src/environments/environment.ts`).

## Building

```bash
npm run build -- --configuration production
```

Build artifacts land in `dist/`. This is what CI runs.

## Running unit tests

Unit tests run on [Vitest](https://vitest.dev/):

```bash
npm test -- --watch=false
```

There are no end-to-end tests; the manual walkthroughs in [../TESTING.md](../TESTING.md) and
[../docs/winner-email-testing.md](../docs/winner-email-testing.md) cover the cross-system paths
instead.

Generated with [Angular CLI](https://github.com/angular/angular-cli) 21.1.3; see the
[CLI command reference](https://angular.dev/tools/cli) for the rest of its commands.
