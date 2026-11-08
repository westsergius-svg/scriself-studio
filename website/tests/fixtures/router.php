<?php
declare(strict_types=1);

// Встроенный сервер PHP не читает .htaccess. Здесь моделируется только маршрут;
// сам .htaccess нужно дополнительно проверить на Apache при публикации.
$path = (string) parse_url($_SERVER['REQUEST_URI'], PHP_URL_PATH);
if (str_starts_with($path, '/uploads/')) {
    if (preg_match('#\A/uploads/scrisoft/(ScriSelfStudio-Setup-[0-9]+(?:\.[0-9]+){1,3}\.exe|ScriSelfStudio-[0-9]+(?:\.[0-9]+){1,3}-osx-(?:arm64|x64)\.(?:zip|dmg))\z#', $path, $match)) {
        $_GET['file'] = $match[1];
        require __DIR__ . '/update-download.php';
    }
    http_response_code(403);
    exit;
}
if (str_starts_with($path, '/download') || str_starts_with($path, '/vk/') || $path === '/en/download') { require __DIR__ . '/index.php'; exit; }
if ($path === '/health') { echo 'ready'; exit; }
return false;
