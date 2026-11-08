<?php
declare(strict_types=1);

// Случайный идентификатор установки не связан с аккаунтами, IP или содержимым проектов.
function app_stats_schema(PDO $db): void
{
    $db->exec('CREATE TABLE IF NOT EXISTS app_installations (
        installation_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin PRIMARY KEY,
        first_seen DATETIME NOT NULL, last_seen DATETIME NOT NULL,
        version VARCHAR(40) NOT NULL, platform VARCHAR(20) NOT NULL,
        KEY app_last_seen (last_seen)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4');
    $db->exec('CREATE TABLE IF NOT EXISTS app_activity_daily (
        activity_day DATE NOT NULL,
        installation_hash CHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
        version VARCHAR(40) NOT NULL, platform VARCHAR(20) NOT NULL,
        PRIMARY KEY (activity_day, installation_hash)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4');
    $db->exec('CREATE TABLE IF NOT EXISTS app_download_daily (
        download_day DATE NOT NULL, file_name VARCHAR(200) NOT NULL,
        source VARCHAR(20) NOT NULL, started BIGINT UNSIGNED NOT NULL DEFAULT 0,
        completed BIGINT UNSIGNED NOT NULL DEFAULT 0,
        PRIMARY KEY (download_day, file_name, source)
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4');
}

function app_stats_payload(array $payload): ?array
{
    $id = $payload['installation_id'] ?? null;
    $version = $payload['version'] ?? null;
    $platform = $payload['platform'] ?? null;
    if (!is_string($id) || !preg_match('/\A[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\z/i', $id)
        || !is_string($version) || strlen($version) > 40
        || !preg_match('/\A[0-9]+(?:\.[0-9]+){1,3}\z/', $version)
        || !in_array($platform, ['win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64'], true)
        || count($payload) !== 3) {
        return null;
    }
    return ['installation_hash' => hash('sha256', strtolower($id)), 'version' => $version, 'platform' => $platform];
}

function app_stats_activity(PDO $db, array $activity, string $now): void
{
    $db->beginTransaction();
    try {
        $db->prepare('INSERT INTO app_installations (installation_hash, first_seen, last_seen, version, platform)
            VALUES (?, ?, ?, ?, ?) ON DUPLICATE KEY UPDATE
            version=IF(VALUES(last_seen) >= last_seen, VALUES(version), version),
            platform=IF(VALUES(last_seen) >= last_seen, VALUES(platform), platform),
            first_seen=LEAST(first_seen, VALUES(first_seen)), last_seen=GREATEST(last_seen, VALUES(last_seen))')
            ->execute([$activity['installation_hash'], $now, $now, $activity['version'], $activity['platform']]);
        $db->prepare('INSERT INTO app_activity_daily (activity_day, installation_hash, version, platform)
            VALUES (?, ?, ?, ?) ON DUPLICATE KEY UPDATE version=VALUES(version), platform=VALUES(platform)')
            ->execute([substr($now, 0, 10), $activity['installation_hash'], $activity['version'], $activity['platform']]);
        $db->commit();
    } catch (Throwable $error) {
        $db->rollBack();
        throw $error;
    }
}

function app_stats_download(PDO $db, string $file, string $source, bool $completed, string $day): void
{
    if (!in_array($source, ['updater', 'website', 'direct'], true)
        || $file === '' || strpbrk($file, "/\\\r\n\0") !== false || strlen($file) > 200) {
        throw new InvalidArgumentException('Invalid download');
    }
    $db->prepare('INSERT INTO app_download_daily (download_day, file_name, source, started, completed)
        VALUES (?, ?, ?, ?, ?) ON DUPLICATE KEY UPDATE started=started+VALUES(started), completed=completed+VALUES(completed)')
        ->execute([$day, $file, $source, $completed ? 0 : 1, $completed ? 1 : 0]);
}

// Неудача счётчика никогда не должна прерывать выдачу установщика.
function app_stats_record_download(string $file, string $source, bool $completed, string $day): void
{
    try {
        static $ready = false;
        if (!$ready) {
            app_stats_schema(db());
            $ready = true;
        }
        app_stats_download(db(), $file, $source, $completed, $day);
    } catch (Throwable $error) {
        error_log('App download statistics unavailable: ' . $error->getMessage());
    }
}

function app_stats_summary(PDO $db, string $today): array
{
    $date = new DateTimeImmutable($today, new DateTimeZone('UTC'));
    $week = $date->modify('-6 days')->format('Y-m-d');
    $month = $date->modify('-29 days')->format('Y-m-d');
    $stmt = $db->prepare('SELECT COUNT(DISTINCT CASE WHEN activity_day = ? THEN installation_hash END) AS day_count,
        COUNT(DISTINCT CASE WHEN activity_day >= ? THEN installation_hash END) AS week_count,
        COUNT(DISTINCT installation_hash) AS month_count FROM app_activity_daily WHERE activity_day BETWEEN ? AND ?');
    $stmt->execute([$today, $week, $month, $today]);
    $summary = array_map('intval', $stmt->fetch(PDO::FETCH_ASSOC));
    $summary['total_count'] = (int) $db->query('SELECT COUNT(*) FROM app_installations')->fetchColumn();
    $summary['inactive_count'] = max(0, $summary['total_count'] - $summary['month_count']);
    $stmt = $db->prepare('SELECT version, platform, COUNT(*) AS installations FROM app_installations
        WHERE last_seen >= ? AND last_seen < ? GROUP BY version, platform ORDER BY installations DESC, version DESC');
    $stmt->execute([$month . ' 00:00:00', $date->modify('+1 day')->format('Y-m-d') . ' 00:00:00']);
    $summary['versions'] = $stmt->fetchAll(PDO::FETCH_ASSOC);
    $stmt = $db->prepare('SELECT activity_day, COUNT(*) AS installations FROM app_activity_daily
        WHERE activity_day BETWEEN ? AND ? GROUP BY activity_day ORDER BY activity_day DESC');
    $stmt->execute([$month, $today]);
    $days = $stmt->fetchAll(PDO::FETCH_KEY_PAIR);
    $summary['days'] = [];
    for ($day = $date; $day->format('Y-m-d') >= $month; $day = $day->modify('-1 day')) {
        $key = $day->format('Y-m-d');
        $summary['days'][] = ['activity_day' => $key, 'installations' => (int) ($days[$key] ?? 0)];
    }
    $stmt = $db->prepare('SELECT file_name, source, SUM(started) AS started, SUM(completed) AS completed,
        SUM(CASE WHEN download_day >= ? THEN started ELSE 0 END) AS started_30d,
        SUM(CASE WHEN download_day >= ? THEN completed ELSE 0 END) AS completed_30d
        FROM app_download_daily WHERE download_day <= ? GROUP BY file_name, source ORDER BY started_30d DESC, started DESC, file_name, source');
    $stmt->execute([$month, $month, $today]);
    $summary['downloads'] = $stmt->fetchAll(PDO::FETCH_ASSOC);
    $summary['download_totals'] = ['started' => 0, 'completed' => 0, 'started_30d' => 0, 'completed_30d' => 0, 'updater_completed' => 0];
    foreach ($summary['downloads'] as $row) {
        foreach (['started', 'completed', 'started_30d', 'completed_30d'] as $key) {
            $summary['download_totals'][$key] += (int) $row[$key];
        }
        if ($row['source'] === 'updater') {
            $summary['download_totals']['updater_completed'] += (int) $row['completed'];
        }
    }
    return $summary;
}

// Канал обновлений открыт для файлов из текущих манифестов.
// Старые публичные сборки доступны через каталог сайта и его счётчик загрузок.
function app_update_file(string $root, string $name): ?string
{
    if (strlen($name) > 200 || !preg_match('/\A(?:ScriSelfStudio-Setup-[0-9]+(?:\.[0-9]+){1,3}\.exe|ScriSelfStudio-[0-9]+(?:\.[0-9]+){1,3}-osx-(?:arm64|x64)\.(?:zip|dmg))\z/', $name)) {
        return null;
    }
    $directory = realpath($root . '/uploads/scrisoft');
    $file = realpath($root . '/uploads/scrisoft/' . $name);
    if ($directory === false || $file === false || dirname($file) !== $directory || !is_file($file) || !is_readable($file)) {
        return null;
    }
    foreach (['latest.json', 'latest-osx-arm64.json', 'latest-osx-x64.json'] as $manifestName) {
        $manifest = json_decode((string) @file_get_contents($directory . '/' . $manifestName), true);
        if (is_array($manifest) && ($manifest['file'] ?? $manifest['file_name'] ?? $manifest['setup'] ?? null) === $name) {
            return $file;
        }
    }
    return null;
}

// 4096 корзин, короткое окно, блокировка файла для параллельных запросов.
// IP используется только для выбора корзины и не записывается в файл.
function app_stats_rate_allowed(string $cache, string $ip, int $now): bool
{
    if (!is_dir($cache) && !@mkdir($cache, 0755, true) && !is_dir($cache)) {
        return false;
    }
    $digest = hash('sha256', gmdate('Y-m-d', $now) . ':' . $ip);
    $path = $cache . '/.app-rate-' . substr($digest, 0, 3);
    $handle = @fopen($path, 'c+');
    if (!$handle) { return false; }
    try {
        if (!flock($handle, LOCK_EX)) { return false; }
        $state = json_decode((string) stream_get_contents($handle), true);
        if (!is_array($state) || !isset($state['start'], $state['count'])
            || !is_int($state['start']) || !is_int($state['count'])
            || $state['start'] > $now || $state['start'] <= $now - 60) {
            $state = ['start' => $now, 'count' => 0];
        }
        $state['count'] = min(121, $state['count'] + 1);
        rewind($handle);
        ftruncate($handle, 0);
        fwrite($handle, json_encode($state, JSON_THROW_ON_ERROR));
        return $state['count'] <= 120;
    } finally {
        flock($handle, LOCK_UN);
        fclose($handle);
    }
}
