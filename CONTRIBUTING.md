# Contributing

Thanks for helping. This page explains how the repository is laid out, how to build and test it,
and the conventions a change should follow.

## Before you start

- Look for an issue, or open one, before starting anything large.
- Decisions are recorded as [architecture decision records](docs/adr/README.md). Read the ones near
  your change; write a new one when your change alters a contract, a dependency, a supported
  version, or something an earlier ADR decided.

## Layout

```text
src/
  Umbraco.Community.SearchManager.Core/          contracts, the search syntax parser, the Lucene compiler, the rebuild queue; no Umbraco references
  Umbraco.Community.SearchManager/               the Umbraco package: API, Examine sources, rebuilds, history, backoffice client (Client/)
  Umbraco.Community.SearchManager.UmbracoSearch/ the Umbraco Search adapter package
tests/                                           xUnit v3 test projects, and the packed nupkg smoke test (package-smoke/)
samples/                                         Umbraco 17 and 18 sample sites, with and without Umbraco Search
docs/                                            the public documentation
```

Code is organised by feature folder, not by technical layer: each feature owns its endpoint, DTOs,
handler and client elements. The Core project must not reference Umbraco packages. The main
package must not reference Umbraco Search; only the adapter does, because Umbraco Search brings
Examine 4 and would upgrade every site.

## Prerequisites

- .NET SDK 10 (see `global.json`)
- [Bun](https://bun.sh/) for the backoffice client
- Node.js 22 or later, to run the Playwright end-to-end tests
- [uv](https://docs.astral.sh/uv/), only for the packed nupkg smoke test

## Build and run

The package builds once per Umbraco major. `dotnet build` targets Umbraco 17;
`-p:UmbracoMajor=18` targets Umbraco 18. Major-specific code goes only in paired `*.V17.cs` and
`*.V18.cs` files.

```sh
# The backoffice client (its output is gitignored)
cd src/Umbraco.Community.SearchManager/Client && bun install && bun run build

# A sample site
dotnet run --project samples/SearchManager.Site17
```

| Sample site | Umbraco | Umbraco Search | URL |
| --- | --- | --- | --- |
| `SearchManager.Site17` | 17 | No | https://localhost:44380 |
| `SearchManager.Site18` | 18 | No | https://localhost:44381 |
| `SearchManager.SearchSite17` | 17 | Yes | https://localhost:44382 |
| `SearchManager.SearchSite18` | 18 | Yes | https://localhost:44383 |

Run the Umbraco 18 sites with `-p:UmbracoMajor=18`. Every sample site installs itself on first run
(SQLite), seeds about 200 content items and a custom `ProductIndex`, and signs in with
`admin@example.com` / `1234567890` (development only). If the first run is interrupted, delete the
site's `umbraco` folder and start again.

The client build output is a static web asset: after `bun run build`, restart the site to serve
the new files.

## Test

Run every suite before you commit, for both majors when your change touches Umbraco APIs:

```sh
dotnet build && dotnet test
dotnet build -p:UmbracoMajor=18 && dotnet test --solution Umbraco.Community.SearchManager.slnx -p:UmbracoMajor=18

cd src/Umbraco.Community.SearchManager/Client
bun run format:check && bun run typecheck && bun run test && bun run build
bun run e2e   # Playwright on all four sample sites, on their own ports (44390-44393)
```

`E2E_SITES=17` runs the end-to-end tests on the Umbraco 17 sites only. Before the tests start, `e2e/prepare.ts`
gives each site with no database its first boot (install, sample content, indexing) and retries it
on a fresh database if it stalls: on SQLite, a fresh Umbraco Search site can deadlock in Umbraco's
cache instruction sync (Umbraco issue 23940), and the deadlock never clears. Stop any running sample site
first: a locked DLL shows up as build errors.

The release workflow also installs the packed nupkgs on clean sites of each major's lowest
supported Umbraco version and its newest release, with and without Umbraco Search. To run it
locally after `dotnet pack -o artifacts`:

```sh
uv run tests/package-smoke/smoke_nupkg.py --packages artifacts --major 17
```

## Screenshots

The images in `docs/images/` are taken by a Playwright script from `SearchManager.SearchSite17`, at
1440 x 900 and device scale 2. Run the site, then:

```sh
cd src/Umbraco.Community.SearchManager/Client && bun run docs:screenshots
```

Retake them in the same change as a UI change they show.

## Conventions

- **Tooling:** Bun, Vite, Vitest and Prettier for the client; xUnit v3, NSubstitute and CSharpier
  for .NET.
- **UI:** use UUI and the backoffice's `umb-*` elements, with their variants, before writing a
  custom element. Every control must be keyboard-reachable and labelled.
- **Code docs:** C# members carry XML doc comments; exported TypeScript carries TSDoc.
- **Tests:** new or changed code comes with tests.
- **Commits:** [Conventional Commits](https://www.conventionalcommits.org/), ASCII only.
- **Changelog:** add a line to [CHANGELOG.md](CHANGELOG.md) for every user-visible change.
- **Docs pages:** a user-visible change updates the page in `docs/` that describes it, following
  [Writing docs](#writing-docs).

## Writing docs

The pages in `docs/` follow [Diataxis](https://diataxis.fr/): a tutorial, how-to guides,
reference and explanation, with one kind per page. Write them in this voice:

- **Speak to the reader.** Second person, imperative, present tense: "Click a value to filter on
  it." Never "we".
- **Active voice when a person acts.** Passive is fine when the system is the actor and naming it
  adds nothing: "the run is recorded as failed".
- **No contractions.** Write "do not", "cannot", "it is".
- **One idea per sentence.** Aim for under 25 words. When a sentence strings three or more clauses
  together, make it a list or split it.
- **No parentheses with clauses inside.** Asides get their own sentence or bullet.
- **Action verbs:** *choose* a labelled control (a button, tab or menu item); *click* something on
  the canvas (a chip, a value, a row) or a box you type in; *press* a key; *tick* a checkbox. Do
  not use *select* for an action.
- **UI names in bold, spelled as on screen:** **Settings > Advanced > Search Manager**,
  **Rebuild all**.
- **What the reader can act on, not how it is built.** Leave out internals the reader cannot see
  or change; those belong in an ADR.
- **Numbers, not adjectives.** "the last 20 runs", "20 results to a page". No "easy", "simply",
  "powerful" or "blazing".
- **Shipped features only.** Planned work goes under a **Coming next** heading.
- **en-GB spelling:** organise, analyse, licence (noun).
- **Descriptive links** that name the page they open, and a **Related** list at the end of each
  page.

## Pull requests

Keep a pull request to one issue where you can, say which issue it closes, and include the test
output. Screenshots help for UI changes.
