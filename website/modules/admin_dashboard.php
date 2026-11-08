<h1>Панель администратора</h1>

<div class="panel"><h2>Использование программы</h2><p>Активные установки, версии и скачивания обновлений.</p><a class="btn btn-main" href="/admin.php?action=app_statistics">Открыть статистику</a></div>

<div style="display:grid; grid-template-columns:repeat(auto-fit, minmax(250px, 1fr)); gap:1.5rem; margin-top:2rem;">
  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">📝 Посты</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Управление контентом: статьи, гайды, FAQ</p>
    <a href="/admin.php?action=posts" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">💬 Комментарии</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Модерация комментариев от пользователей</p>
    <a href="/admin.php?action=comments" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">🔗 Редиректы</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">301 редиректы для миграции и переадресации</p>
    <a href="/admin.php?action=redirects" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">🚀 Версии программы</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Управление загрузками и версиями</p>
    <a href="/admin.php?action=downloads" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">🏷️ Категории</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Управление рубриками контента</p>
    <a href="/admin.php?action=categories" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">🔖 Теги</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Управление тегами и ключевыми словами</p>
    <a href="/admin.php?action=tags" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">👥 Пользователи</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Управление администраторами и редакторами</p>
    <a href="/admin.php?action=users" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>

  <div class="panel" style="padding:1.5rem;">
    <h3 style="margin-top:0;">⚙️ Настройки</h3>
    <p style="color:var(--muted); margin-bottom:1rem;">Общие параметры сайта</p>
    <a href="/admin.php?action=settings" class="btn btn-main" style="display:inline-block; width:100%; text-align:center;">Перейти</a>
  </div>
</div>

<div style="margin-top:2rem; padding:1.5rem; background:var(--surface-2); border-radius:8px;">
  <h3 style="margin-top:0;">⚠️ Важно!</h3>
  <ul style="margin:1rem 0; padding-left:1.5rem;">
    <li>🗑️ <strong>Удалить install.php</strong> после установки</li>
    <li>🔐 <strong>Установить пароль БД</strong> (см. config.php)</li>
    <li>📧 <strong>Проверить логирование ошибок</strong> в logs/error.log</li>
    <li>🔒 <strong>Убедиться, что HTTPS включен</strong></li>
  </ul>
  <a href="/admin.php?action=logout" style="color:var(--brand);">Выход</a>
</div>
