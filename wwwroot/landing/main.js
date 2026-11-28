// Mobile menu of the shared header (Views/Shared/_VibeHeader.cshtml), on the landing page and the dark library.
(() => {
  // Log in / register pages: the provider buttons Identity renders just say "Google"; say what they do.
  document.querySelectorAll('button[name="provider"]').forEach(b => { b.textContent = 'Continue with ' + b.value; });

  const burger = document.querySelector('.vh-burger');
  const menu = document.getElementById('vh-menu');
  const overlay = document.querySelector('.vh-overlay');
  if (!burger || !menu || !overlay) return;

  function setOpen(open) {
    burger.setAttribute('aria-expanded', String(open));
    burger.setAttribute('aria-label', open ? 'Close menu' : 'Menu');
    menu.hidden = !open;
    overlay.hidden = !open;
    document.body.classList.toggle('menu-open', open);
    if (open) menu.querySelector('a, button')?.focus({ preventScroll: true });
  }

  burger.addEventListener('click', () => setOpen(menu.hidden));
  overlay.addEventListener('click', () => setOpen(false));
  menu.addEventListener('click', e => { if (e.target.closest('a')) setOpen(false); });
  addEventListener('keydown', e => {
    if (e.key === 'Escape' && !menu.hidden) { setOpen(false); burger.focus(); }
  });
  addEventListener('resize', () => { if (innerWidth > 720 && !menu.hidden) setOpen(false); });
})();
