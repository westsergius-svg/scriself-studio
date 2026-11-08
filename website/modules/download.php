<?php
/**
 * Страница скачивания.
 * Ожидает: $downloads (список из downloads_public()), $latest (последняя запись).
 */

require_once __DIR__ . '/download_presentation.php';
$downloadLanguage = ($lang ?? 'ru') === 'en' ? 'en' : 'ru';
$downloadText = static fn(string $ru, string $en): string => $downloadLanguage === 'en' ? $en : $ru;
$currentVersion = download_version_label((string) ($latest['version'] ?? $downloads[0]['version'] ?? ''));
$currentDownloads = [];
$previousDownloads = [];
foreach ($downloads ?? [] as $download) {
    if (download_version_label((string) $download['version']) === $currentVersion) {
        $currentDownloads[] = $download;
    } else {
        $previousDownloads[] = $download;
    }
}

$osIcon = static fn(string $os): string => match (strtolower($os)) {
    'windows' => '🪟',
    'android' => '🤖',
    'macos', 'mac' => '🍎',
    'linux' => '🐧',
    default => '💾',
};
?>

<link rel="stylesheet" href="/assets/css/download-archive.css?v=20261109">

<section class="download-hero">
  <div class="container narrow">
    <p class="eyebrow">Скачать</p>
    <h1>Скачать ScriSelf бесплатно</h1>
    <p class="os-note">Скачивайте сразу — регистрация и подписка не нужны.</p>
    <p class="lead">Пишите и планируйте офлайн: файл проекта хранится на вашем компьютере. Если нужно перенести его на другое устройство, подключите Яндекс.Диск или Google Drive и вручную загрузите проект.</p>
    <?php if (!empty($latest)): ?>
      <p class="os-note">Актуальная версия: <strong><?= e($currentVersion) ?></strong> от <?= e(fmt_date($latest['released_at'])) ?></p>
    <?php endif; ?>
  </div>
</section>

<section class="section" style="padding-top:0;">
  <div class="container">
    <?php if (!empty($downloads)): ?>
      <div class="download-cards">
        <?php foreach ($currentDownloads as $d): ?>
          <article class="dl-card" data-os="<?= e(strtolower($d['os'])) ?>">
            <span class="dl-os"><?= $osIcon($d['os']) ?></span>
            <h2><?= e($d['os']) ?></h2>
            <p class="dl-meta">
              <?= e($downloadText('Версия', 'Version')) ?> <?= e(download_version_label($d['version'])) ?> · <?= e(fmt_date($d['released_at'])) ?><?= ($downloadSize = download_size_label($d, $downloadLanguage)) !== '' ? ' · ' . e($downloadSize) : '' ?>
            </p>
            <a class="btn btn-main" href="/download/file/<?= (int) $d['id'] ?>" rel="nofollow">Скачать</a>
            <p class="dl-count">Скачиваний: <strong><?= number_format((int) ($d['download_count'] ?? 0), 0, '.', ' ') ?></strong></p>
            <?php if (!empty($d['checksum_sha256'])): ?>
              <p class="muted" style="font-size:0.8rem; margin:0.7rem 0 0; word-break:break-all;">SHA256: <?= e($d['checksum_sha256']) ?></p>
            <?php endif; ?>
          </article>
        <?php endforeach; ?>
      </div>
    <?php else: ?>
      <div class="panel"><p class="muted">Файлы для скачивания скоро появятся. Следите за обновлениями!</p></div>
    <?php endif; ?>

    <p class="download-repository"><a href="https://github.com/westsergius-svg/scriself-studio" target="_blank" rel="noopener noreferrer"><?= e($downloadText('Репозиторий на GitHub', 'Repository on GitHub')) ?></a></p>

    <?php if (!empty($downloads)):
        $changelog = array_values(array_filter($currentDownloads, static fn(array $d): bool => trim((string) ($d['changelog'] ?? '')) !== ''));
    ?>
      <?php if ($changelog): ?>
        <div class="section-head" style="margin-top:3.5rem;">
          <p class="eyebrow">Что нового</p>
          <h2><?= e($downloadText('Что нового в версии ' . $currentVersion, 'What’s new in version ' . $currentVersion)) ?></h2>
        </div>
        <div class="changelog">
          <?php foreach ($changelog as $ch): ?>
            <div class="ch-item">
              <span class="ch-ver"><?= e($downloadText('Версия', 'Version')) ?> <?= e(download_version_label($ch['version'])) ?></span>
              <span class="muted"><?= e(fmt_date($ch['released_at'])) ?></span>
              <p style="margin:0;"><?= nl2br(e($ch['changelog'])) ?></p>
            </div>
          <?php endforeach; ?>
        </div>
      <?php endif; ?>
    <?php endif; ?>

    <?php if ($previousDownloads): ?>
      <section class="release-archive" aria-labelledby="previous-releases-title">
        <h2 id="previous-releases-title"><?= e($downloadText('Предыдущие версии', 'Previous versions')) ?></h2>
        <ul class="release-list">
          <?php foreach ($previousDownloads as $d):
              $version = download_version_label((string) $d['version']);
              $size = download_size_label($d, $downloadLanguage);
              $sameVersion = array_filter($previousDownloads, static fn(array $other): bool => download_version_label((string) $other['version']) === $version);
          ?>
            <li class="release-row">
              <span class="release-version"><?= e($downloadText('Версия', 'Version')) ?> <?= e($version) ?><?php if (count($sameVersion) > 1): ?><span class="release-platform"><?= e($d['os']) ?></span><?php endif; ?></span>
              <a class="release-download" href="/download/file/<?= (int) $d['id'] ?>" rel="nofollow" aria-label="<?= e($downloadText('Скачать версию ', 'Download version ') . $version . ' (' . $d['os'] . ')') ?>"><?= e($downloadText('Скачать', 'Download')) ?></a>
              <?php if ($size !== ''): ?><span class="release-size"><?= e($size) ?></span><?php endif; ?>
            </li>
          <?php endforeach; ?>
        </ul>
      </section>
    <?php endif; ?>

    <div class="section-head" style="margin-top:4rem;">
      <p class="eyebrow">Вопросы</p>
      <h2>Установка и безопасность</h2>
    </div>
    <div class="grid-2">
      <div class="card">
        <h3>Windows предупреждает о запуске</h3>
        <p>Приложение без цифровой подписи (бесплатный проект). Нажмите «Подробнее» → «Выполнить в любом случае». Если появится окно SmartScreen, выберите «Выполнить в любом случае» повторно.</p>
      </div>
      <div class="card">
        <h3>Антивирус ругается?</h3>
        <p>Установщик собран с помощью PyInstaller и NSIS — это популярная причина ложных срабатываний. Сверьте контрольную сумму SHA256 с указанной на этой странице.</p>
      </div>
      <div class="card">
        <h3>Программа бесплатна?</h3>
        <p>Да. ScriSelf Studio распространяется бесплатно. Для облачного хранения потребуется ваш аккаунт Яндекс.Диска или Google Drive; загрузка и скачивание проекта выполняются вручную.</p>
      </div>
      <div class="card">
        <h3>Нужен ли интернет?</h3>
        <p>Для написания и редактирования интернет не нужен: проект хранится на компьютере. Интернет потребуется для проверки обновлений и при использовании облачного хранения.</p>
      </div>
    </div>
  </div>
</section>
