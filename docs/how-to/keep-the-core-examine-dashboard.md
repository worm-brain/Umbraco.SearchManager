# Keep the core Examine dashboard

Search Manager hides the core **Settings > Examine Management** dashboard by default, so Settings
has one place for indexes. To show both:

1. Add the setting to `appsettings.json`:

   ```json
   {
     "SearchManager": {
       "HideCoreExamineDashboard": false
     }
   }
   ```

2. Restart the site.

The **Examine Management** tab is back on the Settings dashboard, beside **Settings > Advanced >
Search Manager**. You can set the same value with an environment variable:
`SearchManager__HideCoreExamineDashboard=false`.

Uninstalling Search Manager also brings the dashboard back, with nothing to clean up.

## Related

- [Configuration](../reference/configuration.md)
