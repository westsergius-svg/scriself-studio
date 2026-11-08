<?php
declare(strict_types=1);
require __DIR__ . '/config.php';
require __DIR__ . '/modules/app_download.php';
header('Cache-Control: no-store');
$method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
if (!in_array($method, ['GET', 'HEAD'], true)) {
    header('Allow: GET, HEAD');
    http_response_code(405);
    exit;
}
$name = $_GET['file'] ?? '';
$path = is_string($name) ? app_update_file(__DIR__, $name) : null;
if ($path === null) { http_response_code(404); exit; }
$source = str_starts_with($_SERVER['HTTP_USER_AGENT'] ?? '', 'ScriSelfStudioUpdater/') ? 'updater' : 'direct';
app_send_download($path, $source);
