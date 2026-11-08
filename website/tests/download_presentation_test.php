<?php
declare(strict_types=1);

require __DIR__ . '/../modules/download_presentation.php';

function check(bool $condition, string $message): void
{
    if (!$condition) { throw new RuntimeException($message); }
}
function e($value): string { return htmlspecialchars((string) $value, ENT_QUOTES, 'UTF-8'); }
function fmt_date($value): string { return (string) $value; }

check(download_version_label('ScriSelf Studio 1.2.0') === '1.2.0', 'Product prefix');
check(download_version_label('v1.1.3.3S') === '1.1.3.3S', 'Legacy suffix');
check(download_size_label(['file_size' => '38404549']) === '36,63 МБ', 'Legacy bytes');
check(download_size_label(['file_size' => '38,43']) === '38,43 МБ', 'Legacy MB without unit');
check(download_size_label(['file_size' => '71.72 MB']) === '71,72 МБ', 'Legacy English units');
check(download_size_label(['file_size' => '38404549'], 'en') === '36.63 MB', 'English size');
check(download_size_label([]) === '', 'Missing size');

$uploads = __DIR__ . '/../uploads/scrisoft';
if (!is_dir($uploads)) { mkdir($uploads, 0777, true); }
$probe = tempnam($uploads, 'size-test-');
try {
    file_put_contents($probe, str_repeat('x', 1048576));
    $row = ['file_url' => '/uploads/scrisoft/' . basename($probe), 'file_size' => '99'];
    check(download_size_label($row) === '1,00 МБ', 'Actual file overrides stale admin size');
    $row['file_url'] = 'https://external.example/uploads/scrisoft/' . basename($probe);
    check(download_size_label($row) === '99,00 МБ', 'External file does not resolve locally');
    check(download_size_label(['file_url' => '/modules/download.php']) === '', 'Outside uploads');
} finally {
    unlink($probe);
}

$lang = 'ru';
$downloads = [
    ['id' => 3, 'version' => 'ScriSelf Studio 1.2.0', 'os' => 'Windows', 'released_at' => '2026-11-04', 'changelog' => 'Current Windows notes'],
    ['id' => 2, 'version' => '1.2.0', 'os' => 'macOS', 'released_at' => '2026-11-04', 'changelog' => 'Current Mac notes'],
    ['id' => 1, 'version' => '1.1.8', 'os' => 'Windows', 'released_at' => '2026-10-18', 'changelog' => 'Old notes must not appear', 'file_size' => '38404549'],
];
$latest = $downloads[0];
ob_start();
require __DIR__ . '/../modules/download.php';
$page = ob_get_clean();
check(substr_count($page, '<article class="dl-card"') === 2, 'Only current version platforms get cards');
check(substr_count($page, '<li class="release-row"') === 1, 'Older version gets one archive row');
check(str_contains($page, 'Current Windows notes') && str_contains($page, 'Current Mac notes'), 'Current descriptions preserved');
check(!str_contains($page, 'Old notes must not appear'), 'Old descriptions removed from download page');
check(str_contains($page, 'href="/download/file/1"'), 'Old download remains available');
check(str_contains($page, '36,63 МБ'), 'Archive contains readable size');
echo "Download presentation checks passed.\n";
