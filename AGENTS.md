# AGENTS.md

Instructions for anyone, person or agent, changing the code in this repository. This is the
canonical instructions file; tool-specific files such as `CLAUDE.md` point here. Contributions
follow [CONTRIBUTING.md](CONTRIBUTING.md): ideas and issues are welcome from anyone, and pull
requests are issue-first and invitation-only.

## What this is

`Umbraco.Community.SearchManager` is an Umbraco 17/18 backoffice search index manager and a drop-in
replacement for the Examine Management dashboard: every index at a glance, queued bulk rebuilds with
history, and click-to-filter search over core Examine and Umbraco Search indexes. It ships three
NuGet packages per Umbraco major: the package, `Umbraco.Community.SearchManager.Core` with the
contracts and shared logic, and the `Umbraco.Community.SearchManager.UmbracoSearch` adapter for
Umbraco Search.

Some code comments and ADRs cite "BRIEF §n" or "the brief". That is the maintainer's private build
brief; every decision a contributor needs is recorded in `docs/adr/`.

## Working rules

- Verify Umbraco, Examine and Umbraco Search APIs against the installed version before use; never
  invent aliases, routes, element names or members. When docs and the installed package disagree,
  the package wins.
- Record decisions in `docs/adr/` (see its README): a change to a contract, a dependency, a
  supported version or something an earlier ADR decided gets a new ADR.
- Organise by feature folder, not technical layer.
- The Core project must not reference Umbraco packages; a Core.Tests test enforces it. The main
  package must not reference Umbraco Search; only the adapter package does, because Umbraco Search
  brings Examine 4 and would upgrade every site.
- Tooling: Bun, Vite, Vitest and Prettier for the client; xUnit v3, NSubstitute and CSharpier for
  .NET.
- One package per Umbraco major: `dotnet build` targets 17, `-p:UmbracoMajor=18` targets 18.
  Major-specific code goes in paired `*.V17.cs` / `*.V18.cs` files. Build and test both majors
  before committing anything that touches Umbraco APIs.
- Run the tests before every commit and read the build summary. Stop any running sample site first:
  a locked DLL shows up as build errors.
- C# members carry XML doc comments, exported TypeScript carries TSDoc, and new or changed code
  comes with tests (`MethodName_Scenario_ExpectedBehaviour` names in .NET).
- Conventional Commits, ASCII only.
- Every user-visible change adds a line under `## [Unreleased]` in `CHANGELOG.md` and updates the
  `docs/` page that describes it (CONTRIBUTING.md has the docs style guide).
- Branches: `main` plus one short-lived branch per pull request. Both majors build from `main`.

## Commands

```sh
# .NET: format check, build and test for Umbraco 17; add -p:UmbracoMajor=18 for 18
dotnet tool restore && dotnet csharpier check .
dotnet build -warnaserror
dotnet test --solution Umbraco.Community.SearchManager.slnx

# Client, in src/Umbraco.Community.SearchManager/Client
bun install
bun run format:check && bun run typecheck && bun run test && bun run build
bun run e2e               # Playwright against all four sample sites; E2E_SITES=17 for one major
bun run docs:screenshots  # retake docs/images/ from a running SearchManager.SearchSite17
bun run generate          # regenerate src/api/ from the running SearchManager.Site17

# Sample sites; build the client first, because its output is gitignored
dotnet run --project samples/SearchManager.Site17                            # https://localhost:44380/umbraco
dotnet run --project samples/SearchManager.Site18 -p:UmbracoMajor=18         # https://localhost:44381/umbraco
dotnet run --project samples/SearchManager.SearchSite17                      # https://localhost:44382/umbraco
dotnet run --project samples/SearchManager.SearchSite18 -p:UmbracoMajor=18   # https://localhost:44383/umbraco

# Packed-package smoke test, after dotnet pack -o artifacts
uv run tests/package-smoke/smoke_nupkg.py --packages artifacts --major 17
```

The `SearchSite` samples add Umbraco Search and the adapter. The sample sites install themselves on
first run (SQLite), seed about 200 content items and a custom `ProductIndex`, and sign in with
`admin@example.com` / `1234567890` (development only, set in `appsettings.Development.json`). The
same file sets a fixed, development-only `Umbraco:CMS:Imaging:HMACSecretKey`, because without one
the unattended install writes a generated key into `appsettings.json`; never commit a key there. Each
runs on one Umbraco major: a solution build for the other major skips it, and building or running
it on its own for the other major fails with the switch to use. The e2e suite starts them on ports
44390 to 44393.

To release, move the `## [Unreleased]` entries under `## [17.x.y] and [18.x.y] - <date>` in the
commit you tag, then push the tags `v17.x.y` and `v18.x.y`. `release.yml` runs the security gate,
tests, packs all three packages, smoke-tests them with and without Umbraco Search, publishes to
nuget.org and creates the GitHub Release from that CHANGELOG section. A suffix (`v17.1.0-beta.1`)
makes a pre-release.

## Backoffice client facts

- Source in `src/Umbraco.Community.SearchManager/Client/src/`, one folder per feature: `indexes`,
  `rebuilds`, `details`, `explore`, `diagnose`, `node-action`, `umbraco-search`, `settings` and
  `workspace`. Shared code is in `shared/`, entry points in `entrypoints/`, localisation in `lang/`.
  Each feature folder contributes its `manifests` to `bundle.manifests.ts`.
- Vite builds it into `wwwroot/App_Plugins/UmbracoCommunitySearchManager/`. The manifest
  `public/umbraco-package.json` has the id `Umbraco.Community.SearchManager`; the build stamps its
  version from `SEARCH_MANAGER_VERSION`, which `dotnet pack` sets.
- The views live in one workspace (`workspace/`, alias `Umbraco.Community.SearchManager.Workspace`,
  entity type `search-manager`). Shared state belongs in a workspace context, not in chains of
  properties; the Explore and Diagnose views keep their view state in the URL
  (`explore/explore-state.ts`, `diagnose/diagnose-state.ts`) so a link reopens it. Components talk
  through typed custom events with `bubbles: true` and `composed: true`.
- The Management API client in `src/api/` is generated by `@hey-api/openapi-ts` from the package's
  `search-manager` OpenAPI document (`bun run generate`). Never hand-edit it.
- UUI and the backoffice's `umb-*` elements come first, with their variants. Build a custom element
  only when nothing fits, and say in a comment which components were checked. Lit and UUI are
  imported through `@umbraco-cms/backoffice/external/*`.
- Colours and sizes come from UUI tokens (`--uui-color-*`, `--uui-size-*`). Use a plugin-scoped
  `--search-manager-*` custom property only when no token fits, and define it once.
- Index field values are rendered as text, never as HTML.
- Every control is keyboard-reachable and labelled; toggles expose `aria-pressed` and menus
  `aria-expanded`.
