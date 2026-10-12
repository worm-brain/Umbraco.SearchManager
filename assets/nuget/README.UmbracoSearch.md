# Umbraco Search Manager: Umbraco Search adapter

Adds Umbraco Search indexes to
[Umbraco Search Manager](https://www.nuget.org/packages/Umbraco.Community.SearchManager), the
search index manager for the Umbraco backoffice. Install it on sites that use Umbraco Search.

## Install

Use the same version as Umbraco Search Manager: `17.x` for Umbraco 17, `18.x` for Umbraco 18.

```sh
# Umbraco 17, with Umbraco Search 17.2.0 or later
dotnet add package Umbraco.Community.SearchManager.UmbracoSearch --version 17.0.0

# Umbraco 18, with Umbraco Search 18.2.0 or later
dotnet add package Umbraco.Community.SearchManager.UmbracoSearch --version 18.0.0
```

It brings in Umbraco Search Manager itself. No configuration is needed.

## What it adds

- Umbraco Search indexes in the Indexes view, with their health, document count and a Zero
  downtime badge, and an engine filter.
- Rebuilds through Umbraco Search's own rebuilder, with live progress and history. Rebuilds
  started elsewhere are recorded too.
- Explore on Umbraco Search indexes: chips run through Umbraco Search's search, and chips it
  cannot run are shown struck through with the reason.
- A Search Manager box on Umbraco Search's index page, with the last rebuild and links to Explore
  and the history.

## Documentation

- [Use it with Umbraco Search](https://github.com/worm-brain/Umbraco.SearchManager/blob/main/docs/how-to/use-with-umbraco-search.md)
- [Documentation](https://github.com/worm-brain/Umbraco.SearchManager/blob/main/docs/index.md)

MIT licence.
