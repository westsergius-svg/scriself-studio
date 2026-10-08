const body = document.body;
const trigger = document.getElementById('themeTrigger');
const menu = document.getElementById('themeMenu');
const themeName = document.getElementById('themeName');

const names = {
  studio: 'Studio',
  paper: 'Paper',
  graphite: 'Graphite',
  ocean: 'Ocean',
  forest: 'Forest'
};

trigger.addEventListener('click', (e) => {
  e.stopPropagation();
  menu.classList.toggle('open');
});

document.addEventListener('click', () => menu.classList.remove('open'));

menu.addEventListener('click', (e) => {
  const item = e.target.closest('[data-theme]');
  if (!item) return;
  const theme = item.dataset.theme;
  body.dataset.theme = theme;
  themeName.textContent = names[theme];
  localStorage.setItem('scriself-theme', theme);
  menu.classList.remove('open');
});

const saved = localStorage.getItem('scriself-theme');
if (saved && names[saved]) {
  body.dataset.theme = saved;
  themeName.textContent = names[saved];
}
