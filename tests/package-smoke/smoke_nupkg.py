# /// script
# requires-python = ">=3.9"
# dependencies = ["umbraco-spawn-harness @ git+https://github.com/worm-brain/umbraco-spawn-harness@v0.1.0"]
# ///
"""Pre-publish smoke test of the packed Search Manager nupkgs.

For each Umbraco version of one major, spawns clean sites with umbraco-spawn-harness, installs the
packages from a local folder of packed .nupkg files, builds and starts each site (the first start
runs the unattended install), signs in as the admin and checks that the packages work as installed
from NuGet. Two sites per version:

- plain: Umbraco.Community.SearchManager alone, with zero configuration
- umbraco-search: the main package, the Umbraco.Community.SearchManager.UmbracoSearch adapter and
  Umbraco Search (core and Examine provider) at the adapter's floor, turned on by a composer the
  script writes into the site, as a site owner would

Checks on every site:

- the site runs the exact Umbraco version asked for (no silent upgrade past the floor)
- the backoffice lists the package manifest, and umbraco-package.json and the client bundle (with
  every chunk it imports) are served from App_Plugins, stamped with the package version
- GET  /umbraco/search-manager/api/v1/indexes lists the core Examine indexes
- POST /umbraco/search-manager/api/v1/rebuilds rebuilds ExternalIndex to Succeeded, and the run
  shows up in GET /rebuilds/history/latest (the package migration created the history table)
- POST /umbraco/search-manager/api/v1/explore/ExternalIndex/search answers

And on the umbraco-search site: the indexes list Umbraco Search's indexes (engine
`umbraco-search`) without their physical Umb_* Examine index twice, and the backoffice lists the
adapter's Search Manager box for Umbraco Search's index page.

Sites are removed afterwards, also on failure (unless --keep).

    dotnet pack Umbraco.Community.SearchManager.slnx -o <pkgs> -p:UmbracoMajor=17 -p:Version=17.0.0-alpha.0
    uv run tests/package-smoke/smoke_nupkg.py --packages <pkgs> --major 17

By default the versions tried are the floor of the package's declared Umbraco range (from
Directory.Packages.props) and the newest stable release of the major; --umbraco overrides that.
Exits 1 when any site fails.
"""
import argparse
import json
import re
import shutil
import sys
import tempfile
import time
import zipfile
from pathlib import Path
from xml.etree import ElementTree

from umbraco_spawn_harness import (SpawnError, heading, nuget_global_packages, remove_tree, resolve_umbraco_version,
                                   say, scaffold, utf8_stdio, warn)

PACKAGE_ID = "Umbraco.Community.SearchManager"
ADAPTER_ID = "Umbraco.Community.SearchManager.UmbracoSearch"
API = "/umbraco/search-manager/api/v1"
# Where the client build lands inside the package's static web assets.
APP_PLUGIN = "/App_Plugins/UmbracoCommunitySearchManager"
# The box the adapter registers on Umbraco Search's index page (ADR 0015).
BOX_ALIAS = "Umbraco.Community.SearchManager.SearchIndexDetailBox"
REPO_ROOT = Path(__file__).resolve().parents[2]

# Turns Umbraco Search on, as its docs tell a site owner to: the core services and the Examine provider.
UMBRACO_SEARCH_COMPOSER = """using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Search.Core.DependencyInjection;
using Umbraco.Cms.Search.Provider.Examine.DependencyInjection;

namespace SmokeSite;

public sealed class UmbracoSearchComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder) => builder.AddSearchCore().AddExamineSearchProvider();
}
"""


class SmokeFailure(Exception):
    """A check against the running site failed."""


# --------------------------------------------------------------- inputs --
def read_packages(folder):
    """Read the id and version of every .nupkg in a folder from its .nuspec.

    The file name alone is ambiguous (`Umbraco.Community.SearchManager.Core.17.0.0.nupkg`), so the
    nuspec inside each package is the source of truth.

    :param folder: the folder `dotnet pack -o` wrote to.
    :returns: a dict of package id to version.
    :raises SystemExit: when the folder lacks the main package or the adapter.
    """
    packages = {}
    for nupkg in sorted(Path(folder).glob("*.nupkg")):
        with zipfile.ZipFile(nupkg) as archive:
            nuspec = next(n for n in archive.namelist() if n.endswith(".nuspec") and "/" not in n)
            root = ElementTree.fromstring(archive.read(nuspec))
        # The nuspec namespace varies with the NuGet version, so match on local names.
        meta = next(el for el in root if el.tag.endswith("metadata"))
        fields = {el.tag.split("}")[-1]: el.text for el in meta}
        packages[fields["id"]] = fields["version"]
    for required in (PACKAGE_ID, ADAPTER_ID):
        if required not in packages:
            sys.exit(f"error: no {required} .nupkg in {folder}")
    return packages


def declared_floor(major, prop):
    """The lowest version of a range Directory.Packages.props declares for a major.

    :param major: 17 or 18.
    :param prop: the range property, `UmbracoPackageRange` or `UmbracoSearchRange`.
    :returns: the floor version, e.g. `17.0.0`.
    :raises SystemExit: when Directory.Packages.props has no such range for the major.
    """
    props = (REPO_ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
    block = re.search(rf"'\$\(UmbracoMajor\)' == '{major}'\">(.*?)</PropertyGroup>", props, re.S)
    match = block and re.search(rf"<{prop}>\[\s*([^,\]\s]+)", block.group(1))
    if not match:
        sys.exit(f"error: no {prop} for Umbraco {major} in Directory.Packages.props")
    return match.group(1)


def versions_to_try(major):
    """The declared floor of the major and its newest stable release, without duplicates.

    :param major: 17 or 18.
    :returns: the exact Umbraco versions.
    """
    latest = resolve_umbraco_version(str(major)) or sys.exit(f"error: no stable Umbraco {major} on nuget.org")
    return list(dict.fromkeys([declared_floor(major, "UmbracoPackageRange"), latest]))


# ----------------------------------------------------------------- site --
def evict_from_cache(packages):
    """Delete the packages' exact versions from the NuGet global-packages folder.

    A local rebuild keeps the same version (`17.0.0-alpha.0`), and restore would otherwise reuse the
    first copy it extracted rather than the freshly packed one.

    :param packages: a dict of package id to version.
    """
    cache = nuget_global_packages()
    for package, version in packages.items():
        remove_tree(cache / package.lower() / version.lower())


def write_nuget_config(site, folder):
    """Give the site a nuget.config with the local package folder next to nuget.org.

    The harness's `add_package(source=...)` only applies to the `dotnet add package` step, not to the
    restore `build` runs, so the source has to live in configuration. Source mapping sends the Search
    Manager packages to the local folder only, so nothing of the same id and version on nuget.org
    can stand in for what was just packed.

    :param site: the scaffolded harness `Site`.
    :param folder: the folder of .nupkg files.
    """
    (site.dir / "nuget.config").write_text(f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="search-manager-local" value="{Path(folder).resolve()}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="search-manager-local">
      <package pattern="{PACKAGE_ID}" />
      <package pattern="{PACKAGE_ID}.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
""", encoding="utf-8")


# --------------------------------------------------------------- checks --
def call(session, method, url, body=None):
    """Send an authenticated request to the site (any path, not only the Management API).

    :param session: the harness `ManagementApi` from `admin_session()`.
    :param method: the HTTP method.
    :param url: the path from the site root, e.g. `/umbraco/search-manager/api/v1/indexes`.
    :param body: an object to send as JSON.
    :returns: the parsed JSON body.
    :raises SmokeFailure: when the status is not 200 or the body is not JSON.
    """
    status, _, text = session.http.request(method, session.host + url, json_body=body,
                                           headers={"Authorization": f"Bearer {session.token}"})
    if status != 200:
        raise SmokeFailure(f"{method} {url} returned HTTP {status}: {text[:300]}")
    try:
        return json.loads(text)
    except ValueError:
        raise SmokeFailure(f"{method} {url} did not return JSON: {text[:300]}") from None


def check(condition, message):
    """Raise a SmokeFailure with the message when the condition is false."""
    if not condition:
        raise SmokeFailure(message)


def check_client(session, package_version):
    """The backoffice lists the package, and App_Plugins serves its manifest and every client chunk.

    :param session: the signed-in harness session.
    :param package_version: the version the manifest must carry.
    :returns: the private manifest list, for further checks.
    :raises SmokeFailure: on the first failing check.
    """
    manifests = call(session, "GET", "/umbraco/management/api/v1/manifest/manifest/private")
    check(any(m.get("id") == PACKAGE_ID or m.get("name") == "Search Manager" for m in manifests),
          f"The backoffice does not list the package manifest (got {[m.get('name') for m in manifests]})")
    manifest = call(session, "GET", f"{APP_PLUGIN}/umbraco-package.json")
    check(manifest.get("version") == package_version,
          f"umbraco-package.json has version {manifest.get('version')!r}, expected {package_version!r}")
    bundle = next(e["js"] for e in manifest["extensions"] if e.get("type") == "bundle")
    # Follow the bundle's imports transitively: every hashed chunk must be in the static asset manifest.
    pending, seen = [bundle], set()
    while pending:
        url = pending.pop()
        if url in seen:
            continue
        seen.add(url)
        status, _, text = session.http.request("GET", session.host + url)
        check(status == 200 and text.strip(), f"GET {url} returned HTTP {status}")
        folder = url.rsplit("/", 1)[0]
        pending += [f"{folder}/{chunk}" for chunk in set(re.findall(r"\./([\w.-]+\.js)", text))]
    say(f"The backoffice lists the package; {APP_PLUGIN} serves umbraco-package.json {package_version} "
        f"and {len(seen)} client modules")
    return manifests


def check_rebuild(session):
    """Rebuild ExternalIndex through the queue and find the run in the persisted history.

    :param session: the signed-in harness session.
    :raises SmokeFailure: when the run is refused, fails, or is missing from the history.
    """
    queued = call(session, "POST", f"{API}/rebuilds", {"indexNames": ["ExternalIndex"], "mode": "OneAtATime"})
    check(not queued.get("refused"), f"POST /rebuilds refused ExternalIndex: {queued.get('refused')}")
    deadline = time.monotonic() + 180
    while True:
        run = next((r for r in call(session, "GET", f"{API}/rebuilds") if r.get("indexName") == "ExternalIndex"), None)
        status = run and run.get("status")
        if status in ("Succeeded", "Failed", "Cancelled"):
            break
        check(time.monotonic() < deadline, f"ExternalIndex rebuild still {status} after 180 seconds")
        time.sleep(2)
    check(status == "Succeeded", f"ExternalIndex rebuild ended {status}: {run.get('error')}")
    latest = call(session, "GET", f"{API}/rebuilds/history/latest")
    text = json.dumps(latest)
    check("ExternalIndex" in text, f"GET /rebuilds/history/latest has no ExternalIndex run: {text[:300]}")
    say("POST /rebuilds rebuilt ExternalIndex to Succeeded, and the history records it")


def run_checks(site, package_version, umbraco_search):
    """Run every check against a started site.

    :param site: the running harness `Site`.
    :param package_version: the version of the Search Manager packages under test.
    :param umbraco_search: whether the site has the adapter and Umbraco Search.
    :raises SmokeFailure: on the first failing check.
    :raises SpawnError: when the admin cannot sign in.
    """
    session = site.admin_session()

    # The version that actually runs, so a floor test cannot silently resolve a newer Umbraco.
    running = call(session, "GET", "/umbraco/management/api/v1/server/information").get("version", "")
    check(running.split("+")[0] == site.umbraco_version,
          f"The site runs Umbraco {running}, expected {site.umbraco_version}")
    say(f"Signed in; the site runs Umbraco {running}")

    manifests = check_client(session, package_version)

    indexes = call(session, "GET", f"{API}/indexes")
    names = [i.get("name") for i in indexes]
    for core in ("ExternalIndex", "InternalIndex", "MembersIndex"):
        check(core in names, f"GET /indexes has no {core}: {names}")
    say(f"GET /indexes lists {len(indexes)} indexes")

    check_rebuild(session)

    result = call(session, "POST", f"{API}/explore/ExternalIndex/search",
                  {"filters": [], "skip": 0, "take": 10, "culture": None})
    check(isinstance(result.get("hits"), list), f"POST /explore/ExternalIndex/search: unexpected body {result}")
    say("POST /explore/ExternalIndex/search answers")

    if umbraco_search:
        engines = {i.get("name"): i.get("engine") for i in indexes}
        search_indexes = [n for n, e in engines.items() if e == "umbraco-search"]
        check(search_indexes, f"GET /indexes has no Umbraco Search index: {engines}")
        check(len(names) == len(set(n.lower() for n in names)), f"GET /indexes lists an index twice: {names}")
        say(f"GET /indexes lists the Umbraco Search indexes {search_indexes}")
        check(BOX_ALIAS in json.dumps(manifests),
              "The backoffice does not list the adapter's Search Manager box for Umbraco Search's index page")
        say("The backoffice lists the adapter's box for Umbraco Search's index page")


def smoke(root, umbraco, folder, packages, umbraco_search, keep):
    """Spawn one site, install the packages, run the checks and remove the site.

    :param root: the sites folder.
    :param umbraco: the exact Umbraco version.
    :param folder: the folder of .nupkg files.
    :param packages: a dict of package id to version.
    :param umbraco_search: also install the adapter and Umbraco Search.
    :param keep: leave the site running instead of removing it.
    :returns: None when every check passed, else the failure message.
    """
    site = None
    flavour = "search" if umbraco_search else "plain"
    try:
        site = scaffold(root, f"searchmanager-{umbraco}-{flavour}-{time.strftime('%H%M%S')}", umbraco)
        write_nuget_config(site, folder)
        site.add_package(PACKAGE_ID, packages[PACKAGE_ID])
        if umbraco_search:
            # Umbraco Search at the adapter's floor: the lowest version a site owner can pair it with.
            search_floor = declared_floor(umbraco.split(".")[0], "UmbracoSearchRange")
            site.add_package("Umbraco.Cms.Search.Core", search_floor)
            site.add_package("Umbraco.Cms.Search.Provider.Examine", search_floor)
            site.add_package(ADAPTER_ID, packages[ADAPTER_ID])
            (site.project / "UmbracoSearchComposer.cs").write_text(UMBRACO_SEARCH_COMPOSER, encoding="utf-8")
        print("Building", flush=True)
        site.build()
        site.start()
        run_checks(site, packages[PACKAGE_ID], umbraco_search)
        return None
    except (SpawnError, SmokeFailure) as e:
        if site:
            print(site.tail_log(), file=sys.stderr)
        return str(e)
    finally:
        if site and not keep:
            site.remove()


def main():
    utf8_stdio()
    p = argparse.ArgumentParser(prog="smoke_nupkg.py", description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--packages", required=True, help="Folder of packed .nupkg files (dotnet pack -o)")
    p.add_argument("--major", required=True, type=int, choices=(17, 18), help="Umbraco major the packages target")
    p.add_argument("--umbraco", action="append", help="Exact Umbraco version to try (repeatable; default: floor and latest)")
    p.add_argument("--only", choices=("plain", "search"), help="Run only the plain or only the Umbraco Search sites")
    p.add_argument("--root", help="Folder for the spawned sites (default: a new temp folder)")
    p.add_argument("--keep", action="store_true", help="Leave the sites running")
    a = p.parse_args()

    packages = read_packages(a.packages)
    version = packages[PACKAGE_ID]
    if version.split(".")[0] != str(a.major):
        sys.exit(f"error: {PACKAGE_ID} {version} is not an Umbraco {a.major} build")
    for umbraco in a.umbraco or []:
        if umbraco.split(".")[0] != str(a.major):
            sys.exit(f"error: Umbraco {umbraco} is not Umbraco {a.major}")
    umbraco_versions = a.umbraco or versions_to_try(a.major)
    flavours = [f == "search" for f in (["plain", "search"] if not a.only else [a.only])]
    root = Path(a.root) if a.root else Path(tempfile.mkdtemp(prefix="searchmanager-smoke-"))
    evict_from_cache(packages)

    results = {}
    for umbraco in umbraco_versions:
        for umbraco_search in flavours:
            label = f"Umbraco {umbraco}" + (" + Umbraco Search" if umbraco_search else "")
            heading(f"{PACKAGE_ID} {version} on {label}")
            results[label] = smoke(root, umbraco, a.packages, packages, umbraco_search, a.keep)
            if results[label]:
                warn(f"{label}: {results[label]}")

    print()
    for label, failure in results.items():
        print(f"  {label}: {'FAIL - ' + failure.splitlines()[0] if failure else 'pass'}")
    failed = [u for u, f in results.items() if f]
    print(f"{len(results) - len(failed)}/{len(results)} passed" + (f" (sites kept in {root})" if a.keep else ""))
    if not a.keep and not a.root:
        shutil.rmtree(root, ignore_errors=True)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
