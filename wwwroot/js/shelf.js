// Bookshelf page (Views/Home/Shelf.cshtml): books stand spine out on a 3D shelf; the active one turns to show its cover,
// and clicking the cover opens a detail sheet that grows out of it. One requestAnimationFrame loop plus the Web Animations API.
(() => {
  // Books built into the shelf. Their image is a photo of the whole book (back, spine, front); spine and cover are
  // boxes cut out of it: x, y, w, h in image pixels.
  const BUILT_IN = [
    {
      kind: 'sheet', title: 'The King in Yellow', author: 'R. W. Chambers', year: 1895, meta: '1895 / Horror',
      synopsis: 'A forbidden play passes from reader to reader, and anyone who reads it begins to lose their grip on the world they know.',
      image: '/shelf/king-in-yellow.png', size: [535, 400], spine: [230, 0, 68, 400], cover: [304, 0, 230, 400],
    },
  ];

  // The library's visible books with a cover (written into the page by HomeController.Shelf). They only have a front
  // cover image, so their spine is made here: a colour taken from the cover's left edge, the title and the author.
  let library = [];
  try { library = JSON.parse(document.getElementById('shelf-books')?.textContent || '[]'); } catch { library = []; }
  const fromLibrary = library.filter(b => b && b.cover).map(b => ({
    kind: 'cover', id: b.id, title: b.title || '', author: b.author || '', year: b.year ?? null,
    meta: [b.year, b.language || b.nationality].filter(Boolean).join(' / '),
    synopsis: b.summary || '', image: b.cover, url: b.url,
  }));

  // Oldest first; books without a year at the end
  const BOOKS = [...BUILT_IN, ...fromLibrary].sort((a, b) =>
    (a.year ?? 1e9) - (b.year ?? 1e9) || a.title.localeCompare(b.title));
  BOOKS.forEach((b, i) => {
    b.index = i;
    // width / height of the front cover; the real value is read when the image loads
    b.aspect = b.kind === 'sheet' ? b.cover[2] / b.cover[3] : 0.7;
    b.spineColour = '#2a2a2e';
    b.spineInk = '#f4f2ed';
  });
  const START = Math.max(0, BOOKS.findIndex(b => b.kind === 'sheet'));   // open on the built-in book

  const N = BOOKS.length;
  const multi = N > 1;                      // with one book there is nothing to scroll through
  const COPIES = multi ? 3 : 1, LOOP = N * COPIES;
  const $ = (q, el = document) => el.querySelector(q);
  const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const dur = ms => reduced ? 1 : ms;
  const EASE = 'cubic-bezier(.2,.8,.2,1)', SHEET_EASE = 'cubic-bezier(.76,0,.18,1)';
  const mod = (a, n) => ((a % n) + n) % n;
  const smooth = x => x * x * (3 - 2 * x);
  const clamp = (x, a, b) => Math.min(b, Math.max(a, x));

  // Load each cover once: learn its proportions and, for library books, the spine colour.
  function study(book) {
    const img = new Image();
    if (new URL(book.image, location.href).origin !== location.origin) img.crossOrigin = 'anonymous';
    img.onload = () => {
      const box = book.kind === 'sheet' ? book.cover : [0, 0, img.naturalWidth, img.naturalHeight];
      if (box[2] > 0 && box[3] > 0) book.aspect = clamp(box[2] / box[3], 0.45, 0.9);
      if (book.kind === 'cover') {
        try {
          const c = document.createElement('canvas'); c.width = 40; c.height = 60;
          const g = c.getContext('2d', { willReadFrequently: true });
          g.drawImage(img, 0, 0, 40, 60);
          const px = g.getImageData(0, 0, 5, 60).data;    // the strip along the left edge, next to where a spine would be
          let r = 0, gr = 0, bl = 0, n = 0;
          for (let k = 0; k < px.length; k += 4) { if (px[k + 3] < 16) continue; r += px[k]; gr += px[k + 1]; bl += px[k + 2]; n++; }
          if (n) {
            [r, gr, bl] = [r / n, gr / n, bl / n].map(v => Math.round(v * 0.82));   // a little darker, like a spine in shade
            book.spineColour = `rgb(${r}, ${gr}, ${bl})`;
            book.spineInk = (0.2126 * r + 0.7152 * gr + 0.0722 * bl) / 255 > 0.55 ? '#141414' : '#f4f2ed';
          }
        } catch { /* a cover from another site without CORS headers: keep the default spine colour */ }
      }
      applyBook(book);
    };
    img.onerror = () => applyBook(book);
    img.src = book.image;
  }

  // Cover and spine of one face. Built-in books cut both out of one photo; library books use the cover image as it is.
  function dress(t, book) {
    if (book.kind === 'sheet') {
      crop(t.sp, book, book.spine);
      crop(t.cv, book, book.cover);
      return;
    }
    t.cv.style.backgroundImage = `url(${JSON.stringify(book.image)})`;
    t.cv.classList.add('photo');
    t.sp.classList.add('gen');
    const title = document.createElement('span'), author = document.createElement('span'), mark = document.createElement('span');
    title.className = 'sp-title'; title.textContent = book.title;
    author.className = 'sp-author'; author.textContent = book.author;
    mark.className = 'sp-mark';
    t.sp.append(title, author, mark);
  }

  // Spine text: start at full size and shrink until the title (and author) fit the spine's length; only very long
  // titles still end in "...". Sizes are relative to the spine width, so this runs again after a resize.
  function fitSpines() {
    for (const book of BOOKS) {
      if (book.kind !== 'cover') continue;
      const copies = tapes.filter(t => t.i === book.index);
      for (const [cls, base] of [['.sp-title', 0.25], ['.sp-author', 0.17]]) {
        const el = copies[0].sp.querySelector(cls);
        let fit = base;
        el.style.setProperty('--fit', fit);
        while (el.scrollHeight > el.clientHeight && fit > base * 0.6) {   // even 1px over shows "..."
          fit -= 0.01;
          el.style.setProperty('--fit', fit.toFixed(3));
        }
        for (const t of copies) t.sp.querySelector(cls).style.setProperty('--fit', fit.toFixed(3));
      }
    }
  }

  // Width of a book's cover face (it is also the depth of the box), from the cover's proportions
  const widthOf = book => H * book.aspect;
  function applyBook(book) {
    for (const t of tapes) if (t.i === book.index) {
      t.el.style.setProperty('--d', widthOf(book) + 'px');
      t.sp.style.setProperty('--spine', book.spineColour);
      t.sp.style.setProperty('--spine-ink', book.spineInk);
    }
  }

  // Stretch one box of the image over a face. A box as tall (or wide) as the image has no slack, so avoid 0/0.
  function crop(el, book, box) {
    const [W, H] = book.size, [x, y, w, h] = box;
    el.style.backgroundImage = `url("${book.image}")`;
    el.style.backgroundSize = `${W / w * 100}% ${H / h * 100}%`;
    el.style.backgroundPosition = `${W - w ? x / (W - w) * 100 : 0}% ${H - h ? y / (H - h) * 100 : 0}%`;
  }

  const stage = $('.stage'), shelf = $('.shelf');
  const tapes = [];
  let hover = -1, suppressClick = false, isOpen = false, closing = false;
  for (let c = 0; c < COPIES; c++) for (let i = 0; i < N; i++) {
    const v = c * N + i, el = document.createElement('div');
    el.className = 'tape';
    const sp = document.createElement('div'), cv = document.createElement('div');
    sp.className = 'face sp'; cv.className = 'face cv';
    el.append(sp, cv); stage.append(el);
    el.addEventListener('pointerenter', () => { hover = v; });
    el.addEventListener('pointerleave', () => { if (hover === v) hover = -1; });
    el.addEventListener('click', () => {
      if (suppressClick || isOpen) return;
      const r = rel(v, s);
      if (Math.abs(r) < .5) openDetail(); else T = Math.round(T + r);
    });
    const t = { v, i, el, sp, cv, lift: 0 };
    dress(t, BOOKS[i]);
    tapes.push(t);
  }

  let s = multi ? N + START : 0, T = s, active = -1;
  let mx = innerWidth / 2, my = innerHeight / 2;
  let tiltX = 0, tiltY = 0, onCover = false;
  let H = 0, ws = 0, d = 0, gap = 0, W = innerWidth, VH = innerHeight;
  let dragging = false, drag = null;

  function measure() {
    H = $('.probe').getBoundingClientRect().height;
    ws = H * 142 / 840; gap = H * .03;
    W = innerWidth; VH = innerHeight;
    stage.style.setProperty('--ws', ws + 'px');
    BOOKS.forEach(applyBook);
  }
  const rel = (v, at) => { let r = mod(v - at, LOOP); if (r > LOOP / 2) r -= LOOP; return r; };
  const B = r => Math.abs(r) < 1 ? (r + Math.sin(Math.PI * r) / Math.PI) / 2 : Math.sign(r) * .5;
  // d: width of the open cover, which pushes its neighbours apart (blended between two books while the shelf moves)
  const F = r => r * (ws + gap) + (d - ws + 1.6 * gap) * B(r);

  // Caption: each line slides through its own mask when the book changes
  const capTitle = $('.c-title').parentNode, capSub = $('.c-sub').parentNode, capYear = $('.c-year').parentNode;
  function swap(mask, cls, text, dir, instant) {
    const span = document.createElement('span');
    span.className = cls; span.textContent = text;
    const old = [...mask.children];
    mask.append(span);
    if (instant || reduced) { old.forEach(o => o.remove()); return; }
    old.forEach(o => {
      if (o.dataset.leaving) return;
      o.dataset.leaving = 1;
      const from = getComputedStyle(o).transform;
      o.getAnimations().forEach(a => a.cancel());
      o.animate([{ transform: from === 'none' ? 'translateY(0)' : from }, { transform: `translateY(${-dir * 105}%)` }],
        { duration: 650, easing: EASE, fill: 'both' }).onfinish = () => o.remove();
    });
    span.animate([{ transform: `translateY(${dir * 105}%)` }, { transform: 'translateY(0)' }], { duration: 650, easing: EASE, fill: 'both' });
  }
  function setActive(i, dir, instant) {
    active = i;
    const b = BOOKS[i];
    swap(capTitle, 'c-title', b.title, dir, instant);
    swap(capSub, 'c-sub', b.author, dir, instant);
    swap(capYear, 'c-year', b.year ? String(b.year) : '', dir, instant);
  }

  // Navigation, only when there is more than one book
  const prev = $('.arrow.prev'), next = $('.arrow.next'), play = $('.play');
  prev.hidden = next.hidden = !multi;
  prev.addEventListener('click', () => { if (!isOpen) T = Math.round(T) - 1; });
  next.addEventListener('click', () => { if (!isOpen) T = Math.round(T) + 1; });
  play.addEventListener('click', () => openDetail());
  addEventListener('keydown', e => {
    if (e.key === 'Escape') { closeDetail(); return; }
    if (isOpen) return;
    if (multi && e.key === 'ArrowLeft') { T = Math.round(T) - 1; e.preventDefault(); }
    else if (multi && e.key === 'ArrowRight') { T = Math.round(T) + 1; e.preventDefault(); }
    else if (e.key === 'Enter' && (document.activeElement === document.body || !document.activeElement)) openDetail();
  });

  let wheelTimer = 0;
  addEventListener('wheel', e => {
    if (isOpen || !multi) return;
    e.preventDefault();
    const delta = Math.abs(e.deltaX) > Math.abs(e.deltaY) ? e.deltaX : e.deltaY;
    T += delta / (ws + gap) * .5;
    clearTimeout(wheelTimer);
    wheelTimer = setTimeout(() => { T = Math.round(T); }, 160);
  }, { passive: false });

  shelf.addEventListener('pointerdown', e => {
    if (isOpen || !multi || e.button > 0) return;
    drag = { x: e.clientX, T0: T, moved: false };
  });
  addEventListener('pointermove', e => {
    mx = e.clientX; my = e.clientY;
    if (!drag) return;
    const dx = e.clientX - drag.x;
    if (!drag.moved && Math.abs(dx) > 5) { drag.moved = true; dragging = true; }
    if (drag.moved) T = drag.T0 - dx / (ws + gap);
  });
  addEventListener('pointerup', () => {
    if (!drag) return;
    if (drag.moved) { T = Math.round(T); suppressClick = true; setTimeout(() => { suppressClick = false; }, 0); }
    drag = null; dragging = false;
  });
  addEventListener('pointercancel', () => { if (drag) { T = Math.round(T); drag = null; dragging = false; } });

  // Detail sheet: grows out of the cover, text rises, the cover bleeds in through an ink mask
  const detail = $('.detail'), pimg = $('.pimg', detail), poster = $('svg.poster', detail), blobs = [...detail.querySelectorAll('circle')];
  let inkRaf = 0;
  function coverInset() {
    const tape = tapes[mod(Math.round(T), LOOP)];
    const r = tape.cv.getBoundingClientRect();
    const p = 2200 / (2200 - .2 * H);       // the open cover is pulled 0.2 H towards the viewer
    const w = Math.min(r.width, widthOf(BOOKS[tape.i]) * p), h = Math.min(r.height, H * p);
    const ccx = r.left + r.width / 2, ccy = r.top + r.height / 2;
    return { ccx, ccy, inset: `inset(${ccy - h / 2}px ${W - ccx - w / 2}px ${VH - ccy - h / 2}px ${ccx - w / 2}px round 3px)` };
  }
  function openDetail() {
    if (isOpen || closing) return;
    isOpen = true; T = Math.round(T);
    const b = BOOKS[mod(T, N)];
    $('.d-sub', detail).textContent = b.author;
    $('.d-title', detail).textContent = b.title;
    $('.d-meta', detail).textContent = b.meta;
    $('.d-syn', detail).textContent = b.synopsis;
    $('.d-credit', detail).textContent = b.author ? `Written by ${b.author}.` : '';
    const cta = $('.cta', detail);
    cta.textContent = b.url ? 'Open the book' : 'Browse the library';
    cta.href = b.url || '/Library';
    // The poster takes the cover's proportions: 474 wide, as tall as the cover needs
    const PW = 474, PH = Math.round(PW / b.aspect);
    poster.setAttribute('viewBox', `0 0 ${PW} ${PH}`);
    poster.parentNode.style.aspectRatio = `${PW} / ${PH}`;
    $('#pclip rect', poster).setAttribute('height', PH);
    $('#inkm', poster).setAttribute('height', PH + 400);
    pimg.setAttribute('href', b.image);
    if (b.kind === 'sheet') {
      const [SW, SH] = b.size, [x, y, w, h] = b.cover;
      pimg.setAttribute('preserveAspectRatio', 'none');
      pimg.setAttribute('x', -x * PW / w); pimg.setAttribute('y', -y * PH / h);
      pimg.setAttribute('width', SW * PW / w); pimg.setAttribute('height', SH * PH / h);
    } else {
      pimg.setAttribute('preserveAspectRatio', 'xMidYMid slice');
      pimg.setAttribute('x', 0); pimg.setAttribute('y', 0);
      pimg.setAttribute('width', PW); pimg.setAttribute('height', PH);
    }
    blobs.forEach(c => c.setAttribute('r', 0));
    const { ccx, ccy, inset } = coverInset();
    detail.getAnimations({ subtree: true }).forEach(a => a.cancel());
    detail.style.visibility = 'visible';
    detail.setAttribute('aria-hidden', 'false');
    detail.scrollTop = 0;
    detail.style.transformOrigin = `${ccx}px ${ccy}px`;
    detail.animate([
      { clipPath: inset, transform: 'rotate(-4.5deg)' },
      { clipPath: 'inset(0px 0px 0px 0px round 0px)', transform: 'rotate(0deg)' }
    ], { duration: dur(1050), easing: SHEET_EASE, fill: 'both' });
    detail.querySelectorAll('.m > span').forEach((el, i) => {
      el.animate([{ transform: 'translateY(110%)' }, { transform: 'translateY(0)' }],
        { duration: dur(900), delay: reduced ? 0 : 620 + i * 75, easing: EASE, fill: 'both' });
    });
    const start = performance.now() + (reduced ? -5000 : 520);
    cancelAnimationFrame(inkRaf);
    const ink = now => {
      const t = now - start;
      blobs.forEach(c => {
        const p = clamp((t - +c.dataset.delay) / 1250, 0, 1);
        c.setAttribute('r', (720 * (1 - Math.pow(1 - p, 3))).toFixed(1));
      });
      if (t < 2400) inkRaf = requestAnimationFrame(ink);
    };
    inkRaf = requestAnimationFrame(ink);
    $('.d-close', detail).focus({ preventScroll: true });
  }
  function closeDetail() {
    if (!isOpen || closing) return;
    closing = true;
    const { ccx, ccy, inset } = coverInset();
    detail.style.transformOrigin = `${ccx}px ${ccy}px`;
    detail.getAnimations().forEach(a => a.cancel());
    detail.animate([
      { clipPath: 'inset(0px 0px 0px 0px round 0px)', transform: 'rotate(0deg)' },
      { clipPath: inset, transform: 'rotate(-4.5deg)' }
    ], { duration: dur(850), easing: SHEET_EASE, fill: 'both' }).onfinish = () => {
      detail.style.visibility = 'hidden';
      detail.setAttribute('aria-hidden', 'true');
      cancelAnimationFrame(inkRaf);
      detail.getAnimations({ subtree: true }).forEach(a => a.cancel());
      isOpen = false; closing = false;
      if (document.activeElement) document.activeElement.blur();
    };
  }
  $('.d-close', detail).addEventListener('click', closeDetail);
  shelf.addEventListener('click', e => { if (onCover && !suppressClick && !e.target.closest('.tape')) openDetail(); });

  let last = performance.now();
  function frame(now) {
    const dt = Math.min(.05, (now - last) / 1000); last = now;
    const k = dragging ? 18 : 5.2;
    s += (T - s) * (reduced ? 1 : 1 - Math.exp(-k * dt));
    if (Math.abs(T - s) < 1e-4) s = T;
    if (Math.abs(s) > 4 * LOOP) { const sh = Math.round(s / LOOP) * LOOP; s -= sh; T -= sh; if (drag) drag.T0 -= sh; }

    const a = mod(Math.round(s), N);
    if (a !== active) setActive(a, Math.sign(T - s) || 1);

    const shelfY = VH / 2;
    // The open book's cover width; while moving, blend between the two books either side of the centre
    const i0 = Math.floor(s), f = s - i0;
    const d0 = widthOf(BOOKS[mod(i0, N)]), d1 = widthOf(BOOKS[mod(i0 + 1, N)]);
    d = d0 + (d1 - d0) * smooth(f);

    onCover = !isOpen && Math.abs(mx - W / 2) < .55 * d && Math.abs(my - shelfY) < .55 * H && Math.abs(T - s) < .3;
    let tY, tX;
    if (onCover) { tY = (mx - W / 2) / (.55 * d) * 9; tX = -(my - shelfY) / (H / 2) * 6; }
    else { tY = (mx - W / 2) / W * 5; tX = 0; }
    const kt = 1 - Math.exp(-7 * dt);
    tiltY += (tY - tiltY) * kt; tiltX += (tX - tiltX) * kt;

    const kl = reduced ? 1 : 1 - Math.exp(-12 * dt);
    const lim = W / 2 + 1.5 * H * 0.9;   // 0.9: the widest cover allowed
    for (const t of tapes) {
      const r = rel(t.v, s);
      const target = hover === t.v && Math.abs(r) > .5 && !isOpen ? 1 : 0;
      t.lift += (target - t.lift) * kl;
      const x = F(r);
      if (Math.abs(x) > lim) { if (!t.hidden) { t.el.style.visibility = 'hidden'; t.hidden = true; } continue; }
      if (t.hidden) { t.el.style.visibility = ''; t.hidden = false; }
      const o = smooth(Math.max(0, 1 - Math.abs(r)));
      const y = -o * .025 * H - t.lift * .045 * H, z = o * .2 * H;
      t.el.style.transform = `translate3d(${x}px,${y}px,${z}px) rotateZ(${-4.5 * o}deg) rotateX(${tiltX * o}deg) rotateY(${-90 * o + tiltY * o}deg)`;
      t.el.style.zIndex = Math.round(100 - Math.abs(r) * 4);
      t.sp.style.setProperty('--shade', (o * .55).toFixed(3));
      t.cv.style.setProperty('--shade', ((1 - o) * .5).toFixed(3));
    }

    const ps = isOpen ? 0 : smooth(Math.max(0, 1 - Math.abs(T - s) * 3));
    play.style.transform = `translate3d(${W / 2 - .5 * d - 6}px,${shelfY + .36 * H}px,0) scale(${ps})`;

    // The normal mouse arrow is used; the clickable cover shows a pointing hand (see shelf.css)
    shelf.classList.toggle('on-cover', onCover);

    requestAnimationFrame(frame);
  }

  addEventListener('resize', () => { measure(); fitSpines(); });
  measure();
  fitSpines();
  if (document.fonts) document.fonts.ready.then(fitSpines);   // again once Inter has loaded (different widths)
  BOOKS.forEach(study);
  setActive(mod(Math.round(s), N), 1, true);
  requestAnimationFrame(frame);
})();
