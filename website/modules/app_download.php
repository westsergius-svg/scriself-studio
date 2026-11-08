<?php
declare(strict_types=1);

require_once __DIR__ . '/app_statistics.php';

// Общая выдача для обновлений и загрузок сайта. Авторизация выполняется вызывающим.
function app_send_download(string $path, string $source, ?callable $onStarted = null): never
{
    $method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
    if (!in_array($method, ['GET', 'HEAD'], true)) {
        header('Allow: GET, HEAD');
        http_response_code(405);
        exit;
    }
    $handle = @fopen($path, 'rb');
    if ($handle === false) { http_response_code(503); exit; }
    $size = fstat($handle)['size'];
    if ($size <= 0) { fclose($handle); http_response_code(503); exit; }
    $name = basename($path);
    $headerName = preg_replace('/[\r\n"\\\\]/', '_', $name);
    if (session_status() === PHP_SESSION_ACTIVE) { session_write_close(); }
    while (ob_get_level() > 0) { ob_end_clean(); }
    ini_set('zlib.output_compression', '0');
    header('Content-Type: application/octet-stream');
    header('Content-Disposition: attachment; filename="' . $headerName . '"; filename*=UTF-8\'\'' . rawurlencode($name));
    header('Content-Length: ' . $size);
    header('X-Content-Type-Options: nosniff');
    header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
    header('Expires: 0');
    // Range получает полный ответ 200: части одного файла не считаются загрузками.
    header('Accept-Ranges: none');
    if ($method === 'HEAD') { fclose($handle); exit; }

    $day = gmdate('Y-m-d');
    app_stats_record_download($name, $source, false, $day);
    if ($onStarted !== null) {
        try { $onStarted(); }
        catch (Throwable $error) { error_log('Download counter unavailable: ' . $error->getMessage()); }
    }
    set_time_limit(0);
    $sent = 0;
    while (!feof($handle) && !connection_aborted()) {
        $chunk = fread($handle, 262144);
        if ($chunk === false || $chunk === '') { break; }
        echo $chunk;
        $sent += strlen($chunk);
        flush();
    }
    fclose($handle);
    if ($sent === $size && !connection_aborted()) {
        app_stats_record_download($name, $source, true, $day);
    }
    exit;
}
