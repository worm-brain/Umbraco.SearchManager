# Configuration

Every setting is optional and lives under `SearchManager` in `appsettings.json` (or any other
configuration source, such as environment variables: `SearchManager__HideCoreExamineDashboard=false`).

## Settings

| Setting | Default | What it does |
| --- | --- | --- |
| `HideCoreExamineDashboard` | `true` | Hides the core **Settings > Examine Management** dashboard and its field modals, so Search Manager is the one place for indexes. Set `false` to show both. Uninstalling the package brings the dashboard back with no clean-up. |

A full example with the defaults:

```json
{
  "SearchManager": {
    "HideCoreExamineDashboard": true
  }
}
```
