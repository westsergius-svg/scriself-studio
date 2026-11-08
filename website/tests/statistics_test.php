<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli') { http_response_code(403); exit; }
require dirname(__DIR__) . '/modules/app_statistics.php';

set_error_handler(static function (int $severity, string $message, string $file, int $line): bool {
    if (!(error_reporting() & $severity)) { return false; }
    throw new ErrorException($message, 0, $severity, $file, $line);
});
function expect(bool $condition, string $message): void
{
    if (!$condition) { throw new RuntimeException($message); }
}
function test(string $name, callable $test): void
{
    $test();
    $GLOBALS['passed']++;
    echo "PASS $name\n";
}
function activity(string $suffix = '1', string $version = '1.1.12'): array
{
    return app_stats_payload(['installation_id' => '12345678-1234-4abc-8abc-' . str_pad($suffix, 12, '0', STR_PAD_LEFT), 'version' => $version, 'platform' => 'win-x64']);
}
function http_request(string $base, string $path, string $method = 'GET', ?string $body = null, array $headers = []): array
{
    $lines = [];
    foreach ($headers as $key => $value) { $lines[] = $key . ': ' . $value; }
    $context = stream_context_create(['http' => [
        'method' => $method, 'header' => implode("\r\n", $lines), 'content' => $body ?? '',
        'ignore_errors' => true, 'follow_location' => 0, 'timeout' => 5,
    ]]);
    $content = file_get_contents($base . $path, false, $context);
    $status = 0;
    $responseHeaders = [];
    foreach ($http_response_header as $line) {
        if (preg_match('/\AHTTP\/\S+ (\d+)/', $line, $match)) { $status = (int) $match[1]; }
        elseif (str_contains($line, ':')) {
            [$name, $value] = explode(':', $line, 2);
            $responseHeaders[strtolower($name)] = trim($value);
        }
    }
    // Дожидаемся завершения обработчика: счётчик завершений пишется после тела.
    // Стенд обслуживает запросы последовательно, поэтому health служит барьером.
    if ($path !== '/health') { http_request($base, '/health'); }
    return ['status' => $status, 'body' => $content, 'headers' => $responseHeaders];
}

$GLOBALS['passed'] = 0;
$dsn = getenv('SCRISELF_STATS_TEST_SERVER_DSN') ?: 'mysql:host=127.0.0.1;port=33316;charset=utf8mb4';
if (!preg_match('/\Amysql:host=(?:127\.0\.0\.1|localhost);port=[0-9]+;charset=utf8mb4\z/', $dsn)) {
    throw new RuntimeException('Use a disposable MySQL/MariaDB server on loopback; the test creates its own database.');
}
$admin = new PDO($dsn, getenv('SCRISELF_STATS_TEST_USER') ?: 'root', getenv('SCRISELF_STATS_TEST_PASSWORD') ?: '', [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION]);
$database = 'scriself_stats_test_' . bin2hex(random_bytes(6));
$admin->exec('CREATE DATABASE `' . $database . '` CHARACTER SET utf8mb4');
$db = new PDO($dsn . ';dbname=' . $database, getenv('SCRISELF_STATS_TEST_USER') ?: 'root', getenv('SCRISELF_STATS_TEST_PASSWORD') ?: '', [
    PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_EMULATE_PREPARES => false,
]);
$artifactRoot = dirname(__DIR__, 2) . '/artifacts/usage-statistics';
if (!is_dir($artifactRoot)) { mkdir($artifactRoot, 0755, true); }
$root = $artifactRoot . '/web-test-' . bin2hex(random_bytes(4));
mkdir($root . '/uploads/scrisoft', 0755, true);
mkdir($root . '/cache', 0755, true);
$server = null;
try {
    test('strict payload and no personal fields', static function (): void {
        $valid = ['installation_id' => '12345678-1234-4abc-8abc-000000000001', 'version' => '1.1.12', 'platform' => 'win-x64'];
        expect(app_stats_payload($valid) !== null, 'Valid payload rejected');
        expect(!array_key_exists('installation_id', app_stats_payload($valid)), 'Raw ID must not be stored');
        $upper = $valid;
        $upper['installation_id'] = strtoupper($upper['installation_id']);
        expect(app_stats_payload($valid) === app_stats_payload($upper), 'Case must not split installations');
        foreach ([['email' => 'x@example.test'], ['installation_id' => []], ['installation_id' => '00000000-0000-0000-0000-000000000000'], ['version' => '<script>'], ['version' => str_repeat('1', 41)], ['platform' => 'unknown']] as $invalid) {
            expect(app_stats_payload(array_replace($valid, $invalid)) === null, 'Invalid activity accepted');
        }
    });
    test('schema is repeatable and an empty summary contains zeros', static function () use ($db): void {
        app_stats_schema($db);
        app_stats_schema($db);
        $summary = app_stats_summary($db, '2026-10-30');
        expect($summary['total_count'] === 0 && $summary['day_count'] === 0 && $summary['inactive_count'] === 0, 'Wrong empty counts');
        expect(count($summary['days']) === 30 && $summary['download_totals']['completed'] === 0, 'Wrong empty series');
    });
    test('daily and period activity count distinct installations', static function () use ($db): void {
        app_stats_activity($db, activity(), '2026-10-01 00:00:00');
        app_stats_activity($db, activity(), '2026-10-30 09:00:00');
        for ($i = 0; $i < 10; $i++) { app_stats_activity($db, activity(), '2026-10-30 12:00:00'); }
        app_stats_activity($db, activity('2'), '2026-10-24 00:00:00');
        app_stats_activity($db, activity('3'), '2026-10-01 00:00:00');
        app_stats_activity($db, activity('4'), '2026-09-30 23:59:59');
        $summary = app_stats_summary($db, '2026-10-30');
        expect([$summary['day_count'], $summary['week_count'], $summary['month_count'], $summary['total_count'], $summary['inactive_count']] === [1, 2, 3, 4, 1], 'Duplicate or boundary counted incorrectly');
        expect($summary['days'][0]['installations'] === 1 && $summary['days'][29]['installations'] === 2, 'Daily series wrong');
    });
    test('version changes reuse the installation and older concurrent signals cannot regress last seen', static function () use ($db): void {
        app_stats_activity($db, activity('1', '1.1.13'), '2026-10-30 13:00:00');
        app_stats_activity($db, activity('1', '1.1.11'), '2026-10-30 11:00:00');
        $stmt = $db->prepare('SELECT version, last_seen FROM app_installations WHERE installation_hash=?');
        $stmt->execute([activity()['installation_hash']]);
        $row = $stmt->fetch(PDO::FETCH_ASSOC);
        expect($row['version'] === '1.1.13' && $row['last_seen'] === '2026-10-30 13:00:00', 'Latest version regressed');
        expect((int) $db->query('SELECT COUNT(*) FROM app_installations')->fetchColumn() === 4, 'Version update created an installation');
    });
    test('activity is transactional when the daily insert fails', static function () use ($db): void {
        $db->exec("CREATE TRIGGER stats_test_failure BEFORE INSERT ON app_activity_daily FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'test failure'");
        try {
            try { app_stats_activity($db, activity('99'), '2026-10-30 12:00:00'); throw new RuntimeException('Expected database failure'); }
            catch (PDOException) {}
            $stmt = $db->prepare('SELECT COUNT(*) FROM app_installations WHERE installation_hash=?');
            $stmt->execute([activity('99')['installation_hash']]);
            expect((int) $stmt->fetchColumn() === 0 && !$db->inTransaction(), 'Partial installation left after failure');
        } finally { $db->exec('DROP TRIGGER stats_test_failure'); }
    });
    test('download attempts, completions, sources and historical totals are separate', static function () use ($db): void {
        $file = 'ScriSelfStudio-Setup-1.1.12.exe';
        app_stats_download($db, $file, 'updater', false, '2026-10-30');
        app_stats_download($db, $file, 'updater', false, '2026-10-30');
        app_stats_download($db, $file, 'updater', true, '2026-10-30');
        app_stats_download($db, $file, 'website', false, '2026-09-01');
        app_stats_download($db, $file, 'website', true, '2026-09-01');
        $summary = app_stats_summary($db, '2026-10-30');
        expect($summary['download_totals'] === ['started' => 3, 'completed' => 2, 'started_30d' => 2, 'completed_30d' => 1, 'updater_completed' => 1], 'Incorrect download totals');
        expect(count($summary['downloads']) === 2, 'Sources merged');
    });
    test('rate limiter is bounded and resets after its window', static function () use ($root): void {
        $cache = $root . '/rate-test';
        for ($i = 0; $i < 120; $i++) { expect(app_stats_rate_allowed($cache, '192.0.2.1', 1000000), 'Throttled too early'); }
        expect(!app_stats_rate_allowed($cache, '192.0.2.1', 1000000), 'Flood not throttled');
        expect(app_stats_rate_allowed($cache, '192.0.2.1', 1000060), 'Window did not reset');
        expect(count(glob($cache . '/.app-rate-*')) === 1, 'One client created too many files');
        expect(!str_contains(file_get_contents(glob($cache . '/.app-rate-*')[0]), '192.0.2.1'), 'Rate limiter stored IP');
    });

    $filename = 'ScriSelfStudio-Setup-1.1.12.exe';
    $payload = random_bytes(32768);
    file_put_contents($root . '/uploads/scrisoft/' . $filename, $payload);
    file_put_contents($root . '/uploads/scrisoft/ScriSelfStudio-Setup-1.1.11.exe', $payload);
    file_put_contents($root . '/uploads/scrisoft/ScriSelfStudio-1.1.12-osx-arm64.zip', $payload);
    file_put_contents($root . '/uploads/scrisoft/latest.json', json_encode(['version' => '1.1.12', 'file' => $filename]));
    file_put_contents($root . '/uploads/scrisoft/latest-osx-arm64.json', json_encode(['version' => '1.1.12', 'file' => 'ScriSelfStudio-1.1.12-osx-arm64.zip']));
    test('only current manifest files are exposed to the updater', static function () use ($root, $filename): void {
        expect(app_update_file($root, $filename) !== null, 'Current Windows update rejected');
        expect(app_update_file($root, 'ScriSelfStudio-1.1.12-osx-arm64.zip') !== null, 'Current Mac update rejected');
        foreach (['ScriSelfStudio-Setup-1.1.11.exe', '../config.php', '..\\config.php', '%2e%2e%2fconfig.php', 'secret.exe', 'ScriSelfStudio-Setup-1.1.12.exe/../config.php'] as $name) {
            expect(app_update_file($root, $name) === null, 'Unpublished file exposed: ' . $name);
        }
    });

    foreach (['index.php', 'admin.php', 'app-activity.php', 'update-download.php', 'vk_config.php', 'modules/vk.php', 'modules/download.php', 'modules/app_statistics.php', 'modules/app_download.php', 'modules/admin_app_statistics.php', 'modules/admin_header.php', 'modules/admin_downloads.php', 'modules/admin_dashboard.php', 'assets/css/app-statistics.css', 'uploads/scrisoft/.htaccess'] as $relative) {
        if (!is_dir(dirname($root . '/' . $relative))) { mkdir(dirname($root . '/' . $relative), 0755, true); }
        copy(dirname(__DIR__) . '/' . $relative, $root . '/' . $relative);
    }
    copy(__DIR__ . '/fixtures/config.php', $root . '/config.php');
    copy(__DIR__ . '/fixtures/router.php', $root . '/router.php');
    $db->exec('CREATE TABLE downloads (id INT PRIMARY KEY, file_url TEXT, is_public TINYINT, download_count BIGINT DEFAULT 0, os VARCHAR(20), version VARCHAR(40), released_at DATETIME, file_size VARCHAR(80), checksum_sha256 CHAR(64), changelog TEXT)');
    $stmt = $db->prepare('INSERT INTO downloads (id,file_url,is_public,os,version,released_at) VALUES (?,?,?,\'Windows\',\'1.1.12\',NOW())');
    $stmt->execute([1, '/uploads/scrisoft/' . $filename, 1]);
    $stmt->execute([2, 'https://scriself.com/uploads/scrisoft/' . $filename, 1]);
    $stmt->execute([3, $filename, 1]);
    $stmt->execute([4, '/uploads/scrisoft/missing.exe', 1]);
    $stmt->execute([5, '/uploads/scrisoft/' . $filename, 0]);
    $stmt->execute([6, 'https://example.test/uploads/scrisoft/' . $filename, 1]);
    $stmt->execute([7, '/uploads/scrisoft/../../config.php', 1]);
    putenv('SCRISELF_STATS_TEST_FIXTURE=1');
    putenv('SCRISELF_STATS_TEST_DSN=' . $dsn . ';dbname=' . $database);
    $socket = stream_socket_server('tcp://127.0.0.1:0');
    $address = stream_socket_get_name($socket, false);
    fclose($socket);
    $base = 'http://' . $address;
    $server = proc_open([PHP_BINARY, '-d', 'extension_dir=' . ini_get('extension_dir'), '-d', 'extension=pdo_mysql', '-d', 'display_errors=0', '-S', $address, '-t', $root, $root . '/router.php'], [0 => ['pipe', 'r'], 1 => ['file', $root . '/http.log', 'a'], 2 => ['file', $root . '/http.log', 'a']], $pipes);
    fclose($pipes[0]);
    for ($i = 0; $i < 50; $i++) {
        $ready = @fsockopen(explode(':', $address)[0], (int) explode(':', $address)[1]);
        if ($ready !== false) { fclose($ready); break; }
        usleep(100000);
    }
    expect(http_request($base, '/health')['body'] === 'ready', 'Test HTTP server did not start');
    require __DIR__ . '/statistics_http_test.php';
    echo 'All ' . $GLOBALS['passed'] . " scenarios passed.\n";
    echo 'HTTP fixture and logs: ' . $root . "\n";
} finally {
    if (is_resource($server)) { proc_terminate($server); proc_close($server); }
    $db = null;
    $admin->exec('DROP DATABASE `' . $database . '`');
}
