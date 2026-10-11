/**
 * The file name from a Content-Disposition header, preferring the RFC 5987 `filename*` form. Path separators are
 * removed, so a server value can never point the download outside the user's chosen folder. Null when absent.
 */
export function fileNameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const extended = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  let name: string | null = null;
  if (extended) {
    try {
      name = decodeURIComponent(extended[1].trim());
    } catch {
      name = null;
    }
  }
  if (name === null) {
    const plain = /filename\s*=\s*("([^"]*)"|[^;]+)/.exec(header);
    name = plain ? (plain[2] ?? plain[1]).trim() : null;
  }
  const safe = name?.replace(/[\\/]/g, '_').trim();
  return safe ? safe : null;
}
