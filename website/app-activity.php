<?php
declare(strict_types=1);
require __DIR__ . '/config.php';
require __DIR__ . '/modules/app_statistics.php';
header('Cache-Control: no-store');
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    header('Allow: POST');
    json_response(['error' => 'method_not_allowed'], 405);
}
if (strtolower(trim(explode(';', $_SERVER['CONTENT_TYPE'] ?? '')[0])) !== 'application/json') {
    json_response(['error' => 'json_required'], 415);
}
if ((int) ($_SERVER['CONTENT_LENGTH'] ?? 0) > 1024) {
    json_response(['error' => 'body_too_large'], 413);
}
if (!app_stats_rate_allowed(__DIR__ . '/cache', (string) ($_SERVER['REMOTE_ADDR'] ?? ''), time())) {
    header('Retry-After: 60');
    json_response(['error' => 'try_later'], 429);
}
$body = file_get_contents('php://input', false, null, 0, 1025);
if ($body === false || strlen($body) > 1024) { json_response(['error' => 'body_too_large'], 413); }
$payload = json_decode($body, true);
$activity = is_array($payload) ? app_stats_payload($payload) : null;
if ($activity === null) { json_response(['error' => 'invalid_activity'], 400); }
try {
    app_stats_schema(db());
    app_stats_activity(db(), $activity, gmdate('Y-m-d H:i:s'));
    http_response_code(204);
} catch (Throwable $error) {
    error_log('App activity unavailable: ' . $error->getMessage());
    json_response(['error' => 'temporarily_unavailable'], 503);
}
