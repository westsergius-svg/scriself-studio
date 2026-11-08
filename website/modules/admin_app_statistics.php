<?php
if (!function_exists('is_admin') || !is_admin()) { http_response_code(403); exit; }
$number = static fn ($value): string => number_format((int) $value, 0, '.', ' ');
$platformLabels = ['win-x64' => 'Windows', 'win-arm64' => 'Windows ARM', 'osx-x64' => 'macOS · Intel', 'osx-arm64' => 'macOS · Apple Silicon', 'linux-x64' => 'Linux', 'linux-arm64' => 'Linux ARM'];
$sourceLabels = ['updater' => 'Из программы', 'website' => 'С сайта', 'direct' => 'Прямая ссылка'];
?>
<link href="/assets/css/app-statistics.css" rel="stylesheet">
<div class="app-statistics">
  <h1>Использование программы</h1>
  <p class="stats-note">Сколько установок открывают программу и сколько раз скачивают обновления. Даты и периоды — по UTC.</p>
<?php if (!empty($statisticsError)): ?>
  <div class="panel"><p role="alert">Не удалось загрузить статистику. Обновите страницу или проверьте подключение сайта к базе данных.</p></div>
<?php else: ?>
  <h2>Активные установки</h2>
  <div class="stats-cards">
  <?php foreach (['day_count' => 'Сегодня', 'week_count' => 'За 7 дней', 'month_count' => 'За 30 дней', 'inactive_count' => 'Без сигналов 30 дней', 'total_count' => 'Всего со статистикой'] as $key => $label): ?>
    <div class="panel stats-card"><span><?= e($label) ?></span><strong><?= $number($statistics[$key]) ?></strong></div>
  <?php endforeach; ?>
  </div>
  <p class="stats-note">Активная установка — программа была на переднем плане и отправила сигнал с согласия пользователя. Одна установка учитывается один раз за период. Это не число людей: один человек может работать на нескольких устройствах.</p>

  <h2>Скачивания</h2>
  <div class="stats-cards">
    <div class="panel stats-card"><span>Обновлений из программы · всего</span><strong><?= $number($statistics['download_totals']['updater_completed']) ?></strong></div>
    <div class="panel stats-card"><span>Все источники · за 30 дней</span><strong><?= $number($statistics['download_totals']['completed_30d']) ?></strong></div>
    <div class="panel stats-card"><span>Все источники · всего</span><strong><?= $number($statistics['download_totals']['completed']) ?></strong></div>
  </div>
  <p class="stats-note">В сводке — файлы, которые сервер передал полностью. Это не подтверждает установку программы. Повторные скачивания учитываются отдельно. Источник определяется по запросу.</p>
  <div class="panel stats-table"><table class="table">
    <caption>По файлам и источникам</caption>
    <thead><tr><th scope="col">Файл</th><th scope="col">Источник</th><th scope="col">Начато<br>за 30 дней</th><th scope="col">Передано<br>за 30 дней</th><th scope="col">Начато<br>всего</th><th scope="col">Передано<br>всего</th></tr></thead>
    <tbody>
    <?php foreach ($statistics['downloads'] as $row): ?>
      <tr><td class="stats-file"><?= e($row['file_name']) ?></td><td><?= e($sourceLabels[$row['source']] ?? $row['source']) ?></td><td><?= $number($row['started_30d']) ?></td><td><?= $number($row['completed_30d']) ?></td><td><?= $number($row['started']) ?></td><td><?= $number($row['completed']) ?></td></tr>
    <?php endforeach; ?>
    <?php if (!$statistics['downloads']): ?><tr><td colspan="6">Скачиваний после подключения счётчика пока нет.</td></tr><?php endif; ?>
    </tbody>
  </table></div>

  <h2>Версии активных установок за 30 дней</h2>
  <div class="panel stats-table"><table class="table"><thead><tr><th scope="col">Версия</th><th scope="col">Платформа</th><th scope="col">Установки</th></tr></thead><tbody>
  <?php foreach ($statistics['versions'] as $row): ?>
    <tr><td><?= e($row['version']) ?></td><td><?= e($platformLabels[$row['platform']] ?? $row['platform']) ?></td><td><?= $number($row['installations']) ?></td></tr>
  <?php endforeach; ?>
  <?php if (!$statistics['versions']): ?><tr><td colspan="3">Сигналов пока нет. Они появятся после установки версии со статистикой и согласия пользователей.</td></tr><?php endif; ?>
  </tbody></table></div>

  <h2>Активность за 30 дней</h2>
  <div class="panel">
  <?php $maxActivity = max(1, ...array_column($statistics['days'], 'installations')); ?>
    <div class="stats-chart" role="img" aria-label="Активные установки по дням. Точные значения доступны в таблице ниже.">
    <?php foreach (array_reverse($statistics['days']) as $row): ?>
      <div class="stats-bar-slot" title="<?= e($row['activity_day']) ?>: <?= (int) $row['installations'] ?>">
        <div class="stats-bar" style="height:<?= (int) round(100 * $row['installations'] / $maxActivity) ?>%"></div>
      </div>
    <?php endforeach; ?>
    </div>
    <div class="stats-chart-dates"><span><?= e($statistics['days'][29]['activity_day']) ?></span><span><?= e($statistics['days'][0]['activity_day']) ?></span></div>
    <details><summary>Значения по дням</summary>
      <div class="stats-table"><table class="table"><thead><tr><th scope="col">Дата (UTC)</th><th scope="col">Установки</th></tr></thead><tbody>
      <?php foreach ($statistics['days'] as $row): ?><tr><td><?= e($row['activity_day']) ?></td><td><?= $number($row['installations']) ?></td></tr><?php endforeach; ?>
      </tbody></table></div>
    </details>
  </div>
  <p class="stats-note">Статистика не содержит имён, аккаунтов, IP-адресов, названий проектов и текста рукописей. Отсутствие сигналов может означать, что программу не открывали, отключили статистику или работали без интернета. Учёт начинается с подключения счётчика; прежние скачивания остаются в разделе «Версии».</p>
<?php endif; ?>
</div>
