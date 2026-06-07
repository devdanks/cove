// Single source of truth for the changelog is /CHANGELOG.md at the repo root.
// We import it as a raw string and parse it here, so the About page and the
// markdown file never drift. To add a release, edit CHANGELOG.md only.
import changelogRaw from "../../../CHANGELOG.md?raw";

export interface ChangelogEntry {
  /** Semantic version without a leading "v", e.g. "0.0.35". */
  version: string;
  /** Release date as written in the changelog heading, e.g. "2026-06-06". */
  date: string;
  /** Optional short summary line shown under the version heading. */
  summary?: string;
  /** Notable changes for this version. */
  highlights: string[];
}

// Matches headings like "## [0.0.35] - 2026-06-06" or "## 0.0.35 - 2026-06-06",
// including semver prerelease suffixes such as "0.0.35-beta.1".
const HEADING_RE = /^##\s+\[?v?([0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)\]?\s*(?:-\s*(.+))?$/;

function parseChangelog(markdown: string): ChangelogEntry[] {
  const entries: ChangelogEntry[] = [];
  let current: ChangelogEntry | null = null;

  for (const rawLine of markdown.split(/\r?\n/)) {
    const line = rawLine.trim();

    const heading = HEADING_RE.exec(line);
    if (heading) {
      if (current) entries.push(current);
      current = { version: heading[1], date: (heading[2] ?? "").trim(), highlights: [] };
      continue;
    }

    if (!current || line === "") continue;

    if (line.startsWith("- ") || line.startsWith("* ")) {
      current.highlights.push(line.slice(2).trim());
    } else if (!current.summary && !line.startsWith("#")) {
      current.summary = line;
    }
  }

  if (current) entries.push(current);
  return entries;
}

// Newest first (matches the top-to-bottom order in CHANGELOG.md).
export const CHANGELOG: ChangelogEntry[] = parseChangelog(changelogRaw);

/** The most recent N changelog entries (defaults to 3). */
export function recentChangelog(count = 3): ChangelogEntry[] {
  return CHANGELOG.slice(0, count);
}
