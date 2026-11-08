<?php
declare(strict_types=1);

// Только для локального стенда тестов. В комплект для сайта этот файл не входит.
if (getenv('SCRISELF_STATS_TEST_FIXTURE') !== '1') { http_response_code(403); exit; }
define('SITE_URL', 'https://scriself.com');
define('CACHE_TTL', 0);

function db(): PDO
{
    if (($_SERVER['HTTP_X_TEST_DATABASE'] ?? '') === 'offline') { throw new RuntimeException('Test database outage'); }
    static $db;
    return $db ??= new PDO(getenv('SCRISELF_STATS_TEST_DSN'), getenv('SCRISELF_STATS_TEST_USER') ?: 'root', getenv('SCRISELF_STATS_TEST_PASSWORD') ?: '', [
        PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION,
        PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC,
        PDO::ATTR_EMULATE_PREPARES => false,
    ]);
}
function db_ok(): bool { try { db(); return true; } catch (Throwable) { return false; } }
function e(mixed $value): string { return htmlspecialchars((string) $value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8'); }
function current_path(): string { return (string) parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH); }
function ensure_schema(): void {}
function start_secure_session(): void {}
function is_admin(): bool { return ($_SERVER['HTTP_X_TEST_ADMIN'] ?? '') === 'yes'; }
function csrf_token(): string { return 'fixture-csrf'; }
function check_csrf(): void { if (($_POST['csrf'] ?? '') !== csrf_token()) { http_response_code(419); exit; } }
function clear_cache(): void {}
function json_response(array $data, int $code = 200): never
{
    http_response_code($code);
    header('Content-Type: application/json');
    echo json_encode($data, JSON_THROW_ON_ERROR);
    exit;
}
function downloads_public(): array { return db()->query('SELECT * FROM downloads WHERE is_public=1 ORDER BY released_at DESC,id')->fetchAll(); }
function fmt_date(?string $value): string { return $value ? date('d.m.Y', strtotime($value)) : ''; }
function view(string $name, array $data): void
{
    if ($name !== 'download') { echo e($name); return; }
    extract($data, EXTR_SKIP);
    require __DIR__ . '/modules/download.php';
}
function admin_view(string $name, array $data): void
{
    if ($name === 'login') { echo 'Admin login required'; return; }
    extract($data, EXTR_SKIP);
    require __DIR__ . '/modules/admin_header.php';
    require __DIR__ . '/modules/admin_' . $name . '.php';
    echo '</div></main></body></html>';
}
