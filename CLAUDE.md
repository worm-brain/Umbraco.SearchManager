# Umbraco.Community.SearchManager

An Umbraco 17/18 backoffice search index manager and a drop-in replacement for the Examine Management dashboard: every index at a glance, queued bulk rebuilds with history, and click-to-filter search over core Examine and Umbraco Search indexes.

The build brief (`BRIEF.md`), the research and the prototype live in the maintainer's private planning repository, not here. A gitignored `CLAUDE.local.md` at the checkout root imports the brief when it exists; references to "BRIEF §n" mean that file.

## Working rules

- Work is tracked in the maintainer's private issue tracker (milestone per phase; `CLAUDE.local.md` says where). Pick up an issue, plan it, wait for approval, then build. Close it with a short resolution comment.
- Verify Umbraco, Examine and Umbraco Search APIs against the installed version before use; never invent aliases, routes, element names or members. When docs and the installed package disagree, the package wins.
- Decisions and Verify outcomes that change the brief go in `docs/adr/` (see its README), and the brief is updated in the same change.
- The Core project must not reference Umbraco packages. The main package must not reference Umbraco Search; only the adapter package does (it would upgrade every site to Examine 4).
- Organise by feature folder, not technical layer.
- UI: UUI and `umb-*` components first, with their variants; use the `umbraco-backoffice-ui-developer` agent for client work.
- Tooling: Bun + Vitest + Prettier for the client; xUnit v3 + NSubstitute + CSharpier for .NET.
- One package per Umbraco major: `dotnet build` targets 17, `-p:UmbracoMajor=18` targets 18. Major-specific code goes in paired `*.V17.cs` / `*.V18.cs` files. Build and test both majors before committing anything that touches Umbraco APIs.
- Run `dotnet test` and `bun run test` (client) before every commit, and read the build summary: stop any running sample site first, because a locked DLL shows up as build errors. Conventional commits, ASCII only. Update `CHANGELOG.md` per user-visible change.
- Branching: the skeleton went to `main`; from v1, one branch and PR per milestone. Parallel lanes use their own worktree branches merged into the milestone branch.
- At the start of a session, read the handoff that `CLAUDE.local.md` points to, if it exists.
