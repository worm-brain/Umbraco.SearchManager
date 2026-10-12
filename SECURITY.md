# Security policy

## Supported versions

| Package line | Umbraco | Supported |
| --- | --- | --- |
| `17.x` | 17 | Yes |
| `18.x` | 18 | Yes |

Security fixes go into the newest release of each line.

## Reporting a vulnerability

Please do not open a public issue for a security problem.

Report it privately through GitHub: on the repository's **Security** tab, choose **Report a
vulnerability**. Include what you found, how to reproduce it, and which package version and
Umbraco version you used.

You will get an acknowledgement, and the fix will be coordinated with you before any details are
published.

## Scope

Search Manager shows the contents of search indexes and rebuilds them from the Umbraco backoffice,
so reports about these are especially welcome:

- a user seeing an index, a document or a rebuild they should not, such as a member index without
  Members section access;
- access to the API or the rebuild events without Settings section access;
- index content being rendered as HTML or script;
- a way to make a rebuild or a search harm the site beyond the load it is expected to cause.

See [the security model](docs/explanation/security.md) for what Search Manager is designed to
guarantee.
