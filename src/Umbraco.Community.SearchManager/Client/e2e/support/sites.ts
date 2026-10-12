import { fileURLToPath } from "node:url";

/** One sample site the e2e suite runs against. */
export interface E2eSite {
  /** Playwright project name. */
  name: "site17" | "site18" | "search17" | "search18";
  /** Umbraco major the site is built for (`-p:UmbracoMajor`). */
  major: 17 | 18;
  /** Whether the site has Umbraco Search and the adapter package installed. */
  umbracoSearch: boolean;
  /** HTTPS port; the backoffice login refuses plain HTTP. */
  port: number;
  baseURL: string;
  /** The site's project folder; `dotnet run` uses it as the content root. */
  projectDir: string;
}

const repoRoot = fileURLToPath(new URL("../../../../../", import.meta.url));

/**
 * Default e2e ports, clear of the sample sites' own launch ports (44380-44383), so a site left
 * running for manual work is never reused by accident. `E2E_<NAME>_PORT` overrides one.
 */
const DEFAULT_PORTS = { site17: 44390, site18: 44391, search17: 44392, search18: 44393 } as const;

function site(major: 17 | 18, umbracoSearch: boolean): E2eSite {
  const name = umbracoSearch ? (`search${major}` as const) : (`site${major}` as const);
  const override = process.env[`E2E_${name.toUpperCase()}_PORT`];
  const port = override ? Number(override) : DEFAULT_PORTS[name];
  return {
    name,
    major,
    umbracoSearch,
    port,
    baseURL: `https://localhost:${port}`,
    projectDir: `${repoRoot}samples/SearchManager.${umbracoSearch ? "SearchSite" : "Site"}${major}`,
  };
}

/**
 * The sites this run covers. `E2E_SITES` is a comma list of majors (`17`, `18` or `17,18`, the
 * default); each major brings its plain site and its Umbraco Search site. CI runs one major per
 * matrix job.
 */
export const sites: ReadonlyArray<E2eSite> = (process.env.E2E_SITES ?? "17,18")
  .split(",")
  .map((value) => Number(value.trim()))
  .filter((major): major is 17 | 18 => major === 17 || major === 18)
  .flatMap((major) => [site(major, false), site(major, true)]);

/** The repository root, with a trailing separator. */
export { repoRoot };
