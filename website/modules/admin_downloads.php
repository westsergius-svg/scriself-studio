<h1>Версии программы</h1>
<p><a class="btn btn-main" href="/admin.php?action=app_statistics">Активные установки и скачивания обновлений</a></p>
<?php $download = $editDownload ?? []; ?>
<form class="panel form-grid" method="post" action="/admin.php?action=save_download">
  <h2><?= $download ? 'Изменить версию' : 'Добавить версию' ?></h2>
  <input type="hidden" name="csrf" value="<?= e(csrf_token()) ?>">
  <input type="hidden" name="id" value="<?= (int) ($download['id'] ?? 0) ?>">
  <div class="row-2"><label>ОС<input class="input" name="os" value="<?= e($download['os'] ?? '') ?>" required></label><label>Версия<input class="input" name="version" value="<?= e($download['version'] ?? '') ?>" required></label></div>
  <label>Файл / URL<input class="input" name="file_url" value="<?= e($download['file_url'] ?? '') ?>" required></label>
  <div class="row-2"><label>Размер<input class="input" name="file_size" value="<?= e($download['file_size'] ?? '') ?>"></label><label>SHA-256<input class="input" name="checksum_sha256" maxlength="64" value="<?= e($download['checksum_sha256'] ?? '') ?>"></label></div>
  <label>Изменения<textarea class="input" name="changelog" rows="4"><?= e($download['changelog'] ?? '') ?></textarea></label>
  <div class="row-2"><label>Дата релиза<input class="input" name="released_at" value="<?= e($download['released_at'] ?? date('Y-m-d H:i:s')) ?>"></label><label><input type="checkbox" name="is_public" value="1" <?= ($download['is_public'] ?? 1) ? 'checked' : '' ?>> Публичная версия</label></div>
  <button class="btn-main"><?= $download ? 'Сохранить изменения' : 'Добавить' ?></button>
  <?php if ($download): ?><a href="/admin.php?action=downloads">Отмена</a><p class="muted">Замена файла и его описания сохраняет накопленный счётчик скачиваний.</p><?php endif; ?>
</form>
<p class="mt-3">Начатых скачиваний с сайта: <strong><?= number_format((int) ($totalDownloads ?? 0), 0, '.', ' ') ?></strong>. Обновления из программы и полностью переданные файлы — в <a href="/admin.php?action=app_statistics">статистике использования</a>.</p>
<div class="panel mt-3" style="overflow-x:auto"><table class="table"><tr><th>ОС</th><th>Версия</th><th>Файл</th><th>Дата</th><th>Публично</th><th>Скачиваний</th><th>Действие</th></tr><?php foreach ($items as $item): ?><tr><td><?= e($item['os']) ?></td><td><?= e($item['version']) ?></td><td><a href="<?= e($item['file_url']) ?>"><?= e($item['file_url']) ?></a></td><td><?= e($item['released_at']) ?></td><td><?= !empty($item['is_public']) ? 'да' : 'нет' ?></td><td><?= number_format((int) ($item['download_count'] ?? 0), 0, '.', ' ') ?></td><td><a href="/admin.php?action=downloads&amp;edit=<?= (int) $item['id'] ?>">Изменить</a></td></tr><?php endforeach; ?></table></div>
