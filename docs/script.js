(() => {
  'use strict';
  const languageSwitch = document.querySelector('[data-language-switch]');
  const alternatePage = new URL(languageSwitch.href);
  function syncLanguageLink() {
    alternatePage.hash = location.hash;
    languageSwitch.href = alternatePage.href;
  }
  syncLanguageLink();
  window.addEventListener('hashchange', syncLanguageLink);

  const tabs = [...document.querySelectorAll('[role="tab"]')];
  const panels = [...document.querySelectorAll('[role="tabpanel"]')];
  function selectTab(index) {
    tabs.forEach((tab, i) => {
      tab.setAttribute('aria-selected', String(i === index));
      tab.tabIndex = i === index ? 0 : -1;
      panels[i].hidden = i !== index;
    });
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => selectTab(index));
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
      else if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = tabs.length - 1;
      else return;
      event.preventDefault();
      selectTab(next);
      tabs[next].focus();
    });
  });
  function showLinkedPanel() {
    const index = panels.findIndex(panel => `#${panel.id}` === location.hash);
    if (index < 0) return;
    selectTab(index);
    panels[index].scrollIntoView();
  }
  showLinkedPanel();
  window.addEventListener('hashchange', showLinkedPanel);
  const dialog = document.getElementById('image-dialog');
  const dialogImage = document.getElementById('dialog-image');
  document.querySelectorAll('[data-image]').forEach(button => {
    button.addEventListener('click', () => {
      dialogImage.src = button.dataset.image;
      dialogImage.alt = button.querySelector('img').alt;
      document.getElementById('dialog-title').textContent = button.dataset.title;
      dialog.showModal();
    });
  });
  document.getElementById('close-dialog').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', event => {
    if (event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) dialog.close();
  });
  if ('IntersectionObserver' in window && !window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        entry.target.classList.add('is-visible');
        observer.unobserve(entry.target);
      });
    }, { threshold: 0.08 });
    document.documentElement.classList.add('motion-ready');
    document.querySelectorAll('.reveal').forEach(element => observer.observe(element));
  }
})();
