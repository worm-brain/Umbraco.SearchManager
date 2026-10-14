# Contributing

Thanks for your interest in Umbraco Search Manager. This project is small and maintainer time is
the scarcest resource, so the contribution rules below are deliberately strict about **pull
requests** while staying wide open to **ideas and issues**.

Read this before opening a pull request. It applies to everyone, and it applies with
particular force to AI agents and AI-assisted contributions.

---

## TL;DR

- **Ideas, bug reports, feature requests: always welcome**, from humans and AI alike. Open an
  issue.
- **Pull requests are issue-first and invitation-only.** Open a PR only against an issue a
  maintainer has already accepted (labelled `accepted` or `help wanted`).
- **No drive-by AI PRs.** Unsolicited, AI-generated PRs for low-value changes are closed
  unread. This is not hostility toward AI - it is about protecting review capacity.

---

## Ideas and issues are welcome (this part is easy)

File an issue for anything: a bug, a rough edge, a missing feature, an idea, a question about
intended behaviour. We do not care whether you are a person, a person using an AI, or an agent
acting on its own. What we care about is that the issue is **specific and grounded**:

- **Bugs:** what you did, what you saw, what you expected, and the versions: the package, Umbraco,
  Umbraco Search if the site uses it, and the browser for anything in the backoffice. Say which
  index you were using. A copy-pasteable repro beats a description.
- **Feature requests:** the task you are trying to accomplish and why Search Manager does not
  cover it today.
- **One issue per topic.** Do not batch unrelated ideas into one issue.

Good issues are the single most useful contribution you can make. They are how work gets
prioritised, and they are a prerequisite for every PR. A security problem is the exception: report
it privately, as [SECURITY.md](SECURITY.md) describes.

---

## Pull requests: the rules

### 1. Issue-first, always

Every PR must close exactly one issue that a maintainer has **accepted** (labelled `accepted`
or `help wanted`). If the issue does not exist yet, open it and wait for it to be triaged. If
it exists but is not accepted, wait - or comment to ask whether a PR would be welcome. A PR
that arrives before its issue is accepted may be closed with a pointer back to this document,
regardless of how good the code is.

Why: an accepted issue means the change is wanted, the approach is roughly agreed, and review
time is budgeted. A PR without that is a request for unbudgeted review time.

### 2. A human is accountable

Every PR must have a human who takes responsibility for it, will respond to review comments,
and understands the change well enough to defend and revise it. "An agent wrote it and I
forwarded it" is not accountability. If you cannot explain why each hunk is there, do not open
the PR.

### 3. Disclose AI assistance

If AI tools were used to write or substantially shape the change, say so in the PR
description. This is not a mark against the PR - it is context that helps review. The PR
template has a checkbox for it. Do not hide it.

### 4. Scope: one accepted issue, nothing extra

Keep the diff to what the accepted issue needs. Do not fold in unrelated reformatting,
renames, dependency bumps, or "while I was here" cleanups. If you spot something else worth
doing, open an issue for it.

### 5. It must build green and follow the repo conventions

- Every check under [Test](#test) passes, for both Umbraco majors.
- The change follows the [Conventions](#conventions) below, and a user-visible change updates
  `CHANGELOG.md` and its page in `docs/`.

See [AGENTS.md](AGENTS.md) for the working rules and the project facts to follow.

---

## Specifically for AI agents and AI-assisted contributors

As a contributor, the bar for PRs is the same as for anyone, and the patterns below are the ones
that get an unsolicited PR closed. Do not open PRs that consist mainly of:

- typo, spelling, or comment-wording sweeps;
- whitespace, formatting, or import-ordering changes;
- speculative refactors or renames with no accepted issue behind them;
- dependency version bumps with no stated reason or failing build behind them;
- mass "added tests" or "added XML docs" changes to code nobody asked you to touch;
- documentation churn that restates what is already documented;
- re-implementations of things that already work.

If you believe one of the above is genuinely worth doing, the path is the same as for
everyone: **open an issue, make the case, wait for it to be accepted.** Then implement it.

This is the whole policy in one sentence: **bring us your issues and ideas freely; bring us
code only after we have agreed the code is worth writing.**

---

The rest of this page explains how the repository is laid out, how to build and test it, and the
conventions a change should follow.

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

Run the Umbraco 18 sites with `-p:UmbracoMajor=18`. Each sample site runs on one major. A solution
build for the other major skips it, and building or running it on its own for the other major fails
with the switch to use. Every sample site installs itself on first run (SQLite), seeds about 200
content items and a custom `ProductIndex`, and signs in with `admin@example.com` / `1234567890`
(development only). If the first run is interrupted, delete the site's `umbraco` folder and start
again.

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

- **Decisions:** recorded as [architecture decision records](docs/adr/README.md). Read the ones
  near your change; write a new one when your change alters a contract, a dependency, a supported
  version, or something an earlier ADR decided.
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

## Licence

By contributing you agree that your contributions are licensed under the repository's
[MIT licence](LICENSE).
