<?php

declare(strict_types=1);

require __DIR__ . '/config.php';
start_secure_session();

$action = $_GET['action'] ?? $_POST['action'] ?? 'dashboard';

if ($action === 'logout') {
    session_destroy();
    header('Location: /admin.php');
    exit;
}

if (!is_admin()) {
    $error = '';
    if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
        check_csrf();
        $email = trim((string) ($_POST['email'] ?? ''));
        $password = (string) ($_POST['password'] ?? '');
        $stmt = db()->prepare('SELECT * FROM users WHERE email = ? AND role IN ("admin","editor") LIMIT 1');
        $stmt->execute([$email]);
        $user = $stmt->fetch();
        if ($user && password_verify($password, $user['password_hash'])) {
            session_regenerate_id(true);
            $_SESSION['admin_id'] = $user['id'];
            $_SESSION['admin_name'] = $user['name'];
            header('Location: /admin.php');
            exit;
        }
        $error = 'Неверный логин или пароль.';
    }
    admin_view('login', ['title' => 'Вход', 'error' => $error]);
    exit;
}

if ($action === 'posts') {
    $filters = [
        'q'      => trim((string) ($_GET['q'] ?? '')),
        'lang'   => strtolower(trim((string) ($_GET['lang'] ?? ''))),
        'type'   => strtolower(trim((string) ($_GET['type'] ?? ''))),
        'status' => strtolower(trim((string) ($_GET['status'] ?? ''))),
        'sort'   => (string) ($_GET['sort'] ?? 'updated_at'),
        'dir'    => strtolower((string) ($_GET['dir'] ?? 'desc')) === 'asc' ? 'asc' : 'desc',
    ];

    $sortable = [
        'id'           => 'id',
        'title'        => 'title',
        'lang'         => 'lang',
        'type'         => 'type',
        'status'       => 'status',
        'published_at' => 'published_at',
        'updated_at'   => 'updated_at',
    ];
    $sortCol = $sortable[$filters['sort']] ?? 'updated_at';
    $dir     = $filters['dir'];

    $where  = [];
    $params = [];
    if ($filters['q'] !== '') {
        $where[]  = '(title LIKE ? OR slug LIKE ?)';
        $like     = '%' . $filters['q'] . '%';
        $params[] = $like;
        $params[] = $like;
    }
    if (in_array($filters['lang'], ['ru', 'en'], true)) {
        $where[]  = 'lang = ?';
        $params[] = $filters['lang'];
    }
    if (in_array($filters['type'], ['article', 'guide', 'program_page', 'faq', 'review', 'glossary'], true)) {
        $where[]  = 'type = ?';
        $params[] = $filters['type'];
    }
    if (in_array($filters['status'], ['draft', 'published', 'archived'], true)) {
        $where[]  = 'status = ?';
        $params[] = $filters['status'];
    }
    $whereSql = $where ? (' WHERE ' . implode(' AND ', $where)) : '';

    $countStmt = db()->prepare('SELECT COUNT(*) FROM posts' . $whereSql);
    $countStmt->execute($params);
    $total = (int) $countStmt->fetchColumn();

    $perPage = 30;
    $pages   = max(1, (int) ceil($total / $perPage));
    $page    = max(1, min($pages, (int) ($_GET['page'] ?? 1)));
    $offset  = ($page - 1) * $perPage;

    $stmt = db()->prepare(
        'SELECT id, title, slug, lang, type, status, published_at, updated_at FROM posts'
        . $whereSql
        . " ORDER BY {$sortCol} {$dir}, id DESC LIMIT {$perPage} OFFSET {$offset}"
    );
    $stmt->execute($params);
    $posts = $stmt->fetchAll();

    admin_view('posts', [
        'title'    => 'Посты',
        'posts'    => $posts,
        'filters'  => $filters,
        'sortable' => $sortable,
        'total'    => $total,
        'page'     => $page,
        'pages'    => $pages,
        'perPage'  => $perPage,
    ]);
    exit;
}

if ($action === 'edit') {
    $id = (int) ($_GET['id'] ?? 0);
    $post = ['id'=>0,'title'=>'','slug'=>'','lang'=>'ru','type'=>'article','status'=>'published','excerpt'=>'','content_html'=>'','meta_title'=>'','meta_description'=>'','published_at'=>date('Y-m-d H:i:s')];
    if ($id) {
        $stmt = db()->prepare('SELECT * FROM posts WHERE id = ?');
        $stmt->execute([$id]);
        $post = $stmt->fetch() ?: $post;
    }
    admin_view('edit', ['title' => $id ? 'Редактировать' : 'Новый пост', 'post' => $post]);
    exit;
}

if ($action === 'save' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $id = (int) ($_POST['id'] ?? 0);
    $title = trim((string) ($_POST['title'] ?? ''));
    $slug = trim((string) ($_POST['slug'] ?? '')) ?: slugify($title);
    $data = [
        $_POST['type'] ?? 'article',
        $_POST['lang'] ?? 'ru',
        $title,
        $slug,
        trim((string) ($_POST['excerpt'] ?? '')),
        (string) ($_POST['content_html'] ?? ''),
        $_POST['status'] ?? 'draft',
        trim((string) ($_POST['meta_title'] ?? '')),
        trim((string) ($_POST['meta_description'] ?? '')),
        trim((string) ($_POST['published_at'] ?? date('Y-m-d H:i:s'))),
    ];
    if ($id) {
        $stmt = db()->prepare('UPDATE posts SET type=?, lang=?, title=?, slug=?, excerpt=?, content_html=?, status=?, meta_title=?, meta_description=?, published_at=? WHERE id=?');
        $stmt->execute([...$data, $id]);
    } else {
        $stmt = db()->prepare('INSERT INTO posts (type, lang, title, slug, excerpt, content_html, status, meta_title, meta_description, published_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)');
        $stmt->execute($data);
    }
    clear_cache();
    header('Location: /admin.php?action=posts');
    exit;
}

if ($action === 'redirects') {
    $items = db()->query('SELECT * FROM redirects ORDER BY id DESC LIMIT 300')->fetchAll();
    admin_view('redirects', ['title' => 'Редиректы', 'items' => $items]);
    exit;
}

if ($action === 'save_redirect' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $stmt = db()->prepare('INSERT INTO redirects (source_path, target_path, http_code, source) VALUES (?, ?, ?, "manual") ON DUPLICATE KEY UPDATE target_path=VALUES(target_path), http_code=VALUES(http_code), is_active=1');
    $stmt->execute([trim((string) $_POST['source_path']), trim((string) $_POST['target_path']), (int) $_POST['http_code'] ?: 301]);
    clear_cache();
    header('Location: /admin.php?action=redirects');
    exit;
}

if ($action === 'delete' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $id = (int) ($_POST['id'] ?? 0);
    if ($id > 0) {
        try {
            db()->prepare('DELETE FROM posts WHERE id = ?')->execute([$id]);
            db()->prepare('DELETE FROM comments WHERE post_id = ?')->execute([$id]);
            db()->prepare('DELETE FROM post_tags WHERE post_id = ?')->execute([$id]);
            clear_cache();
            header('Location: /admin.php?action=posts&deleted=1');
            exit;
        } catch (Throwable $e) {
            error_log('Error deleting post: ' . $e->getMessage());
        }
    }
    header('Location: /admin.php?action=posts');
    exit;
}

if ($action === 'app_statistics') {
    require __DIR__ . '/modules/app_statistics.php';
    $statistics = [];
    $statisticsError = false;
    try {
        app_stats_schema(db());
        $statistics = app_stats_summary(db(), gmdate('Y-m-d'));
    } catch (Throwable $error) {
        $statisticsError = true;
        error_log('Admin app statistics unavailable: ' . $error->getMessage());
    }
    admin_view('app_statistics', ['title' => 'Использование программы', 'statistics' => $statistics, 'statisticsError' => $statisticsError]);
    exit;
}

if ($action === 'downloads') {
    ensure_schema();
    $items = db()->query('SELECT * FROM downloads ORDER BY released_at DESC, id DESC LIMIT 300')->fetchAll();
    $totalDownloads = (int) db()->query('SELECT COALESCE(SUM(download_count), 0) FROM downloads')->fetchColumn();
    $editDownload = null;
    $editId = (int) ($_GET['edit'] ?? 0);
    if ($editId > 0) {
        $stmt = db()->prepare('SELECT * FROM downloads WHERE id=?');
        $stmt->execute([$editId]);
        $editDownload = $stmt->fetch() ?: null;
    }
    admin_view('downloads', ['title' => 'Версии программы', 'items' => $items, 'totalDownloads' => $totalDownloads, 'editDownload' => $editDownload]);
    exit;
}

if ($action === 'save_download' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $id = (int) ($_POST['id'] ?? 0);
    $data = [
        trim((string) ($_POST['os'] ?? '')),
        trim((string) ($_POST['version'] ?? '')),
        trim((string) ($_POST['file_url'] ?? '')),
        trim((string) ($_POST['file_size'] ?? '')),
        trim((string) ($_POST['checksum_sha256'] ?? '')),
        trim((string) ($_POST['changelog'] ?? '')),
        !empty($_POST['is_public']) ? 1 : 0,
        trim((string) ($_POST['released_at'] ?? date('Y-m-d H:i:s'))),
    ];
    if ($id > 0) {
        db()->prepare('UPDATE downloads SET os=?, version=?, file_url=?, file_size=?, checksum_sha256=?, changelog=?, is_public=?, released_at=? WHERE id=?')->execute([...$data, $id]);
    } else {
        db()->prepare('INSERT INTO downloads (os, version, file_url, file_size, checksum_sha256, changelog, is_public, released_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)')->execute($data);
    }
    clear_cache();
    header('Location: /admin.php?action=downloads');
    exit;
}

if ($action === 'categories') {
    $items = db()->query('SELECT * FROM categories ORDER BY lang, type, name LIMIT 300')->fetchAll();
    admin_view('categories', ['title' => 'Категории', 'items' => $items]);
    exit;
}

if ($action === 'save_category' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $name = trim((string) ($_POST['name'] ?? ''));
    $slug = trim((string) ($_POST['slug'] ?? '')) ?: slugify($name);
    db()->prepare('INSERT INTO categories (lang, name, slug, type) VALUES (?, ?, ?, ?) ON DUPLICATE KEY UPDATE name=VALUES(name), type=VALUES(type)')->execute([
        $_POST['lang'] ?? 'ru',
        $name,
        $slug,
        $_POST['type'] ?? 'article',
    ]);
    clear_cache();
    header('Location: /admin.php?action=categories');
    exit;
}

if ($action === 'tags') {
    $items = db()->query('SELECT * FROM tags ORDER BY lang, name LIMIT 500')->fetchAll();
    admin_view('tags', ['title' => 'Теги', 'items' => $items]);
    exit;
}

if ($action === 'save_tag' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $name = trim((string) ($_POST['name'] ?? ''));
    $slug = trim((string) ($_POST['slug'] ?? '')) ?: slugify($name);
    db()->prepare('INSERT INTO tags (lang, name, slug) VALUES (?, ?, ?) ON DUPLICATE KEY UPDATE name=VALUES(name)')->execute([
        $_POST['lang'] ?? 'ru',
        $name,
        $slug,
    ]);
    clear_cache();
    header('Location: /admin.php?action=tags');
    exit;
}

if ($action === 'users') {
    $users = db()->query('SELECT id, name, email, role, created_at FROM users ORDER BY id DESC LIMIT 200')->fetchAll();
    admin_view('users', ['title' => 'Пользователи', 'users' => $users]);
    exit;
}

if ($action === 'save_user' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $password = (string) ($_POST['password'] ?? '');
    if ($password !== '') {
        db()->prepare('INSERT INTO users (name, email, password_hash, role) VALUES (?, ?, ?, ?) ON DUPLICATE KEY UPDATE name=VALUES(name), role=VALUES(role), password_hash=VALUES(password_hash)')->execute([
            trim((string) ($_POST['name'] ?? '')),
            trim((string) ($_POST['email'] ?? '')),
            password_hash($password, PASSWORD_DEFAULT),
            in_array($_POST['role'] ?? '', ['admin', 'editor'], true) ? $_POST['role'] : 'editor',
        ]);
    }
    header('Location: /admin.php?action=users');
    exit;
}

if ($action === 'settings') {
    $settings = db()->query('SELECT * FROM settings ORDER BY name LIMIT 300')->fetchAll();
    admin_view('settings', ['title' => 'Настройки', 'settings' => $settings]);
    exit;
}

if ($action === 'save_setting' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    db()->prepare('INSERT INTO settings (name, value) VALUES (?, ?) ON DUPLICATE KEY UPDATE value=VALUES(value)')->execute([
        trim((string) ($_POST['name'] ?? '')),
        (string) ($_POST['value'] ?? ''),
    ]);
    clear_cache();
    header('Location: /admin.php?action=settings');
    exit;
}

if ($action === 'comments') {
    $comments = db()->query('SELECT c.*, p.title FROM comments c LEFT JOIN posts p ON c.post_id = p.id ORDER BY c.created_at DESC LIMIT 500')->fetchAll();
    admin_view('comments', ['title' => 'Комментарии', 'comments' => $comments]);
    exit;
}

if ($action === 'approve_comment' && ($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    check_csrf();
    $id = (int) ($_POST['id'] ?? 0);
    $status = in_array($_POST['status'] ?? '', ['approved', 'rejected', 'spam'], true) ? $_POST['status'] : 'approved';
    if ($id > 0) {
        db()->prepare('UPDATE comments SET status = ? WHERE id = ?')->execute([$status, $id]);
        clear_cache();
    }
    header('Location: /admin.php?action=comments');
    exit;
}

admin_view('dashboard', ['title' => 'Админка']);
