# Umbraco Search Manager

A search index manager for the Umbraco backoffice and a drop-in replacement for the Examine
Management dashboard. Install the package and it takes the dashboard's place under Settings, with
every index on one page and no configuration. On top of that, you can rebuild several indexes at
once in a queue that keeps running when you leave the page, see who rebuilt what and when, and
search any index with click-to-filter chips instead of hand-written Lucene.

## Install

One package line per Umbraco major: `17.x` for Umbraco 17, `18.x` for Umbraco 18.

```sh
# Umbraco 17
dotnet add package Umbraco.Community.SearchManager --version 17.0.0

# Umbraco 18
dotnet add package Umbraco.Community.SearchManager --version 18.0.0
```

Use the newest version of your line. Without `--version`, the newest overall is the `18.x` line.

On a site that uses Umbraco Search, add `Umbraco.Community.SearchManager.UmbracoSearch` at the
same version as well.

## Quick start

1. Run the site and sign in as a user with access to the Settings section.
2. Open **Settings > Advanced > Search Manager**.
3. Tick two indexes, choose **Rebuild 2 indexes**, and confirm.
4. Choose **Explore**, type `type:` and pick a document type.

No configuration is needed. The core Examine Management dashboard is hidden by default; set
`SearchManager:HideCoreExamineDashboard` to `false` to keep it.

## Features

- Every index in one table: health, documents, deleted documents, fields, size and last rebuild.
- Bulk rebuilds in a server-side queue, one at a time or all at once, with live progress, and a
  confirmation that says what happens to live search for each index.
- Rebuild history in the database: the last 20 runs per index, with who, when, how long and the
  error of a failure.
- Explore: filter chips, field and value suggestions, a fields panel, a document drawer, columns,
  cultures, and the whole view in the URL.
- Show query (Lucene and C# with Examine) and a raw Lucene mode with one-click fixes.
- A warning on load-balanced sites that core Examine rebuilds only affect one server.

## Documentation

- [README with screenshots](https://github.com/worm-brain/Umbraco.SearchManager#readme)
- [Documentation](https://github.com/worm-brain/Umbraco.SearchManager/blob/main/docs/index.md)
- [Configuration reference](https://github.com/worm-brain/Umbraco.SearchManager/blob/main/docs/reference/configuration.md)
- [Changelog](https://github.com/worm-brain/Umbraco.SearchManager/blob/main/CHANGELOG.md)

## Support and licence

Questions and bugs: [GitHub issues](https://github.com/worm-brain/Umbraco.SearchManager/issues).
MIT licence.
