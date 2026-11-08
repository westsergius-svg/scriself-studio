/* ScriSelf — фронтовые сценарии: тема, меню, TOC, FAQ, OS-детект, шеринг */
(function () {
  'use strict';

  /* ---------- Тема ---------- */
  const ROOT = document.documentElement;

  function applyTheme(theme) {
    ROOT.dataset.theme = theme;
    const icon = document.getElementById('theme-toggle');
    if (icon) icon.textContent = theme === 'dark' ? '☀️' : '🌙';
  }

  const saved = localStorage.getItem('scr-theme');
  applyTheme(saved || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'));

  document.getElementById('theme-toggle')?.addEventListener('click', () => {
    const next = ROOT.dataset.theme === 'dark' ? 'light' : 'dark';
    localStorage.setItem('scr-theme', next);
    applyTheme(next);
  });

  /* ---------- Мобильное меню ---------- */
  const menuBtn = document.getElementById('menu-btn');
  const nav = document.getElementById('main-nav');
  menuBtn?.addEventListener('click', () => nav?.classList.toggle('open'));
  nav?.querySelectorAll('a').forEach(a => a.addEventListener('click', () => nav.classList.remove('open')));

  /* ---------- активный пункт меню ---------- */
  (function markActive() {
    const path = location.pathname.toLowerCase();
    nav?.querySelectorAll('a').forEach(a => {
      const href = (a.getAttribute('href') || '').toLowerCase();
      const hit = href.length > 1 && path.startsWith(href);
      const exact = path === href;
      if (exact) { a.classList.add('active'); return; }
      if (hit) a.classList.add('active');
    });
  })();

  /* ---------- FAQ-аккордеон ---------- */
  document.querySelectorAll('.faq-item').forEach(item => {
    const q = item.querySelector('.faq-q');
    q?.addEventListener('click', () => {
      const open = item.classList.toggle('open');
      q.setAttribute('aria-expanded', open ? 'true' : 'false');
      document.querySelectorAll('.faq-item.open').forEach(other => {
        if (other !== item) { other.classList.remove('open'); other.querySelector('.faq-q')?.setAttribute('aria-expanded', 'false'); }
      });
    });
  });

  /* ---------- TOC: подсветка активного пункта ---------- */
  (function tocScrollSpy() {
    const links = Array.from(document.querySelectorAll('.toc a'));
    if (!links.length) return;
    const heads = links
      .map(a => document.querySelector(a.getAttribute('href')))
      .filter(Boolean);
    const setActive = id => {
      links.forEach(a => a.classList.toggle('active', a.getAttribute('href') === '#' + id));
    };
    const onScroll = () => {
      let cur = heads[0]?.id;
      const top = window.scrollY + 140;
      for (const h of heads) if (h.offsetTop <= top) cur = h.id;
      if (window.innerHeight + window.scrollY >= document.body.scrollHeight - 80) cur = heads[heads.length - 1]?.id;
      if (cur) setActive(cur);
    };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
  })();

  /* ---------- OS-детект (страница скачивания) ---------- */
  (function detectOS() {
    const cards = document.querySelectorAll('.dl-card[data-os]');
    if (!cards.length) return;
    const ua = navigator.userAgent;
    const os = /Windows/i.test(ua) ? 'windows' : /Android/i.test(ua) ? 'android' : /Mac|iPhone|iPad|iPod/i.test(ua) ? 'mac' : null;
    if (!os) return;
    const target = document.querySelector('.dl-card[data-os="' + os + '"]');
    if (target) {
      cards.forEach(c => c.classList.remove('marked'));
      target.classList.add('marked');
    }
    const flag = document.getElementById('os-flag');
    if (flag) flag.value = os;
  })();

  /* ---------- копирование в буфер (ref-ссылки, коды) ---------- */
  document.querySelectorAll('[data-copy]').forEach(el => {
    el.addEventListener('click', () => {
      const text = el.getAttribute('data-copy') || '';
      navigator.clipboard?.writeText(text).then(() => {
        const old = el.textContent;
        el.textContent = 'Скопировано ✓';
        setTimeout(() => { el.textContent = old; }, 1400);
      });
    });
  });

  /* ---------- share-buttons ---------- */
  (function shareButtons() {
    const buttons = document.querySelectorAll('.share-btn');
    if (!buttons.length) return;

    const feedback = (el, text) => {
      if (!el.dataset.label) el.dataset.label = el.textContent;
      el.textContent = text;
      el.classList.add('is-done');
      clearTimeout(el._fbTimer);
      el._fbTimer = setTimeout(() => {
        el.textContent = el.dataset.label;
        el.classList.remove('is-done');
      }, 1800);
    };

    /* Открываем через настоящую ссылку — так надёжнее, чем window.open(). */
    const openUrl = (url) => {
      const a = document.createElement('a');
      a.href = url;
      a.target = '_blank';
      a.rel = 'noopener';
      document.body.appendChild(a);
      a.click();
      a.remove();
    };

    const copyText = async (text) => {
      try {
        if (navigator.clipboard && window.isSecureContext) {
          await navigator.clipboard.writeText(text);
          return true;
        }
      } catch (e) { /* переходим к запасному способу */ }
      try {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.top = '-1000px';
        document.body.appendChild(ta);
        ta.select();
        const ok = document.execCommand('copy');
        ta.remove();
        return ok;
      } catch (e) {
        return false;
      }
    };

    buttons.forEach(el => {
      el.addEventListener('click', async (ev) => {
        ev.preventDefault();
        const kind = el.getAttribute('data-share');
        const url = el.getAttribute('data-url') || location.href;
        const title = el.getAttribute('data-title') || document.title;

        if (kind === 'copy') {
          const ok = await copyText(url);
          feedback(el, ok ? 'Ссылка скопирована ✓' : 'Не удалось скопировать');
          return;
        }

        /* Нативная системная панель «Поделиться» (Windows, Android, iOS). */
        const shareData = { title: title, text: title, url: url };
        if (navigator.share && (!navigator.canShare || navigator.canShare(shareData))) {
          try {
            await navigator.share(shareData);
            return;
          } catch (err) {
            if (err && err.name === 'AbortError') return;
            /* иначе — провайдерский фолбэк ниже */
          }
        }

        let link = null;
        if (kind === 'telegram') link = 'https://t.me/share/url?url=' + encodeURIComponent(url) + '&text=' + encodeURIComponent(title);
        else if (kind === 'x') link = 'https://twitter.com/intent/tweet?url=' + encodeURIComponent(url) + '&text=' + encodeURIComponent(title);
        else if (kind === 'whatsapp') link = 'https://wa.me/?text=' + encodeURIComponent(title + ' ' + url);

        if (link) openUrl(link);
      });
    });
  })();

  /* ---------- smoth-scroll для якорей с учётом шапки ---------- */
  document.querySelectorAll('a[href^="#"]').forEach(a => {
    a.addEventListener('click', e => {
      const id = a.getAttribute('href');
      if (id.length < 2) return;
      const el = document.querySelector(id);
      if (!el) return;
      e.preventDefault();
      history.replaceState(null, '', id);
      const y = el.getBoundingClientRect().top + window.scrollY - 84;
      window.scrollTo({ top: y, behavior: 'smooth' });
    });
  });
})();
