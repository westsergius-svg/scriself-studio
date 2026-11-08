<?php

declare(strict_types=1);

require __DIR__ . '/config.php';
require __DIR__ . '/modules/app_download.php';

// Проверка: если install.php существует И БД инициализирована — ошибка
if (file_exists(__DIR__ . '/install.php') && db_ok()) {
    trigger_error('SECURITY: install.php still exists on a initialized database. Delete it immediately!', E_USER_WARNING);
}

$path = current_path();
// Старые ссылки входа больше не запускают OAuth и ведут на обычное скачивание.
if (str_starts_with($path, '/vk/')) {
    foreach (['scriself_vk_member', 'scriself_vk_oauth'] as $cookie) {
        setcookie($cookie, '', ['expires' => 1, 'path' => '/', 'httponly' => true, 'samesite' => 'Lax']);
    }
    header('Cache-Control: no-store');
    header('Location: /download', true, 303);
    exit;
}
$queryString = (string) ($_SERVER['QUERY_STRING'] ?? '');
$liveDownloadPage = in_array($path, ['/download', '/en/download'], true);
if ($liveDownloadPage) {
    header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0', true);
    header('Pragma: no-cache', true);
    header('Expires: 0', true);
}
$cacheable = ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'GET'
    && !str_starts_with($path, '/admin')
    && !str_starts_with($path, '/api/')
    && !str_starts_with($path, '/download/file/')
    && !$liveDownloadPage
    && !in_array($path, ['/robots.txt', '/sitemap.xml'], true);
$cacheRevisionFiles = [
    __DIR__ . '/modules/header.php',
    __DIR__ . '/modules/footer.php',
    __DIR__ . '/modules/home.php',
    __DIR__ . '/modules/features.php',
    __DIR__ . '/modules/blog_post.php',
];
$cacheRevision = implode(':', array_map(
    static fn(string $file): string => is_file($file) ? (string) filemtime($file) : '0',
    $cacheRevisionFiles
));
$cacheFile = __DIR__ . '/cache/' . sha1($path . '?' . $queryString . ':' . $cacheRevision) . '.html';

if ($cacheable && is_file($cacheFile) && filemtime($cacheFile) + CACHE_TTL > time()) {
    header('Content-Type: text/html; charset=utf-8');
    readfile($cacheFile);
    exit;
}

if ($cacheable) {
    ob_start();
    register_shutdown_function(static function () use ($cacheFile): void {
        $html = ob_get_contents();
        if ($html === false || $html === '') {
            return;
        }
        if (!is_dir(__DIR__ . '/cache')) {
            mkdir(__DIR__ . '/cache', 0755, true);
        }
        file_put_contents($cacheFile, $html);
    });
}

function route_slug(string $slug): string
{
    return rawurldecode($slug);
}

/** Обработка отправки комментария на странице поста. */
function handle_comment_post(array $post): void
{
    if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'POST' || !db_ok()) {
        return;
    }
    try {
        $postId = (int) ($post['id'] ?? 0);
        if ($postId <= 0) {
            return;
        }
        if (trim((string) ($_POST['website'] ?? '')) !== '') {
            http_response_code(403);
            exit;
        }
        if (!comment_token_valid((string) ($_POST['token'] ?? ''))) {
            header('Location: ' . post_url($post) . '?comment=error');
            exit;
        }
        if (comment_recent_blocked()) {
            header('Location: ' . post_url($post) . '?comment=error');
            exit;
        }
        $name = trim((string) ($_POST['name'] ?? ''));
        $email = trim((string) ($_POST['email'] ?? ''));
        $content = trim((string) ($_POST['content'] ?? ''));
        if ($name === '' || !filter_var($email, FILTER_VALIDATE_EMAIL) || $content === '' || mb_strlen($content) > 2000 || mb_strlen($name) > 120) {
            header('Location: ' . post_url($post) . '?comment=error');
            exit;
        }
        save_comment($postId, $name, $email, $content);
        clear_cache();
        header('Location: ' . post_url($post) . '?comment=pending');
        exit;
    } catch (Throwable) {
        header('Location: ' . post_url($post) . '?comment=error');
        exit;
    }
}

try {
    /* Публичная загрузка с сайта: выдача файла через счётчик, без входа. */
    if (preg_match('#^/download/file/(\d+)$#', $path, $m)) {
        if (!in_array($_SERVER['REQUEST_METHOD'] ?? 'GET', ['GET', 'HEAD'], true)) {
            header('Allow: GET, HEAD');
            http_response_code(405);
            exit;
        }
        $downloadId = (int) $m[1];
        $fileUrl = '';
        if (db_ok()) {
            ensure_schema();
            try {
                $stmt = db()->prepare('SELECT file_url FROM downloads WHERE id = ? AND is_public = 1 LIMIT 1');
                $stmt->execute([$downloadId]);
                $row = $stmt->fetch();
                $fileUrl = trim((string) ($row['file_url'] ?? ''));
            } catch (Throwable $e) {
                error_log('download counter failed: ' . $e->getMessage());
            }
        }
        if ($fileUrl !== '') {
            // Относительные пути приводим к абсолютным, иначе редирект уедет в /download/file/...
            if (!preg_match('#^(https?:)?//#i', $fileUrl) && $fileUrl[0] !== '/') {
                $inUploads = '/uploads/scrisoft/' . $fileUrl;
                $fileUrl = is_file(__DIR__ . $inUploads) ? $inUploads : '/' . $fileUrl;
            }

            $urlParts = parse_url($fileUrl);
            $downloadHost = strtolower((string) ($urlParts['host'] ?? ''));
            $siteHost = strtolower((string) parse_url(SITE_URL, PHP_URL_HOST));
            $downloadPath = (string) ($urlParts['path'] ?? '');
            if (
                ($downloadHost === '' || $downloadHost === $siteHost)
                && in_array(strtolower((string) ($urlParts['scheme'] ?? '')), ['', 'https', 'http'], true)
                && str_starts_with($downloadPath, '/uploads/scrisoft/')
            ) {
                $uploadsRoot = realpath(__DIR__ . '/uploads/scrisoft');
                $localFile = realpath(__DIR__ . rawurldecode($downloadPath));
                if (
                    $uploadsRoot === false
                    || $localFile === false
                    || !str_starts_with($localFile, $uploadsRoot . DIRECTORY_SEPARATOR)
                    || !is_file($localFile)
                ) {
                    error_log('Download file is missing or outside the downloads directory: ' . $downloadPath);
                    http_response_code(404);
                    view('404', ['title' => 'Файл не найден', 'description' => 'Такой сборки нет.', 'active' => '']);
                    exit;
                }

                app_send_download($localFile, 'website', static function () use ($downloadId): void {
                    db()->prepare('UPDATE downloads SET download_count = download_count + 1 WHERE id = ?')->execute([$downloadId]);
                });
            }

            error_log('Blocked external download URL for download id ' . $downloadId);
            http_response_code(503);
            exit('Файл этой версии временно недоступен. Попробуйте позже.');
        }
        http_response_code(404);
        view('404', ['title' => 'Файл не найден', 'description' => 'Такой сборки нет.', 'active' => '']);
        exit;
    }

    if (in_array($path, ['/', '/en'], true)) {
        $lang = $path === '/en' ? 'en' : 'ru';
        view('home', [
            'title' => $lang === 'en' ? 'ScriSelf Studio — book planning and writing software' : 'ScriSelf Studio — программа для планирования и написания книг',
            'description' => $lang === 'en'
                ? 'Plan books, write and organize scenes, develop characters and plotlines, and export manuscripts to DOCX, PDF, EPUB, TXT, or Markdown. Works offline; optional cloud storage is manual.'
                : 'Планируйте структуру книги, пишите сцены, ведите карточки персонажей и сюжетные линии. Экспортируйте рукопись в DOCX, PDF, EPUB, TXT или Markdown. Работа офлайн; облако — для ручного переноса проекта.',
            'active' => 'home',
            'hero' => true,
            'lang' => $lang,
            'schema' => [
                '@context' => 'https://schema.org',
                '@graph' => [
                    ['@type' => 'Organization', 'name' => SITE_NAME, 'url' => SITE_URL . '/'],
                    ['@type' => 'WebSite', 'name' => SITE_NAME, 'url' => SITE_URL . '/', 'inLanguage' => $lang],
                    ['@type' => 'SoftwareApplication', 'name' => SITE_NAME, 'operatingSystem' => 'Windows', 'applicationCategory' => 'WritingApplication', 'offers' => ['@type' => 'Offer', 'price' => '0', 'priceCurrency' => 'RUB']],
                ],
            ],
        ]);
        exit;
    }

    if (in_array($path, ['/features', '/en/features'], true)) {
        $lang = $path === '/en/features' ? 'en' : 'ru';
        view('features', ['title' => $lang === 'en' ? 'ScriSelf features' : 'Возможности ScriSelf', 'description' => $lang === 'en' ? 'Tools for structure, scenes, characters and export.' : 'Инструменты для структуры книги, сцен, персонажей и экспорта.', 'active' => 'features', 'lang' => $lang]);
        exit;
    }

    if (in_array($path, ['/download', '/en/download'], true)) {
        $lang = $path === '/en/download' ? 'en' : 'ru';
        $downloads = db_ok() ? downloads_public() : [];
        view('download', [
            'title' => $lang === 'en' ? 'Download ScriSelf' : 'Скачать ScriSelf',
            'description' => $lang === 'en' ? 'Download the free ScriSelf app for Windows, Android, Linux and macOS.' : 'Скачать бесплатную программу ScriSelf для Windows, Android, Linux и macOS.',
            'active' => 'download',
            'downloads' => $downloads,
            'latest' => $downloads[0] ?? null,
            'lang' => $lang,
        ]);
        exit;
    }

    if (preg_match('#^/(?:en/)?(blog|guides)$#', $path, $m)) {
        $section = $m[1];
        $lang = str_starts_with($path, '/en/') ? 'en' : 'ru';
        $page = max(1, (int) ($_GET['page'] ?? 1));
        $perPage = 12;
        $offset = ($page - 1) * $perPage;
        if ($section === 'blog') {
            $total = db_ok() ? count_posts($lang) : 0;
            $posts = db_ok() ? latest_posts($lang, $perPage, $offset) : [];
        } else {
            $total = db_ok() ? count_posts_by_type('guide', $lang) : 0;
            $posts = db_ok() ? posts_by_type('guide', $lang, $perPage, $offset) : [];
        }
        $pages = max(1, (int) ceil($total / $perPage));
        view('blog_list', [
            'title' => $lang === 'en' ? ($section === 'blog' ? 'ScriSelf Blog' : 'Writing guides') : ($section === 'blog' ? 'Блог ScriSelf' : 'Гайды для писателей'),
            'description' => $lang === 'en' ? ($section === 'blog' ? 'Articles about writing and ScriSelf.' : 'Step-by-step guides to work with ScriSelf.') : ($section === 'blog' ? 'Статьи о писательстве и работе с платформой ScriSelf.' : 'Пошаговые руководства по работе с ScriSelf.'),
            'active' => $section === 'blog' ? 'blog' : 'guides',
            'lang' => $lang,
            'section' => $section,
            'posts' => $posts,
            'page' => $page,
            'pages' => $pages,
        ]);
        exit;
    }

    /* --- Детальные страницы постов --- */
    foreach ([
        '#^/blog/([^/]+)$#' => ['lang' => 'ru', 'active' => 'blog'],
        '#^/en/blog/([^/]+)$#' => ['lang' => 'en', 'active' => 'blog'],
        '#^/guides/([^/]+)$#' => ['lang' => 'ru', 'type' => 'guide', 'active' => 'guides'],
        '#^/en/guides/([^/]+)$#' => ['lang' => 'en', 'type' => 'guide', 'active' => 'guides'],
        '#^/features/([^/]+)$#' => ['lang' => 'ru', 'type' => 'program_page', 'active' => 'features'],
        '#^/en/features/([^/]+)$#' => ['lang' => 'en', 'type' => 'program_page', 'active' => 'features'],
        '#^/glossary/([^/]+)$#' => ['lang' => 'ru', 'type' => 'glossary', 'active' => 'glossary'],
        '#^/en/glossary/([^/]+)$#' => ['lang' => 'en', 'type' => 'glossary', 'active' => 'glossary'],
    ] as $pattern => $route) {
        if (preg_match($pattern, $path, $match)) {
            $slug = route_slug($match[1]);
            $post = isset($route['type'])
                ? post_by_slug_type($slug, $route['type'], $route['lang'])
                : post_by_slug($slug, $route['lang']);
            if ($post) {
                handle_comment_post($post);
                view('blog_post', [
                    'title' => $post['meta_title'] ?: $post['title'],
                    'description' => $post['meta_description'] ?: $post['excerpt'],
                    'active' => $route['active'],
                    'lang' => $post['lang'] ?? 'ru',
                    'post' => $post,
                ]);
                exit;
            }
        }
    }

    if ($path === '/faq' || $path === '/en/faq') {
        view('faq', ['title' => 'Частые вопросы о ScriSelf', 'description' => 'Ответы на частые вопросы о программе ScriSelf.', 'active' => 'faq', 'lang' => $path === '/en/faq' ? 'en' : 'ru']);
        exit;
    }

    if ($path === '/reviews' || $path === '/en/reviews') {
        view('reviews', ['title' => 'Отзывы о ScriSelf', 'description' => 'Что авторы говорят о ScriSelf.', 'active' => 'reviews', 'lang' => $path === '/en/reviews' ? 'en' : 'ru']);
        exit;
    }

    if ($path === '/glossary' || $path === '/en/glossary') {
        view('glossary', ['title' => 'Глоссарий писателя', 'description' => 'Термины и понятия для писателей.', 'active' => 'glossary', 'lang' => $path === '/en/glossary' ? 'en' : 'ru']);
        exit;
    }

    if ($path === '/search' || $path === '/en/search') {
        view('search', ['title' => 'Поиск по сайту', 'description' => 'Поиск статей, гайдов и терминов.', 'active' => '', 'lang' => $path === '/en/search' ? 'en' : 'ru']);
        exit;
    }

    if ($path === '/changelog' || $path === '/en/changelog') {
        $downloads = db_ok() ? downloads_public() : [];
        view('changelog', ['title' => $path === '/en/changelog' ? 'Changelog' : 'История версий', 'description' => 'История обновлений ScriSelf.', 'active' => 'download', 'lang' => $path === '/en/changelog' ? 'en' : 'ru', 'downloads' => $downloads]);
        exit;
    }

    if ($path === '/contacts' || $path === '/en/contacts') {
        view('contacts', ['title' => $path === '/en/contacts' ? 'Contact us' : 'Связаться с нами', 'description' => 'Контакты поддержки ScriSelf.', 'active' => '', 'lang' => $path === '/en/contacts' ? 'en' : 'ru']);
        exit;
    }

    if ($path === '/robots.txt') {
        header('Content-Type: text/plain; charset=utf-8');
        echo "User-agent: *\nAllow: /\nDisallow: /admin/\nDisallow: /cache/\nSitemap: " . SITE_URL . "/sitemap.xml\n";
        exit;
    }

    if ($path === '/sitemap.xml') {
        require __DIR__ . '/modules/sitemap.php';
        exit;
    }

    if ($path === '/api/v1/latest-version') {
        require __DIR__ . '/modules/api.php';
        exit;
    }

    $source = '/' . trim($path, '/') . '/';
    $stmt = db()->prepare('SELECT target_path, http_code FROM redirects WHERE source_path = ? AND is_active = 1 LIMIT 1');
    $stmt->execute([$source]);
    $redirect = $stmt->fetch();
    if ($redirect) {
        header('Location: ' . $redirect['target_path'], true, (int) $redirect['http_code']);
        exit;
    }
} catch (Throwable $ex) {
    if (str_starts_with($path, '/api/')) {
        json_response(['error' => 'server_error'], 500);
    }
}

http_response_code(404);
view('404', ['title' => 'Страница не найдена', 'description' => 'Такой страницы нет.', 'active' => '']);
