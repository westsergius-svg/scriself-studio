<?php
declare(strict_types=1);

function download_version_label(string $version): string
{
    return preg_replace('/^(?:ScriSelf(?:\s+Studio)?\s+|v(?=\d))/i', '', trim($version));
}

/** Prefer the actual installer size; older admin entries mix bytes and megabytes. */
function download_size_label(array $download, string $lang = 'ru'): string
{
    $url = trim((string) ($download['file_url'] ?? ''));
    $parts = parse_url($url);
    $host = is_array($parts) ? ($parts['host'] ?? '') : '';
    $siteHost = defined('SITE_URL') ? (string) parse_url(SITE_URL, PHP_URL_HOST) : '';
    if (is_array($parts) && ($host === '' || strcasecmp($host, $siteHost) === 0)) {
        $path = rawurldecode($parts['path'] ?? '');
        if ($path !== '' && !str_contains($path, "\0")) {
            $uploads = realpath(__DIR__ . '/../uploads/scrisoft');
            $path = str_contains($path, '/') ? '/' . ltrim($path, '/') : '/uploads/scrisoft/' . $path;
            $file = realpath(__DIR__ . '/..' . $path);
            if ($uploads !== false && $file !== false
                && str_starts_with($file, $uploads . DIRECTORY_SEPARATOR) && is_file($file)) {
                $bytes = filesize($file);
                if ($bytes !== false) {
                    return number_format($bytes / 1048576, 2, $lang === 'en' ? '.' : ',', ' ')
                        . ($lang === 'en' ? ' MB' : ' МБ');
                }
            }
        }
    }

    $value = trim((string) ($download['file_size'] ?? ''));
    $number = str_replace([' ', "\u{00A0}", ','], ['', '', '.'], $value);
    if ($number !== '' && is_numeric($number)) {
        $size = (float) $number;
        // Legacy integer entries of at least 1 KiB are byte counts.
        if (ctype_digit($number) && $size >= 1024) { $size /= 1048576; }
        return number_format($size, 2, $lang === 'en' ? '.' : ',', ' ')
            . ($lang === 'en' ? ' MB' : ' МБ');
    }
    if (preg_match('/^([\d.,]+)\s*(?:MB|МБ)$/iu', $value, $match)) {
        return number_format((float) str_replace(',', '.', $match[1]), 2, $lang === 'en' ? '.' : ',', ' ')
            . ($lang === 'en' ? ' MB' : ' МБ');
    }
    return $value;
}
