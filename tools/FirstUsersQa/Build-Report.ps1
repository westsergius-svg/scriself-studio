param([string] $ReportDirectory = 'qa/first-users/20261104-1.2.0')
$ErrorActionPreference = 'Stop'
$reportRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) $ReportDirectory))
$data = [IO.File]::ReadAllText((Join-Path $reportRoot 'findings.json')).Replace('<', '\u003c')
$template = @'
<!doctype html>
<html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>ScriSelf Studio 1.2.0 · Первое использование</title>
<style>
:root{color-scheme:dark;font:15px/1.5 system-ui,sans-serif;background:#202020;color:#eee}
body{margin:0 auto;padding:32px;max-width:1500px}h1{font-size:28px;margin:0 0 12px}h2{font-size:20px;margin-top:36px}
p{max-width:1000px}a{color:#8ac4ff}small,.muted{color:#b4b4b4}.intro{border-left:3px solid #438edf;padding:2px 20px}
.summary{display:flex;gap:36px;flex-wrap:wrap;padding:16px 0;border-top:1px solid #494949;border-bottom:1px solid #494949;margin:24px 0}.summary strong{display:block;font-size:23px;color:#eee}.summary span{color:#b4b4b4}.proof{display:block;font-size:12px;margin-top:4px}
table{border-collapse:collapse;width:100%;background:#292929}th,td{text-align:left;vertical-align:top;padding:12px;border-bottom:1px solid #494949}th{background:#343434;font-weight:600}tbody tr:hover{background:#303030}
.scroll{overflow-x:auto}.count{font-size:21px;white-space:nowrap}.nowrap{white-space:nowrap}button,input{font:inherit;padding:8px 12px;background:#333;color:#eee;border:1px solid #666;border-radius:4px}button{cursor:pointer}button[aria-pressed=true]{background:#285987;border-color:#7bb6f4}input{min-width:260px}
.filters{display:flex;gap:8px;flex-wrap:wrap;margin:20px 0}.evidence{display:flex;gap:10px;flex-wrap:wrap;margin-top:12px}.evidence img{width:220px;max-height:155px;object-fit:contain;background:#1e1e1e;border:1px solid #555}.evidence a{font-size:12px;display:grid;gap:4px;max-width:220px}summary{cursor:pointer;color:#8ac4ff}details p{margin:10px 0}.id{font-size:12px;color:#aaa}
@media(max-width:650px){body{padding:16px}h1{font-size:24px}th,td{padding:8px}.issues{min-width:850px}}
@media print{.filters{display:none}body{max-width:none}.scroll{overflow:visible}details{display:block}a{color:inherit}}
</style>
<h1>Первое использование · ScriSelf Studio 1.2.0</h1>
<p class="intro"><strong>Первый успешный результат:</strong> создать проект, книгу, главу и сцену, написать предложение, явно сохранить файл .scri, закрыть и снова открыть проект и увидеть тот же текст.</p>
<p id="state" class="muted"></p>
<p>Два последовательных прохода по шести независимым агентам. Это имитация новых пользователей, не исследование с людьми. Участники второго прохода не получали первую таблицу и историю исправлений. Число кликов включает только нажатия внутри приложения; двойное нажатие считается дважды.</p>
<div id="summary" class="summary" aria-label="Сравнение результатов"></div>
<h2>Результаты участников</h2><div class="scroll"><table><thead><tr><th>Участник</th><th>До: успех / клики</th><th>После: успех / клики</th><th>Вернулся бы: до → после</th></tr></thead><tbody id="participants"></tbody></table></div>
<h2>Общая таблица проблем</h2><p class="muted">Одинаковые проблемы на одном экране объединены. Описание в строке передаёт первоначальное наблюдение; повторные наблюдения и снимки раскрываются отдельно. Сортировка — по числу разных участников двух проходов, столкнувшихся с проблемой.</p>
<div class="filters"><button data-filter="all" aria-pressed="true">Все</button><button data-filter="open" aria-pressed="false">Остались</button><button data-filter="legal" aria-pressed="false">Вопросы владельцу</button><input id="query" aria-label="Найти проблему" placeholder="Найти проблему"></div>
<div class="scroll"><table class="issues"><thead><tr><th>Участники<br>до → после</th><th>Этап и проблема</th><th>Результат исправления</th><th>Доказательства</th></tr></thead><tbody id="issues"></tbody></table></div>
<h2>Границы проверки</h2><p>Настоящие интерфейсы Avalonia и производственное SQLite-хранилище работают в локальном Headless/Skia-стенде. Это не управление живым окном Windows. Диалоги выбора файла возвращают изолированные тестовые пути. ОС/DPI, системный IME, установка, первичное согласие на статистику, облачные аккаунты, платежи и реальное 3G не проверялись. Для маленького экрана сначала запрашивалась ширина 375 px, затем путь проходился при 1024×768. Основной размер — 1440×900.</p>
<p id="validation"></p><div id="additional-validation"></div><p><a href="README.md">Методика и ограничения</a> · <a href="findings.json">Данные общей таблицы</a></p>
<script>
const data=__DATA__;
const names=Object.fromEntries([...data.before,...data.after].map(p=>[p.id,p.name]));
const esc=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const result=p=>p?`<a href="${esc(p.assessment)}">${p.success?'Да':'Нет'} · ${p.clicks} кл.</a>${p.successScreenshot?`<a class="proof" href="${esc(p.successScreenshot)}" target="_blank" rel="noopener">Текст после открытия</a>`:''}`:'—';
const returned=p=>p?(p.wouldReturn?'Да':'Нет'):'—';
document.getElementById('state').textContent=data.state;
document.getElementById('validation').textContent=data.validation||'';
document.getElementById('additional-validation').innerHTML=(data.additionalValidation||[]).map(v=>`<p>${esc(typeof v==='string'?v:v.text)}${v.screenshot?` <a href="${esc(v.screenshot)}" target="_blank" rel="noopener">Снимок настоящего окна</a>`:''}</p>`).join('');
const ratio=(phase,field)=>`${phase.filter(p=>p[field]).length}/${phase.length}`;
if(data.after.length)document.getElementById('summary').innerHTML=`<div><strong>${ratio(data.before,'success')} → ${ratio(data.after,'success')}</strong><span>Достигли первого успеха · до → после</span></div><div><strong>${ratio(data.before,'wouldReturn')} → ${ratio(data.after,'wouldReturn')}</strong><span>Вернулись бы к продукту · до → после</span></div>`;
const participantNotes=(b,a)=>(b.reason||a?.reason)?`<details><summary>Почему вернулся бы</summary>${b.reason?`<p><strong>До${b.originalReason?' (перевод)':''}:</strong> ${esc(b.reason)}</p>`:''}${a?.reason?`<p><strong>После${a.originalReason?' (перевод)':''}:</strong> ${esc(a.reason)}</p>`:''}</details>`:'';
document.getElementById('participants').innerHTML=data.before.map(b=>{const a=data.after.find(p=>p.id===b.id);return `<tr><td>${esc(b.name)}</td><td>${result(b)}</td><td>${result(a)}</td><td>${returned(b)} → ${returned(a)}${participantNotes(b,a)}</td></tr>`}).join('');
let filter='all';
const observations=i=>i.afterObservations?.length?`<details><summary>Что заметили после</summary>${i.afterObservations.map(o=>`<p><strong>${esc(names[o.persona]||o.persona)}</strong> · ${esc(o.stage)}<br><small>${esc(o.location)}</small></p><p>${esc(o.observed)}</p><p>Действие: ${esc(o.attemptedAction)} <a href="${esc(o.screenshot)}" target="_blank" rel="noopener">Снимок</a></p>`).join('')}</details>`:'';
function render(){const query=document.getElementById('query').value.toLocaleLowerCase();document.getElementById('issues').innerHTML=[...data.issues].sort((a,b)=>(b.beforeAgents.length+b.afterAgents.length)-(a.beforeAgents.length+a.afterAgents.length)||b.afterAgents.length-a.afterAgents.length||a.id.localeCompare(b.id)).filter(i=>(filter!=='legal'||i.category==='commercial-legal')&&(filter!=='open'||i.afterAgents.length||i.category==='commercial-legal'||i.category==='platform'||i.open===true)&&JSON.stringify(i).toLocaleLowerCase().includes(query)).map(i=>`<tr><td><div class="count">${i.beforeAgents.length} → ${i.afterAgents.length}</div><small>До: ${esc(i.beforeAgents.map(p=>names[p]).join(', ')||'—')}<br>После: ${esc(i.afterAgents.map(p=>names[p]).join(', ')||'—')}</small></td><td><div class="id">${esc(i.id)} · ${esc(i.stage)}</div><strong>${esc(i.title)}</strong><p><small>${esc(i.location)}</small></p><p>${esc(i.description)}</p></td><td>${esc(i.status)}${i.fix?`<p>${esc(i.fix)}</p>`:''}${observations(i)}</td><td><details><summary>${i.evidence.length} снимков</summary><p>${esc(i.attemptedAction)}</p><div class="evidence">${i.evidence.map(e=>`<a href="${esc(e.screenshot)}" target="_blank" rel="noopener">${esc(e.phase==='before'?'До':e.phase==='validation'?'Проверка':'После')} · ${esc(names[e.persona]||e.persona)}<img loading="lazy" src="${esc(e.screenshot)}" alt="${esc(i.title)}"></a>`).join('')}</div></details></td></tr>`).join('')}
document.querySelectorAll('[data-filter]').forEach(b=>b.onclick=()=>{filter=b.dataset.filter;document.querySelectorAll('[data-filter]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));render()});document.getElementById('query').oninput=render;render();
</script></html>
'@
[IO.File]::WriteAllText((Join-Path $reportRoot 'report.html'), $template.Replace('__DATA__', $data), [Text.UTF8Encoding]::new($false))
Write-Output (Join-Path $reportRoot 'report.html')
