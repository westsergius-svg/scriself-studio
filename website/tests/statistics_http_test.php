<?php
// Продолжение statistics_test.php; используются его одноразовые база и HTTP-стенд.
if (PHP_SAPI !== 'cli' || !isset($base, $db)) { http_response_code(403); exit; }

$valid = ['installation_id' => 'aaaaaaaa-1234-4abc-8abc-000000000001', 'version' => '1.1.12', 'platform' => 'win-x64'];
$jsonHeaders = ['Content-Type' => 'application/json'];
$updaterHeaders = ['User-Agent' => 'ScriSelfStudioUpdater/1.0'];
$downloadUrl = '/uploads/scrisoft/' . $filename;

test('HTTP activity accepts a valid report once per day', static function () use ($base, $db, $valid, $jsonHeaders): void {
    $before = (int) $db->query('SELECT COUNT(*) FROM app_installations')->fetchColumn();
    for ($i = 0; $i < 3; $i++) {
        $response = http_request($base, '/app-activity.php', 'POST', json_encode($valid), $jsonHeaders);
        expect($response['status'] === 204 && $response['body'] === '', 'Activity was not acknowledged with empty 204');
    }
    expect((int) $db->query('SELECT COUNT(*) FROM app_installations')->fetchColumn() === $before + 1, 'HTTP retries created duplicate installations');
});
test('HTTP activity rejects invalid bodies, methods and content types', static function () use ($base, $valid, $jsonHeaders): void {
    expect(http_request($base, '/app-activity.php')['status'] === 405, 'GET activity accepted');
    expect(http_request($base, '/app-activity.php', 'POST', json_encode($valid), ['Content-Type' => 'text/plain'])['status'] === 415, 'Wrong content type accepted');
    foreach (['{', 'null', '[]', json_encode($valid + ['email' => 'private@example.test'])] as $body) {
        expect(http_request($base, '/app-activity.php', 'POST', $body, $jsonHeaders)['status'] === 400, 'Malformed activity accepted');
    }
    expect(http_request($base, '/app-activity.php', 'POST', str_repeat('x', 1025), $jsonHeaders)['status'] === 413, 'Oversize activity accepted');
});
test('database outage returns retryable activity failure without exposing server errors', static function () use ($base, $valid, $jsonHeaders): void {
    $response = http_request($base, '/app-activity.php', 'POST', json_encode($valid), $jsonHeaders + ['X-Test-Database' => 'offline']);
    expect($response['status'] === 503 && !str_contains($response['body'], 'Test database outage'), 'Database error leaked or success was forged');
});
test('updater downloads exact bytes without VK and increments both stages once', static function () use ($base, $db, $downloadUrl, $payload, $updaterHeaders): void {
    $before = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    $response = http_request($base, $downloadUrl, 'GET', null, $updaterHeaders);
    expect($response['status'] === 200 && $response['body'] === $payload, 'Installer corrupted or blocked');
    expect($response['headers']['cache-control'] === 'no-store, no-cache, must-revalidate, max-age=0', 'Downloads can bypass counter via cache');
    expect((int) $response['headers']['content-length'] === strlen($payload), 'Wrong content length');
    $after = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    expect($after['started'] === $before['started'] + 1 && $after['completed'] === $before['completed'] + 1 && $after['updater_completed'] === $before['updater_completed'] + 1, 'Download counted incorrectly');
});
test('HEAD and unsupported methods do not increment download counters', static function () use ($base, $db, $downloadUrl, $updaterHeaders): void {
    $before = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    $response = http_request($base, $downloadUrl, 'HEAD', null, $updaterHeaders);
    expect($response['status'] === 200 && $response['body'] === '', 'HEAD returned payload');
    expect(http_request($base, $downloadUrl, 'POST', '', $updaterHeaders)['status'] === 405, 'POST download accepted');
    expect(app_stats_summary($db, gmdate('Y-m-d'))['download_totals'] === $before, 'Non-download increased counters');
});
test('Range requests receive one whole file and direct downloads have their own source', static function () use ($base, $db, $downloadUrl, $payload): void {
    $before = (int) $db->query("SELECT COALESCE(SUM(completed),0) FROM app_download_daily WHERE source='direct'")->fetchColumn();
    $response = http_request($base, $downloadUrl, 'GET', null, ['Range' => 'bytes=0-9']);
    expect($response['status'] === 200 && $response['body'] === $payload && $response['headers']['accept-ranges'] === 'none', 'Partial download changed counting semantics');
    expect((int) $db->query("SELECT COALESCE(SUM(completed),0) FROM app_download_daily WHERE source='direct'")->fetchColumn() === $before + 1, 'Direct source miscounted');
});
test('unpublished and malformed update names are not counted or exposed', static function () use ($base, $db): void {
    $before = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    foreach (['file=ScriSelfStudio-Setup-1.1.11.exe', 'file=..%2Fconfig.php', 'file[]=test.exe', 'file=missing.exe'] as $query) {
        expect(http_request($base, '/update-download.php?' . $query)['status'] === 404, 'Unpublished file was exposed');
    }
    expect(app_stats_summary($db, gmdate('Y-m-d'))['download_totals'] === $before, 'Rejected download increased counters');
});
test('statistics failure does not corrupt or stop update delivery', static function () use ($base, $downloadUrl, $payload, $updaterHeaders): void {
    $response = http_request($base, $downloadUrl, 'GET', null, $updaterHeaders + ['X-Test-Database' => 'offline']);
    expect($response['status'] === 200 && $response['body'] === $payload, 'Statistics failure broke the installer');
});
test('an interrupted transfer increments started but not completed', static function () use ($base, $db, $root, $payload, $filename): void {
    $largeName = 'ScriSelfStudio-Setup-9.9.9.exe';
    $large = fopen($root . '/uploads/scrisoft/' . $largeName, 'wb');
    for ($i = 0; $i < 2048; $i++) { fwrite($large, $payload); }
    fclose($large);
    file_put_contents($root . '/uploads/scrisoft/latest.json', json_encode(['file' => $largeName]));
    try {
        $parts = parse_url($base);
        $socket = fsockopen($parts['host'], $parts['port']);
        stream_set_timeout($socket, 3);
        fwrite($socket, "GET /uploads/scrisoft/$largeName HTTP/1.1\r\nHost: localhost\r\nUser-Agent: ScriSelfStudioUpdater/1.0\r\nConnection: close\r\n\r\n");
        $headers = '';
        while (!str_contains($headers, "\r\n\r\n") && !feof($socket) && strlen($headers) < 8192) {
            $headers .= fread($socket, 1);
        }
        expect(str_starts_with($headers, 'HTTP/1.1 200'), 'Large download did not start');
        fread($socket, 1024);
        fclose($socket);
        http_request($base, '/health');
        $stmt = $db->prepare('SELECT SUM(started), SUM(completed) FROM app_download_daily WHERE file_name=?');
        $stmt->execute([$largeName]);
        $counts = array_map('intval', $stmt->fetch(PDO::FETCH_NUM));
        expect($counts === [1, 0], 'Interrupted download counted as complete');
    } finally {
        file_put_contents($root . '/uploads/scrisoft/latest.json', json_encode(['file' => $filename]));
    }
});
test('website downloads require no account and ignore obsolete login cookies', static function () use ($base, $db, $payload): void {
    $before = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    $anonymous = http_request($base, '/download/file/1');
    expect($anonymous['status'] === 200 && $anonymous['body'] === $payload, 'Anonymous website download was blocked');
    $oldCookie = http_request($base, '/download/file/1', 'GET', null, ['Cookie' => 'scriself_vk_member=expired; scriself_vk_oauth=invalid']);
    expect($oldCookie['status'] === 200 && $oldCookie['body'] === $payload, 'Stale account state blocked download');
    $after = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    expect($after['started'] === $before['started'] + 2 && $after['completed'] === $before['completed'] + 2, 'Public download count wrong');
});
test('download page always shows file links without a login prompt', static function () use ($base): void {
    foreach (['/download', '/download?membership=join', '/en/download'] as $path) {
        $response = http_request($base, $path);
        expect($response['status'] === 200 && str_contains($response['body'], 'href="/download/file/1"'), 'Download link missing');
        expect(!str_contains($response['body'], '/vk/') && !str_contains($response['body'], 'VK ID') && !str_contains($response['body'], 'Войти, чтобы скачать'), 'Obsolete login UI shown');
    }
});
test('obsolete login routes redirect without OAuth and old integration files are disabled', static function () use ($base): void {
    foreach (['/vk/login', '/vk/callback?code=fixture&state=fixture', '/vk/logout'] as $path) {
        $response = http_request($base, $path);
        expect($response['status'] === 303 && $response['headers']['location'] === '/download', 'Obsolete login route remains active');
    }
    expect(http_request($base, '/modules/vk.php')['status'] === 410, 'Old OAuth module still active');
    expect(http_request($base, '/vk_config.php')['status'] === 410, 'Old configuration remains exposed');
});
test('website counts real transfers for relative, absolute and filename-only URLs', static function () use ($base, $db, $payload): void {
    $before = (int) $db->query("SELECT COALESCE(SUM(completed),0) FROM app_download_daily WHERE source='website'")->fetchColumn();
    foreach ([1, 2, 3] as $id) {
        $legacyBefore = (int) $db->query('SELECT download_count FROM downloads WHERE id=' . $id)->fetchColumn();
        $response = http_request($base, '/download/file/' . $id);
        expect($response['status'] === 200 && $response['body'] === $payload, 'Download failed for URL type ' . $id);
        expect((int) $db->query('SELECT download_count FROM downloads WHERE id=' . $id)->fetchColumn() === $legacyBefore + 1, 'Legacy count wrong');
    }
    expect((int) $db->query("SELECT COALESCE(SUM(completed),0) FROM app_download_daily WHERE source='website'")->fetchColumn() === $before + 3, 'Website source count wrong');
});
test('website rejects missing, private, external and traversing files without counting', static function () use ($base, $db): void {
    $before = app_stats_summary($db, gmdate('Y-m-d'))['download_totals'];
    $legacyBefore = (int) $db->query('SELECT download_count FROM downloads WHERE id=1')->fetchColumn();
    foreach ([4, 5, 6, 7] as $id) {
        expect(in_array(http_request($base, '/download/file/' . $id)['status'], [404, 503], true), 'Unsafe/missing download allowed');
    }
    expect(http_request($base, '/download/file/1', 'HEAD')['status'] === 200, 'HEAD failed');
    expect(app_stats_summary($db, gmdate('Y-m-d'))['download_totals'] === $before, 'Rejected website request counted');
    expect((int) $db->query('SELECT download_count FROM downloads WHERE id=1')->fetchColumn() === $legacyBefore, 'HEAD increased legacy count');
});
test('admin statistics require authorization and render real query results', static function () use ($base): void {
    $guest = http_request($base, '/admin.php?action=app_statistics');
    expect($guest['body'] === 'Admin login required', 'Statistics exposed without login');
    expect(http_request($base, '/modules/admin_app_statistics.php')['status'] === 403, 'Partial page exposed directly');
    $admin = http_request($base, '/admin.php?action=app_statistics', 'GET', null, ['X-Test-Admin' => 'yes']);
    expect($admin['status'] === 200 && str_contains($admin['body'], 'Без сигналов 30 дней') && str_contains($admin['body'], 'ScriSelfStudio-Setup-1.1.12.exe'), 'Admin summary missing');
    expect(!str_contains($admin['body'], 'Warning:') && !str_contains($admin['body'], 'Fatal error'), 'Admin render failed');
});
test('admin database outage renders an error instead of fake zero metrics', static function () use ($base): void {
    $response = http_request($base, '/admin.php?action=app_statistics', 'GET', null, ['X-Test-Admin' => 'yes', 'X-Test-Database' => 'offline']);
    expect(str_contains($response['body'], 'Не удалось загрузить статистику') && !str_contains($response['body'], 'stats-card'), 'Database error masked as zero activity');
});
test('replacing the file under the same version preserves its download count', static function () use ($base, $db, $filename): void {
    $before = (int) $db->query('SELECT download_count FROM downloads WHERE id=1')->fetchColumn();
    $form = http_request($base, '/admin.php?action=downloads&edit=1', 'GET', null, ['X-Test-Admin' => 'yes']);
    expect(str_contains($form['body'], 'name="id" value="1"') && str_contains($form['body'], 'Сохранить изменения'), 'Existing release cannot be edited');
    $checksum = str_repeat('a', 64);
    $body = http_build_query(['csrf' => 'fixture-csrf', 'id' => 1, 'os' => 'Windows', 'version' => '1.1.12', 'file_url' => '/uploads/scrisoft/' . $filename, 'file_size' => '36.7 MB', 'checksum_sha256' => $checksum, 'changelog' => 'Updated file', 'is_public' => '1', 'released_at' => '2026-10-30 12:00:00']);
    $response = http_request($base, '/admin.php?action=save_download', 'POST', $body, ['Content-Type' => 'application/x-www-form-urlencoded', 'X-Test-Admin' => 'yes']);
    expect($response['status'] === 302, 'Release update rejected');
    $row = $db->query('SELECT download_count,checksum_sha256 FROM downloads WHERE id=1')->fetch(PDO::FETCH_ASSOC);
    expect((int) $row['download_count'] === $before && $row['checksum_sha256'] === $checksum, 'Replacing the file reset downloads or lost checksum');
});
