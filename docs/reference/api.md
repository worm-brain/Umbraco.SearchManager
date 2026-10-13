# Management API

Everything Search Manager shows comes from its own Management API, which you can also call from
scripts and tools. It lives under `/umbraco/search-manager/api/v1` and has its own OpenAPI
document, **Umbraco Search Manager Management API**, in the site's Swagger UI.

## Authentication

The API uses the backoffice's authentication, like Umbraco's own Management API. Every endpoint
needs access to the Settings section. Endpoints that touch member indexes, such as `MembersIndex`,
also need access to the Members section, and leave member indexes out otherwise.

For a script, create an API user in **Users** with a client ID and secret, give it a group with
Settings access, and request a token with the client credentials flow from
`/umbraco/management/api/v1/security/back-office/token`.

## Indexes

| Method and path | What it does |
| --- | --- |
| `GET /indexes` | Lists every index with its engine, health, health message, document, deleted document and field counts, size, and what it supports (rebuild, search, raw Lucene). An `Incomplete` index also has `failedRebuild`: when the failed rebuild ended, its error, and the documents it left. |
| `GET /server` | Names this server, its role, and whether the site runs on more than one server. |
| `GET /settings` | The settings the backoffice needs, currently `hideCoreExamineDashboard`. |

## Rebuilds

| Method and path | What it does |
| --- | --- |
| `GET /rebuilds` | Lists the queued, running and finished runs this server's queue holds, oldest first. |
| `POST /rebuilds` | Queues rebuilds. Body: `{ "indexNames": ["ExternalIndex"], "mode": "OneAtATime" }`, or `"AllAtOnce"`. Indexes that cannot be queued are returned with the reason. |
| `POST /rebuilds/{id}/cancel` | Cancels a queued run. A run that has started cannot be cancelled. |
| `DELETE /rebuilds/finished` | Clears finished runs from the queue. The history keeps them. |
| `GET /rebuilds/history?indexName=ExternalIndex` | The last 20 recorded runs of one index, newest first. |
| `GET /rebuilds/history/latest` | The latest recorded run of each index. |

A run is `Queued`, `Running`, `Succeeded`, `Failed` or `Cancelled`. A core Examine run is
`Succeeded` only once every document is committed and searchable.

A history entry's `trigger` is `User` (from Search Manager, with `triggeredBy`), `Startup`
(Umbraco's rebuild of empty core Examine indexes after the site starts) or `Other` (anything else
outside Search Manager).

## Explore

`{target}` is an index or searcher name, as listed by `GET /explore/targets`.

| Method and path | What it does |
| --- | --- |
| `GET /explore/targets` | The indexes and searchers you can search. |
| `GET /explore/{target}/fields` | The target's fields with their type and coverage, and the short names it accepts. |
| `GET /explore/{target}/terms?field=&prefix=&take=10` | A field's most common values, with counts. |
| `POST /explore/{target}/parse` | Turns search box input into filters. Body: `{ "input": "umbraco type:blogPost" }`. |
| `POST /explore/{target}/search` | Runs a search with filters, a culture and paging, and returns the results and the query sent. |
| `POST /explore/{target}/facets` | Counts the top values of the fields panel's fields, for the whole index or for a search. |
| `POST /explore/{target}/compile` | Returns the Lucene and C# a set of filters compiles to, as Show query displays them. |
| `POST /explore/{target}/validate` | Checks a raw Lucene query and returns the parse error, its position and the warnings with fixes. |
| `POST /explore/{target}/read-native` | Turns a raw Lucene query back into filters. |

The Swagger UI shows the full request and response schemas.

## Live updates

The backoffice receives rebuild changes through Umbraco's server events, so the Rebuilds panel
updates without polling. Only users with Settings access receive them, and Members access as well
for member indexes.

## Related

- [The security model](../explanation/security.md)
- [How rebuilds work](../explanation/how-rebuilds-work.md)
