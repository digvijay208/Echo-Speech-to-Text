/* Single source of truth for Echo download links.
   The desktop app ships on Windows only (for now) — every other
   platform resolves to null, and the UI renders it as "Coming soon". */

export const GITHUB_REPO = "digvijay208/Echo-Speech-to-Text";

/** Windows installer from the latest GitHub release. */
export const WINDOWS_INSTALLER_URL = `https://github.com/${GITHUB_REPO}/releases/latest/download/Echo-Setup-1.0.0.exe`;

/** All past releases (changelog, older versions). */
export const ALL_RELEASES_URL = `https://github.com/${GITHUB_REPO}/releases`;

export const WINDOWS_MIN_LABEL = "Windows 10 / 11 · 64-bit";
