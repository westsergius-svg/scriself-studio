<?php
/**
 * Детальная страница поста (article / guide / program_page / glossary).
 * Ожидает $post (из БД). Отрисовывает: шапку, TOC, контент, теги,
 * шеринг, похожие посты, комментарии и CTA.
 */
$section = type_section($post['type'] ?? 'article');

/* --- Подготовка контента: идентификаторы заголовков + TOC --- */
$contentHtml = format_post_content((string) ($post['content_html'] ?? ''));
$toc = [];
if ($contentHtml !== '') {
    $dom = new DOMDocument();
    libxml_use_internal_errors(true);
    $dom->loadHTML('<?xml encoding="utf-8"?>' . $contentHtml, LIBXML_HTML_NOIMPLIED | LIBXML_HTML_NODEFDTD);
    libxml_clear_errors();
    $usedIds = [];
    foreach (['h2', 'h3'] as $tag) {
        foreach ($dom->getElementsByTagName($tag) as $h) {
            $baseId = slugify($h->textContent);
            $id = $baseId;
            $counter = 2;
            while (isset($usedIds[$id])) {
                $id = $baseId . '-' . $counter++;
            }
            $usedIds[$id] = true;
            $h->setAttribute('id', $id);
            $toc[] = ['id' => $id, 'text' => trim($h->textContent), 'lvl' => $tag === 'h3' ? 3 : 2];
        }
    }
    $contentHtml = $dom->saveHTML() ?: $contentHtml;
    $contentHtml = preg_replace('/^<\?xml encoding="utf-8"\?>\s*/', '', $contentHtml) ?: $contentHtml;
}

$wordCount = count(preg_split('/\s+/u', trim(strip_tags($contentHtml))) ?: []);
$readMin = max(1, (int) round($wordCount / 200));

$tags = db_ok() ? tags_for_post((int) $post['id']) : [];
$related = db_ok() ? related_posts($post, 3) : [];

$uri = current_path();
$postedOk  = ($_GET['comment'] ?? '') === 'ok';
$postedMsg = match ($_GET['comment'] ?? '') {
    'pending' => 'Комментарий отправлен и появится после модерации.',
    'error' => 'Не удалось отправить комментарий. Попробуйте ещё раз.',
    default => '',
};
?>

<section class="post-head">
  <div class="container narrow">
    <nav class="breadcrumb" aria-label="Хлебные крошки">
      <a href="/">Главная</a><span class="sep"></span>
      <a href="<?= e(section_url($section)) ?>"><?= e(section_label($section)) ?></a><span class="sep"></span>
      <span><?= e(mb_strimwidth($post['title'], 0, 70, '…')) ?></span>
    </nav>
    <p class="eyebrow"><?= e(type_label($post['type'] ?? 'article')) ?></p>
    <h1><?= e($post['title']) ?></h1>
    <?php if (!empty($post['excerpt'])): ?><p class="lead"><?= e($post['excerpt']) ?></p><?php endif; ?>
    <div class="article-meta">
      <span>📅 <?= e(fmt_date($post['published_at'] ?? $post['updated_at'])) ?></span>
      <span>⏱ <?= $readMin ?> мин чтения</span>
    </div>
    <?php if ($tags): ?>
      <div class="tag-list">
        <?php foreach ($tags as $tag): ?><a class="tag" href="/search?q=<?= e($tag['name']) ?>">#<?= e($tag['name']) ?></a><?php endforeach; ?>
      </div>
    <?php endif; ?>
  </div>
</section>

<div class="container">
  <div class="post-layout">
    <article class="post-content panel">
      <?= $contentHtml ?>

      <?php if ($tags): ?>
        <div class="tag-list">
          <?php foreach ($tags as $tag): ?><a class="tag" href="/search?q=<?= e($tag['name']) ?>">#<?= e($tag['name']) ?></a><?php endforeach; ?>
        </div>
      <?php endif; ?>

      <div class="share-row">
        <span class="share-label">Поделиться:</span>
        <button type="button" class="share-btn" data-share="telegram" data-title="<?= e($post['title']) ?>">Telegram</button>
        <button type="button" class="share-btn" data-share="x" data-title="<?= e($post['title']) ?>">X</button>
        <button type="button" class="share-btn" data-share="copy">Копировать ссылку</button>
      </div>
    </article>

    <?php if ($toc): ?>
      <aside class="toc">
        <div class="panel">
          <h4>Содержание</h4>
          <ol>
            <?php foreach ($toc as $item): ?>
              <li class="<?= $item['lvl'] === 3 ? 'lvl-2' : '' ?>"><a href="#<?= e($item['id']) ?>"><?= e($item['text']) ?></a></li>
            <?php endforeach; ?>
          </ol>
        </div>
      </aside>
    <?php endif; ?>
  </div>
</div>

<?php if ($related): ?>
  <section class="section">
    <div class="container">
      <div class="section-head">
        <p class="eyebrow">Похожие материалы</p>
        <h2>Читайте также</h2>
      </div>
      <div class="resource-grid">
        <?php foreach ($related as $p): ?>
          <article class="article-card">
            <div>
              <div class="article-meta"><span><?= e(type_label($p['type'])) ?></span><span><?= e(fmt_date($p['published_at'])) ?></span></div>
              <h3><a href="<?= e(post_url($p)) ?>"><?= e($p['title']) ?></a></h3>
              <p><?= e(mb_strimwidth((string) ($p['excerpt'] ?? ''), 0, 160, '…')) ?></p>
            </div>
          </article>
        <?php endforeach; ?>
      </div>
    </div>
  </section>
<?php endif; ?>

<section class="section comments">
  <div class="container narrow">
    <div class="section-head">
      <p class="eyebrow">Обсуждение</p>
      <h2>Комментарии</h2>
    </div>

    <div class="comments">
      <?php foreach (comments_public((int) $post['id']) as $c): ?>
        <div class="comment <?= $c['parent_id'] ? 'reply' : '' ?>">
          <div class="comment-head">
            <span class="comment-author"><?= e($c['author_name']) ?></span>
            <span class="comment-date"><?= e(fmt_date($c['created_at'])) ?></span>
          </div>
          <p><?= nl2br(e($c['content'])) ?></p>
        </div>
      <?php endforeach; ?>
      <?php if (!count(comments_public((int) $post['id']))): ?>
        <p class="muted">Комментариев пока нет — будьте первым!</p>
      <?php endif; ?>
    </div>

    <?php if ($postedOk): ?><p style="color:var(--ok); font-weight:700;">Спасибо! Комментарий добавлен.</p><?php endif; ?>
    <?php if ($postedMsg): ?><p class="muted"><?= e($postedMsg) ?></p><?php endif; ?>

    <form class="panel form-grid" method="post" action="<?= e($uri) ?>" style="margin-top:1.5rem;">
      <h3 style="margin-bottom:0;">Оставить комментарий</h3>
      <div class="row-2">
        <label>Имя
          <input class="input" type="text" name="name" maxlength="120" required>
        </label>
        <label>E-mail (не публикуется)
          <input class="input" type="email" name="email" maxlength="190" required>
        </label>
      </div>
      <label>Комментарий
        <textarea class="input" name="content" rows="4" maxlength="2000" required></textarea>
      </label>
      <label class="hp-field" aria-hidden="true">Не заполняйте это поле
        <input type="text" name="website" tabindex="-1" autocomplete="off">
      </label>
      <input type="hidden" name="post_id" value="<?= (int) $post['id'] ?>">
      <input type="hidden" name="token" value="<?= e(comment_token()) ?>">
      <div><button class="btn btn-main" type="submit">Отправить</button></div>
    </form>
  </div>
</section>

<section class="post-cta section">
  <div class="container narrow" style="text-align:center;">
    <h2>Начните свою книгу в ScriSelf</h2>
    <p>Структура книги, сцены, персонажи и сюжетные линии — в одной программе. Работайте офлайн; облако используйте по желанию для ручного переноса проекта.</p>
    <a class="btn btn-hero" href="/download">Скачать для Windows</a>
  </div>
</section>
