/* =========================================================
   Inkwell landing — interactions
   Vanilla JS. No deps. Drop into wwwroot/js/landing.js.
   ========================================================= */
(function () {
  'use strict';

  const root = document.querySelector('.inkwell-root');
  const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ──────────────────────────────────────────────────────────
  // 1) Scroll progress hairline
  // ──────────────────────────────────────────────────────────
  const progressEl = document.getElementById('scrollProgress');
  function onScroll() {
    const h = document.documentElement;
    const max = h.scrollHeight - h.clientHeight;
    const pct = max > 0 ? (h.scrollTop / max) * 100 : 0;
    progressEl.style.width = pct + '%';
  }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  // ──────────────────────────────────────────────────────────
  // 2) Scroll-reveal — IntersectionObserver
  // ──────────────────────────────────────────────────────────
  const reveals = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !prefersReduced) {
    const io = new IntersectionObserver((entries) => {
      entries.forEach(e => {
        if (e.isIntersecting) {
          e.target.classList.add('in');
          io.unobserve(e.target);
        }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.12 });
    reveals.forEach(el => io.observe(el));
  } else {
    reveals.forEach(el => el.classList.add('in'));
  }

  // ──────────────────────────────────────────────────────────
  // 3) Hero ink-cursor (subtle dot following pointer)
  // ──────────────────────────────────────────────────────────
  const hero = document.getElementById('hero');
  const inkCursor = document.getElementById('inkCursor');
  if (hero && inkCursor && !prefersReduced) {
    hero.addEventListener('pointermove', (e) => {
      const r = hero.getBoundingClientRect();
      inkCursor.style.left = (e.clientX - r.left) + 'px';
      inkCursor.style.top  = (e.clientY - r.top)  + 'px';
    });
  }

  // ──────────────────────────────────────────────────────────
  // 4) Typing demo in the hero issue mockup
  // ──────────────────────────────────────────────────────────
  const typingEl = document.getElementById('typingLede');
  if (typingEl) {
    const lines = [
      "The first draft is a private negotiation with the page.",
      "The second is where the writer meets the reader.",
      "And the third — the third is where Inkwell, at its quietest, helps most."
    ];
    let li = 0, ci = 0, deleting = false;
    function type() {
      if (prefersReduced) {
        typingEl.innerHTML = lines.join(' ') + '<span class="typing-cursor"></span>';
        return;
      }
      const cur = lines[li];
      if (!deleting) {
        ci++;
        if (ci > cur.length) {
          deleting = true;
          setTimeout(type, 1800);
          return;
        }
      } else {
        ci--;
        if (ci === 0) {
          deleting = false;
          li = (li + 1) % lines.length;
        }
      }
      typingEl.innerHTML = cur.slice(0, ci) + '<span class="typing-cursor"></span>';
      setTimeout(type, deleting ? 18 : 32 + Math.random() * 28);
    }
    setTimeout(type, 1400);
  }

  // ──────────────────────────────────────────────────────────
  // 5) Stats counter (counts up when in view)
  // ──────────────────────────────────────────────────────────
  const stats = document.querySelectorAll('.stats .num');
  if (stats.length && 'IntersectionObserver' in window) {
    const sio = new IntersectionObserver((entries) => {
      entries.forEach(e => {
        if (!e.isIntersecting) return;
        const el = e.target;
        const target = parseInt(el.getAttribute('data-count'), 10);
        const suf = el.getAttribute('data-suf') || '';
        const dur = 1400;
        const start = performance.now();
        function tick(now) {
          const t = Math.min(1, (now - start) / dur);
          const eased = 1 - Math.pow(1 - t, 3);
          const v = Math.round(target * eased);
          el.innerHTML = v.toLocaleString() + (suf ? '<span class="suf">' + suf + '</span>' : '');
          if (t < 1) requestAnimationFrame(tick);
        }
        requestAnimationFrame(tick);
        sio.unobserve(el);
      });
    }, { threshold: 0.4 });
    stats.forEach(s => sio.observe(s));
  }

  // ──────────────────────────────────────────────────────────
  // 6) Audience toggle (hero lede swap)
  // ──────────────────────────────────────────────────────────
  (function () {
    const ledes = {
      writer: "An open-source publishing platform for independent writers who want full ownership of their words — and a literary magazine for a website.",
      agency: "Run a hundred client blogs from a single deploy. Multi-tenant by design, white-labelled, with a Layout commissioned just for your studio.",
      dev:    "A .NET 8 application licensed under MIT. Clone the repo, set a connection string, run. Razor views, EF Core, full source — no surprises."
    };
    const btns = document.querySelectorAll('.audience button');
    const lede = document.querySelector('[data-lede]');
    btns.forEach(b => b.addEventListener('click', () => {
      btns.forEach(x => x.classList.remove('active'));
      b.classList.add('active');
      if (lede) lede.textContent = ledes[b.getAttribute('data-aud')] || ledes.writer;
    }));
  })();

  // ──────────────────────────────────────────────────────────
  // 7) Layouts theatre — clickable list swaps the schematic
  // ──────────────────────────────────────────────────────────
  const LAYOUTS = {
    feed:       { name: 'The <em>Feed</em>',        family: 'Modern',    use: 'Personal blogs',   pair: 'Slate · Sand · Press',
      blurb: 'Reverse-chronological stream of essays, image-led. The default modern Layout — the one most blogs start with.',
      canvas: `
        <div style="height:14px; background:var(--ink-black);"></div>
        <div style="display:flex; gap:12px; align-items:flex-start; margin-top:6px;">
          <div style="flex:1.6; aspect-ratio:16/10; background:var(--ink-sepia); opacity:.7;"></div>
          <div style="flex:1; display:flex; flex-direction:column; gap:6px; padding-top:4px;">
            <div style="height:10px; background:var(--ink-black); opacity:.7; width:75%;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22; width:60%;"></div>
            <div style="height:1px; background:var(--ink-sepia); margin:6px 0;"></div>
            <div style="height:8px; background:var(--ink-black); opacity:.7; width:65%;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22; width:80%;"></div>
          </div>
        </div>
      `
    },
    grid: { name: 'The <em>Grid</em>', family: 'Modern', use: 'Studios, photo essays', pair: 'Slate · Mono · Forest',
      blurb: 'Card grid with even cadence and image-led tiles. For studios and visual writers who publish in series.',
      canvas: `
        <div style="height:14px; background:var(--ink-black);"></div>
        <div style="display:grid; grid-template-columns:repeat(3, 1fr); gap:10px; margin-top:10px; flex:1;">
          <div style="background:var(--ink-sepia); opacity:.6; aspect-ratio:4/3;"></div>
          <div style="background:var(--ink-sepia); opacity:.45; aspect-ratio:4/3;"></div>
          <div style="background:var(--ink-sepia); opacity:.7; aspect-ratio:4/3;"></div>
          <div style="background:var(--ink-sepia); opacity:.5; aspect-ratio:4/3;"></div>
          <div style="background:var(--ink-sepia); opacity:.65; aspect-ratio:4/3;"></div>
          <div style="background:var(--ink-sepia); opacity:.35; aspect-ratio:4/3;"></div>
        </div>
      `
    },
    reader: { name: 'The <em>Reader</em>', family: 'Modern', use: 'Essayists, single voice', pair: 'Cream · Manuscript · Press',
      blurb: 'Single column, narrow measure, no chrome. The most concentrated reading experience Inkwell ships.',
      canvas: `
        <div style="max-width:380px; margin:0 auto; display:flex; flex-direction:column; gap:8px; padding-top:18px;">
          <div style="height:14px; background:var(--ink-black); width:60%;"></div>
          <div style="height:5px; background:var(--ink-sepia); width:30%;"></div>
          <div style="height:1px; background:var(--ink-sepia); margin:8px 0;"></div>
          ${Array(9).fill('<div style="height:4px; background:var(--ink-black); opacity:.22;"></div>').join('')}
          <div style="height:4px; background:var(--ink-black); opacity:.22; width:60%;"></div>
        </div>
      `
    },
    showcase: { name: 'The <em>Showcase</em>', family: 'Modern', use: 'Portfolios, agencies', pair: 'Mono · Slate · Onyx',
      blurb: 'A hero feature with a supporting trio. Portfolio energy, with editorial restraint.',
      canvas: `
        <div style="aspect-ratio:16/8; background:var(--ink-sepia); opacity:.75;"></div>
        <div style="display:grid; grid-template-columns:repeat(3, 1fr); gap:10px; margin-top:10px;">
          <div style="aspect-ratio:4/3; background:var(--ink-sepia); opacity:.5;"></div>
          <div style="aspect-ratio:4/3; background:var(--ink-sepia); opacity:.4;"></div>
          <div style="aspect-ratio:4/3; background:var(--ink-sepia); opacity:.6;"></div>
        </div>
      `
    },
    bento: { name: 'The <em>Bento</em>', family: 'Modern', use: 'Mixed-media authors', pair: 'Plum · Forest · Sand',
      blurb: 'Mosaic of mixed-size cards — text, image, quote. Playful, modern, varied.',
      canvas: `
        <div style="display:grid; grid-template-columns:2fr 1fr 1fr; grid-template-rows:1fr 1fr; gap:10px; flex:1;">
          <div style="grid-row:1/3; background:var(--ink-sepia); opacity:.7;"></div>
          <div style="background:var(--ink-sepia); opacity:.5;"></div>
          <div style="background:var(--ink-sepia); opacity:.4;"></div>
          <div style="background:var(--ink-sepia); opacity:.6;"></div>
          <div style="background:var(--ink-sepia); opacity:.5;"></div>
        </div>
      `
    },
    timeline: { name: 'The <em>Timeline</em>', family: 'Modern', use: 'Changelogs, journals', pair: 'Slate · Press · Forest',
      blurb: 'Vertical timeline of moments. Changelog-friendly, journal-friendly — for writing that builds.',
      canvas: `
        <div style="display:flex; gap:24px; padding-top:8px;">
          <div style="width:28px; display:flex; flex-direction:column; align-items:center; gap:14px; padding-top:6px;">
            ${Array(5).fill('<span style="width:8px; height:8px; border-radius:50%; background:var(--ink-black);"></span>').join('<span style="width:1px; flex:1; background:var(--ink-sepia); min-height:24px;"></span>')}
          </div>
          <div style="flex:1; display:flex; flex-direction:column; gap:22px;">
            ${Array(5).fill(`
              <div>
                <div style="height:8px; background:var(--ink-black); opacity:.7; width:55%; margin-bottom:5px;"></div>
                <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
                <div style="height:3px; background:var(--ink-black); opacity:.22; margin-top:3px; width:80%;"></div>
              </div>
            `).join('')}
          </div>
        </div>
      `
    },
    stream: { name: 'The <em>Stream</em>', family: 'Modern', use: 'Microblogs, threads', pair: 'Sand · Mono · Plum',
      blurb: 'Microblog + threaded notes. Short-form, conversational — the contemporary blog at its loosest.',
      canvas: `
        <div style="display:flex; gap:16px; flex:1;">
          <div style="flex:2; display:flex; flex-direction:column; gap:14px;">
            ${Array(4).fill(`
              <div style="border-left:2px solid var(--ink-sepia); padding-left:14px;">
                <div style="height:7px; background:var(--ink-black); opacity:.7; width:40%; margin-bottom:6px;"></div>
                <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
                <div style="height:3px; background:var(--ink-black); opacity:.22; width:90%; margin-top:3px;"></div>
              </div>
            `).join('')}
          </div>
          <div style="flex:1; display:flex; flex-direction:column; gap:8px;">
            <div style="height:9px; background:var(--ink-black); opacity:.6; width:60%;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:70%;"></div>
          </div>
        </div>
      `
    },
    newsletter: { name: 'The <em>Newsletter</em>', family: 'Modern', use: 'Subscriber-led blogs', pair: 'Foxglove · Letterpress · Cream',
      blurb: 'Single-issue Layout with a subscribe block on top. For when you would rather be sent than scrolled to.',
      canvas: `
        <div style="display:flex; flex-direction:column; align-items:center; gap:14px; padding:24px 60px;">
          <div style="font-family:var(--serif); font-style:italic; color:var(--ink-sepia); font-size:20px;">✉</div>
          <div style="height:14px; background:var(--ink-black); width:70%;"></div>
          <div style="height:5px; background:var(--ink-sepia); width:40%;"></div>
          <div style="height:32px; width:80%; border:1px solid var(--ink-black); display:flex; align-items:center; padding:0 10px;">
            <div style="height:4px; background:var(--ink-black); opacity:.3; flex:1;"></div>
            <div style="height:18px; width:80px; background:var(--ink-black); margin-left:8px;"></div>
          </div>
          <div style="width:100%; display:flex; flex-direction:column; gap:5px; margin-top:6px;">
            <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:70%;"></div>
          </div>
        </div>
      `
    },
    magazine: { name: 'The <em>Magazine</em>', family: 'Editorial', use: 'Multi-author publications', pair: 'Letterpress · Manuscript · Folio',
      blurb: 'Issue masthead, hero feature, multi-author. The New Yorker, online — the kind of homepage that announces.',
      canvas: `
        <div style="display:flex; justify-content:space-between; font-family:var(--serif); font-style:italic; font-size:14px; color:var(--ink-sepia); padding-bottom:6px;">
          <span>The Magazine · Vol. III</span><span>Issue 12</span>
        </div>
        <div style="height:2px; background:var(--ink-black);"></div>
        <div style="display:flex; gap:14px; margin-top:10px; flex:1;">
          <div style="flex:1.5; aspect-ratio:4/3; background:var(--ink-sepia); opacity:.75;"></div>
          <div style="flex:1; display:flex; flex-direction:column; gap:6px;">
            <div style="font-family:var(--serif); font-style:italic; font-size:12px; color:var(--ink-sepia);">Cover essay</div>
            <div style="height:11px; background:var(--ink-black); opacity:.7; width:85%;"></div>
            <div style="height:11px; background:var(--ink-black); opacity:.7; width:65%;"></div>
            <div style="height:1px; background:var(--ink-sepia); margin:5px 0;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22;"></div>
            <div style="height:4px; background:var(--ink-black); opacity:.22; width:80%;"></div>
          </div>
        </div>
      `
    },
    journal: { name: 'The <em>Journal</em>', family: 'Editorial', use: 'Monthly long-form', pair: 'Cream · Manuscript · Ink',
      blurb: 'Single column, sidebar of recent letters. Quiet, monthly, long-form — for writing that wants room.',
      canvas: `
        <div style="display:flex; gap:24px; flex:1;">
          <div style="flex:2; display:flex; flex-direction:column; gap:7px;">
            <div style="font-family:var(--serif); font-style:italic; font-size:12px; color:var(--ink-sepia);">No. 47 · April</div>
            <div style="height:13px; background:var(--ink-black); width:75%;"></div>
            <div style="height:1px; background:var(--ink-sepia); margin:3px 0;"></div>
            ${Array(7).fill('<div style="height:3px; background:var(--ink-black); opacity:.22;"></div>').join('')}
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:60%;"></div>
          </div>
          <div style="flex:1; border-left:1px solid var(--ink-sepia); padding-left:14px; display:flex; flex-direction:column; gap:8px;">
            <div style="font-family:var(--sans); font-size:9px; letter-spacing:0.14em; text-transform:uppercase; color:var(--ink-sepia);">Recent</div>
            ${Array(4).fill('<div style="display:flex; gap:4px; flex-direction:column;"><div style="height:6px; background:var(--ink-black); opacity:.6;"></div><div style="height:3px; background:var(--ink-black); opacity:.22; width:80%;"></div></div>').join('')}
          </div>
        </div>
      `
    },
    notebook: { name: 'The <em>Notebook</em>', family: 'Editorial', use: 'Scholars, footnoters', pair: 'Cobalt · Manuscript · Press',
      blurb: 'Marginalia, citations, no imagery. Reader-first, footnote-friendly — for writing that argues.',
      canvas: `
        <div style="display:flex; gap:18px; flex:1;">
          <div style="flex:1; border-right:1px dotted var(--ink-sepia); padding-right:14px; display:flex; flex-direction:column; gap:8px;">
            <div style="font-family:var(--serif); font-style:italic; font-size:11px; color:var(--ink-sepia);">Marginalia</div>
            ${Array(5).fill('<div style="font-family:var(--serif); font-style:italic; font-size:10px; color:var(--ink-sepia);">¶ a note ·</div>').join('')}
          </div>
          <div style="flex:3; display:flex; flex-direction:column; gap:6px;">
            <div style="height:12px; background:var(--ink-black); width:70%;"></div>
            ${Array(9).fill('<div style="height:3px; background:var(--ink-black); opacity:.22;"></div>').join('')}
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:70%;"></div>
          </div>
        </div>
      `
    },
    studio: { name: 'The <em>Studio</em>', family: 'Editorial', use: 'Visual essays, photo journals', pair: 'Folio · Linen · Onyx',
      blurb: 'Cover-heavy masonry. Are.na meets Ghost — for photographers and visual essayists.',
      canvas: `
        <div style="display:grid; grid-template-columns:1.4fr 1fr 1fr; grid-template-rows:1fr 1fr; gap:10px; flex:1;">
          <div style="grid-row:1/3; background:var(--ink-sepia); opacity:.7;"></div>
          <div style="background:var(--ink-sepia); opacity:.5;"></div>
          <div style="background:var(--ink-sepia); opacity:.6;"></div>
          <div style="background:var(--ink-sepia); opacity:.45;"></div>
          <div style="background:var(--ink-sepia); opacity:.55;"></div>
        </div>
      `
    },
    broadsheet: { name: 'The <em>Broadsheet</em>', family: 'Editorial', use: 'Newspapers, dispatches', pair: 'Press · Letterpress · Linen',
      blurb: 'Multi-column newspaper. Type-forward, dense, ruled. For newsletters that miss being newspapers.',
      canvas: `
        <div style="text-align:center; font-family:var(--serif); font-style:italic; font-weight:500; font-size:22px; color:var(--ink-black); border-bottom:2px solid var(--ink-black); padding-bottom:6px;">The Broadsheet</div>
        <div style="display:flex; justify-content:space-between; font-family:var(--sans); font-size:9px; letter-spacing:.14em; color:var(--ink-sepia); padding:4px 0 10px;"><span>Vol. III · No. 12</span><span>Saturday</span><span>March 14, 2026</span></div>
        <div style="display:grid; grid-template-columns:1fr 1fr 1fr; gap:12px; flex:1;">
          ${Array(3).fill(`
            <div style="display:flex; flex-direction:column; gap:5px; border-right:1px solid var(--ink-sepia); padding-right:8px;">
              <div style="height:9px; background:var(--ink-black); opacity:.8; width:80%;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22; width:70%;"></div>
              <div style="height:1px; background:var(--ink-sepia); margin:3px 0;"></div>
              <div style="height:6px; background:var(--ink-black); opacity:.7; width:60%;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
              <div style="height:3px; background:var(--ink-black); opacity:.22;"></div>
            </div>
          `).join('')}
        </div>
      `
    },
    almanac: { name: 'The <em>Almanac</em>', family: 'Editorial', use: 'Archive-driven sites', pair: 'Manuscript · Linen · Folio',
      blurb: 'Chronological index. Archival, scholarly, ledger-like — for blogs that want to look like records.',
      canvas: `
        <div style="font-family:var(--serif); font-style:italic; font-size:16px; color:var(--ink-black); padding-bottom:8px;">An Almanac, MMXXVI</div>
        <div style="height:1px; background:var(--ink-black); margin-bottom:10px;"></div>
        <div style="display:flex; flex-direction:column; gap:10px;">
          ${[ '12', '08', '05', '03', '01' ].map(d => `
            <div style="display:flex; gap:14px; align-items:baseline; border-bottom:1px dotted var(--ink-sepia); padding-bottom:8px;">
              <span style="font-family:var(--serif); font-style:italic; font-size:18px; color:var(--ink-sepia); min-width:32px;">${d}</span>
              <div style="flex:1; display:flex; flex-direction:column; gap:4px;">
                <div style="height:8px; background:var(--ink-black); opacity:.7; width:50%;"></div>
                <div style="height:3px; background:var(--ink-black); opacity:.22; width:80%;"></div>
              </div>
              <span style="font-family:var(--sans); font-size:10px; color:var(--ink-sepia); letter-spacing:.14em;">MAR</span>
            </div>
          `).join('')}
        </div>
      `
    },
    marquee: { name: 'The <em>Marquee</em>', family: 'Modern · New', use: 'Bold single-feature blogs', pair: 'Mono · Slate · Onyx',
      blurb: 'Poster-scale type with a single feature essay. The headline is the layout — built for writers who want to arrive, not introduce.',
      canvas: `
        <div style="display:flex; justify-content:space-between; align-items:baseline; font-family:var(--sans); font-size:9px; letter-spacing:.18em; color:var(--ink-sepia); padding-bottom:6px; border-bottom:1px solid var(--ink-black);">
          <span>THE MARQUEE</span><span>FEATURED</span><span>2026</span>
        </div>
        <div style="flex:1; display:flex; flex-direction:column; justify-content:center; padding:12px 4px;">
          <div style="font-family:var(--serif); font-weight:500; font-size:54px; line-height:0.94; letter-spacing:-0.035em; color:var(--ink-black);">A quiet</div>
          <div style="font-family:var(--serif); font-style:italic; font-weight:500; font-size:54px; line-height:0.94; letter-spacing:-0.035em; color:var(--ink-black); margin-left:46px;">revolt,</div>
          <div style="font-family:var(--serif); font-weight:500; font-size:54px; line-height:0.94; letter-spacing:-0.035em; color:var(--ink-accent);">in long form.</div>
        </div>
        <div style="display:flex; align-items:center; gap:14px; border-top:1px solid var(--ink-black); padding-top:10px;">
          <div style="width:54px; height:54px; background:var(--ink-sepia); opacity:.75;"></div>
          <div style="flex:1; display:flex; flex-direction:column; gap:5px;">
            <div style="height:7px; background:var(--ink-black); opacity:.75; width:65%;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:85%;"></div>
            <div style="height:3px; background:var(--ink-black); opacity:.22; width:55%;"></div>
          </div>
          <span style="font-family:var(--sans); font-size:9px; letter-spacing:.16em; color:var(--ink-sepia);">12·MAR · 8 MIN</span>
        </div>
      `
    },
    atlas: { name: 'The <em>Atlas</em>', family: 'Modern · New', use: 'Topic-led archives', pair: 'Slate · Forest · Press',
      blurb: 'Coordinate-gridded topic map. Every essay sits in a labelled cell — readers navigate by subject, not chronology.',
      canvas: `
        <div style="display:flex; justify-content:space-between; align-items:baseline; padding-bottom:8px;">
          <span style="font-family:var(--serif); font-style:italic; font-size:14px; color:var(--ink-black);">An Atlas, by subject</span>
          <span style="font-family:var(--mono); font-size:10px; letter-spacing:.06em; color:var(--ink-sepia);">A1 — D4 · 47 essays</span>
        </div>
        <div style="display:grid; grid-template-columns:18px repeat(4, 1fr); grid-template-rows:14px repeat(3, 1fr); gap:5px; flex:1;">
          <div></div>
          ${['A','B','C','D'].map(l => `<div style="font-family:var(--mono); font-size:10px; color:var(--ink-sepia); text-align:center;">${l}</div>`).join('')}
          ${[1,2,3].map(r => `
            <div style="font-family:var(--mono); font-size:10px; color:var(--ink-sepia); align-self:center;">${r}</div>
            ${[0,1,2,3].map(c => {
              const filled = (r+c) % 2 === 0;
              const accent = (r===2 && c===1);
              return '<div style="border:1px solid var(--ink-black); ' + (accent ? 'background:var(--ink-black);' : (filled ? 'background:var(--ink-sepia); opacity:.55;' : '')) + ' display:flex; flex-direction:column; justify-content:space-between; padding:5px 6px;"><span style="font-family:var(--mono); font-size:8px; color:' + (accent ? 'var(--ink-cream)' : (filled ? 'var(--ink-cream)' : 'var(--ink-sepia)')) + '; letter-spacing:.08em;">' + String.fromCharCode(65+c) + r + '</span></div>';
            }).join('')}
          `).join('')}
        </div>
        <div style="font-family:var(--sans); font-size:10px; letter-spacing:.14em; color:var(--ink-sepia); text-transform:uppercase; border-top:1px solid var(--ink-black); padding-top:8px;">C2 · On the second draft — Anjali Rao</div>
      `
    },
    constellation: { name: 'The <em>Constellation</em>', family: 'Modern · New', use: 'Cross-linked essay maps', pair: 'Plum · Cobalt · Onyx',
      blurb: 'Node-and-connector map of essays — readers follow the threads between ideas rather than dates.',
      canvas: `
        <div style="display:flex; justify-content:space-between; align-items:baseline; padding-bottom:8px;">
          <span style="font-family:var(--serif); font-style:italic; font-size:14px; color:var(--ink-black);">A constellation of essays</span>
          <span style="font-family:var(--mono); font-size:10px; color:var(--ink-sepia);">·  ·  ·</span>
        </div>
        <div style="flex:1; position:relative;">
          <svg viewBox="0 0 460 290" style="position:absolute; inset:0; width:100%; height:100%;" preserveAspectRatio="none">
            <g stroke="var(--ink-sepia)" stroke-width="1" fill="none" opacity="0.55">
              <line x1="90" y1="60" x2="220" y2="110"/>
              <line x1="220" y1="110" x2="370" y2="60"/>
              <line x1="220" y1="110" x2="120" y2="200"/>
              <line x1="220" y1="110" x2="340" y2="210"/>
              <line x1="120" y1="200" x2="340" y2="210"/>
              <line x1="370" y1="60" x2="430" y2="170"/>
              <line x1="430" y1="170" x2="340" y2="210"/>
            </g>
            <g stroke="var(--ink-accent)" stroke-width="2" fill="none">
              <line x1="220" y1="110" x2="370" y2="60"/>
              <line x1="220" y1="110" x2="120" y2="200"/>
            </g>
            <g fill="var(--ink-black)">
              <circle cx="90" cy="60" r="5"/><circle cx="370" cy="60" r="6"/>
              <circle cx="430" cy="170" r="4"/><circle cx="340" cy="210" r="5"/><circle cx="120" cy="200" r="5"/>
            </g>
            <circle cx="220" cy="110" r="9" fill="var(--ink-accent)"/>
            <g font-family="var(--sans)" font-size="9" fill="var(--ink-black)" letter-spacing="0.5">
              <text x="240" y="108">On the second draft</text>
              <text x="100" y="50">On craft</text>
              <text x="305" y="50">On editing</text>
              <text x="385" y="170">On the reader</text>
              <text x="270" y="225">On the field</text>
              <text x="55" y="218">Marginalia</text>
            </g>
          </svg>
        </div>
        <div style="display:flex; justify-content:space-between; font-family:var(--sans); font-size:10px; letter-spacing:.14em; color:var(--ink-sepia); text-transform:uppercase; border-top:1px solid var(--ink-black); padding-top:8px;"><span>47 essays</span><span>132 threads</span><span>9 clusters</span></div>
      `
    },
    spread: { name: 'The <em>Spread</em>', family: 'Editorial · New', use: 'Print-style features', pair: 'Manuscript · Folio · Linen',
      blurb: 'Two-page magazine spread, fold and all. A confident editorial Layout that treats the homepage like the centre pages of a print issue.',
      canvas: `
        <div style="display:flex; justify-content:space-between; font-family:var(--sans); font-size:9px; letter-spacing:.16em; color:var(--ink-sepia); padding-bottom:6px;"><span>PP. 24—25</span><span>THE SPREAD</span><span>VOL. III</span></div>
        <div style="height:1px; background:var(--ink-black);"></div>
        <div style="display:grid; grid-template-columns:1fr 1fr; flex:1; position:relative;">
          <div style="position:absolute; top:0; bottom:0; left:50%; width:1px; background:var(--ink-sepia); opacity:.55;"></div>
          <div style="padding:14px 16px 14px 0; display:flex; flex-direction:column; gap:8px;">
            <div style="font-family:var(--serif); font-style:italic; font-size:11px; color:var(--ink-sepia);">A cover essay, set in two parts</div>
            <div style="font-family:var(--serif); font-weight:500; font-size:26px; line-height:0.98; letter-spacing:-0.025em; color:var(--ink-black);">On the second draft.</div>
            <div style="aspect-ratio:5/3; background:var(--ink-sepia); opacity:.75; margin-top:2px;"></div>
          </div>
          <div style="padding:14px 0 14px 16px; display:flex; flex-direction:column; gap:5px;">
            <div style="height:1px; background:var(--ink-black); opacity:.4; margin-bottom:4px;"></div>
            ${Array(8).fill('<div style="height:3px; background:var(--ink-black); opacity:.22;"></div>').join('')}
            <div style="border-left:2px solid var(--ink-accent); padding:4px 0 4px 8px; margin-top:4px; font-family:var(--serif); font-style:italic; font-size:11px; color:var(--ink-black);">"A pull-quote."</div>
          </div>
        </div>
        <div style="display:flex; justify-content:space-between; font-family:var(--mono); font-size:9px; color:var(--ink-sepia); border-top:1px solid var(--ink-black); padding-top:6px;"><span>24</span><span>useinkwell.app · The Spread</span><span>25</span></div>
      `
    },
    manifesto: { name: 'The <em>Manifesto</em>', family: 'Editorial · New', use: 'Statement homepages', pair: 'Mono · Letterpress · Onyx',
      blurb: 'Billboard typography — one statement, one image, one line. For writers whose homepage should be the argument.',
      canvas: `
        <div style="display:flex; justify-content:space-between; font-family:var(--sans); font-size:9px; letter-spacing:.18em; color:var(--ink-sepia); text-transform:uppercase; padding-bottom:8px; border-bottom:1px solid var(--ink-black);">
          <span>§ I · A statement</span><span>2026 —</span>
        </div>
        <div style="flex:1; display:flex; flex-direction:column; justify-content:center; padding:6px 0;">
          <div style="font-family:var(--serif); font-weight:500; font-size:48px; line-height:0.92; letter-spacing:-0.035em; color:var(--ink-black);">The internet</div>
          <div style="font-family:var(--serif); font-weight:500; font-size:48px; line-height:0.92; letter-spacing:-0.035em; color:var(--ink-black);">should reward</div>
          <div style="font-family:var(--serif); font-style:italic; font-weight:500; font-size:48px; line-height:0.92; letter-spacing:-0.035em; color:var(--ink-accent); margin-top:2px;">writing</div>
          <div style="font-family:var(--serif); font-weight:500; font-size:48px; line-height:0.92; letter-spacing:-0.035em; color:var(--ink-black);">that takes its time.</div>
        </div>
        <div style="border-top:1px solid var(--ink-black); padding-top:10px; display:flex; justify-content:space-between; align-items:center;">
          <span style="font-family:var(--serif); font-style:italic; font-size:13px; color:var(--ink-black);">— Read the manifesto</span>
          <span style="font-family:var(--sans); font-size:10px; letter-spacing:.14em; color:var(--ink-sepia); text-transform:uppercase;">Signed by 47</span>
        </div>
      `
    },
    compendium: { name: 'The <em>Compendium</em>', family: 'Editorial · New', use: 'Reference archives, A—Z', pair: 'Manuscript · Press · Cobalt',
      blurb: 'Dictionary-style A–Z index — every essay catalogued under its first letter. The most navigable editorial Layout for deep archives.',
      canvas: `
        <div style="display:flex; justify-content:space-between; align-items:baseline; padding-bottom:6px;">
          <span style="font-family:var(--serif); font-style:italic; font-size:16px; color:var(--ink-black);">A Compendium</span>
          <span style="font-family:var(--mono); font-size:10px; color:var(--ink-sepia);">A — Z · 184 entries</span>
        </div>
        <div style="display:flex; gap:4px; padding:6px 0; border-top:1px solid var(--ink-black); border-bottom:1px solid var(--ink-black); margin-bottom:10px;">
          ${'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('').map((l, i) => '<span style="flex:1; font-family:var(--mono); font-size:10px; text-align:center; color:' + (i===2 ? 'var(--ink-accent)' : (i % 3 === 0 ? 'var(--ink-black)' : 'var(--ink-sepia)')) + ';">' + l + '</span>').join('')}
        </div>
        <div style="display:grid; grid-template-columns:32px 1fr; gap:10px 14px; flex:1; align-content:start;">
          ${[['C','Craft, on','12 essays'],['','Critic, the role of','4 essays'],['D','Draft, the second','9 essays'],['','Desk, the editorial','15 essays'],['E','Editing, against','6 essays']].map(([letter, title, count]) =>
            '<span style="font-family:var(--serif); font-style:italic; font-weight:500; font-size:22px; color:' + (letter === 'C' ? 'var(--ink-accent)' : 'var(--ink-black)') + '; line-height:1;">' + letter + '</span><div style="display:flex; justify-content:space-between; align-items:baseline; gap:8px; border-bottom:1px dotted var(--ink-sepia); padding-bottom:5px;"><span style="font-family:var(--serif); font-size:13px; color:var(--ink-black);">' + title + '</span><span style="font-family:var(--sans); font-size:9px; letter-spacing:.12em; color:var(--ink-sepia); text-transform:uppercase;">' + count + '</span></div>'
          ).join('')}
        </div>
      `
    }
  };

  const theatreList = document.getElementById('theatreList');
  const canvasEl = document.getElementById('layoutCanvas');
  const titleEl = document.getElementById('theatreTitle');
  const blurbEl = document.getElementById('theatreBlurb');
  const familyEl = document.getElementById('theatreFamily');
  const useEl = document.getElementById('theatreUse');
  const pairEl = document.getElementById('theatrePair');
  const liveLabel = document.getElementById('liveLayoutLabel');

  function setLayout(key) {
    const l = LAYOUTS[key];
    if (!l) return;
    canvasEl.classList.add('out');
    setTimeout(() => {
      canvasEl.innerHTML = l.canvas;
      titleEl.innerHTML = l.name;
      blurbEl.textContent = l.blurb;
      familyEl.textContent = l.family;
      useEl.textContent = l.use;
      pairEl.textContent = l.pair;
      canvasEl.classList.remove('out');
      // update hero issue mockup footnote subtly
      if (liveLabel) {
        const plain = titleEl.textContent.trim();
        liveLabel.textContent = plain + ' · ' + (root.getAttribute('data-preset') ? capitalize(root.getAttribute('data-preset')) : 'Cream');
      }
    }, 180);
    if (theatreList) {
      theatreList.querySelectorAll('button').forEach(b =>
        b.classList.toggle('active', b.getAttribute('data-layout') === key)
      );
    }
  }
  if (theatreList) {
    theatreList.querySelectorAll('button').forEach(b => {
      b.addEventListener('click', () => setLayout(b.getAttribute('data-layout')));
    });
  }
  // initial render
  setLayout('feed');

  // ──────────────────────────────────────────────────────────
  // 8) Preset switcher + interactive showcase
  // ──────────────────────────────────────────────────────────
  const presetCur = document.getElementById('presetCur');
  const swatches = document.querySelectorAll('.swatch');
  const chips = document.querySelectorAll('#presetChips .chip');
  const live = document.getElementById('presetLive');
  const liveCredit = document.getElementById('presetLiveCredit');

  function applyPreset(name, displayLabel) {
    if (name) root.setAttribute('data-preset', name);
    else root.removeAttribute('data-preset');
    chips.forEach(c => c.classList.toggle('active', (c.getAttribute('data-preset') || '') === name));
    swatches.forEach(s => s.classList.toggle('active', (s.getAttribute('data-preset') || '') === name));
    if (presetCur && displayLabel) presetCur.textContent = displayLabel;
    // update the hero footnote to reflect choice
    if (liveLabel && titleEl) {
      liveLabel.textContent = (titleEl.textContent.trim() || 'The Magazine') + ' · ' + (displayLabel || 'Cream');
    }
  }

  chips.forEach(c => c.addEventListener('click', () => {
    const name = c.getAttribute('data-preset') || '';
    applyPreset(name, c.getAttribute('title').replace(' (dark)', ''));
  }));

  function setLivePreset(bg, fg, accent, label) {
    if (!live) return;
    const isDark = label && (label.toLowerCase().includes('dark') || ['#16140F','#0A0A0C'].includes(bg.toUpperCase()));
    live.style.background = bg;
    live.style.color = fg;
    live.querySelector('.eb').style.color = accent;
    live.querySelector('.ttl').style.color = fg;
    const ld = live.querySelector('.ld'); ld.style.color = fg; ld.style.opacity = isDark ? .82 : .75;
    live.querySelector('.rule').style.background = accent;
    live.querySelector('.meta').style.color = accent;
    const tag = live.querySelector('.accent-tag');
    tag.style.background = accent; tag.style.color = bg;
    if (liveCredit) { liveCredit.textContent = label; liveCredit.style.color = accent; }
  }

  swatches.forEach(sw => {
    const enter = () => {
      setLivePreset(
        sw.getAttribute('data-bg'),
        sw.getAttribute('data-fg'),
        sw.getAttribute('data-accent'),
        sw.getAttribute('data-meta')
      );
    };
    sw.addEventListener('mouseenter', enter);
    sw.addEventListener('click', () => {
      const name = sw.getAttribute('data-preset') || '';
      const label = sw.querySelector('.label span:first-child').textContent;
      applyPreset(name, label);
      enter();
    });
  });
  // Initial live preview = Cream
  setLivePreset('#FAF7F2', '#1A1A1A', '#8B7355', 'Cream · Editorial');

  function capitalize(s) { return s ? s[0].toUpperCase() + s.slice(1) : ''; }

  // ──────────────────────────────────────────────────────────
  // 9) Marquee · populate quote cards (so they loop seamlessly)
  // ──────────────────────────────────────────────────────────
  const QUOTES = [
    { q: 'I moved my newsletter off Substack in an afternoon. It already looks better than I had any right to expect.',                    who: 'Letter №12', name: 'Anjali Rao, essayist' },
    { q: 'Inkwell is the first piece of software in years that <em>respects</em> a sentence.',                                              who: 'Letter №08', name: 'Sam Greaves, novelist' },
    { q: 'We host fourteen client blogs on a single deploy. Multi-tenant means I sleep at night.',                                          who: 'Letter №21', name: 'Mira Aldridge, studio lead' },
    { q: 'The Editorial Desk is the first CMS admin I have not wanted to redesign within ten minutes.',                                     who: 'Letter №05', name: 'Theo Vaughan, designer' },
    { q: 'It feels like a print magazine — but it is just a Razor view and a CSS preset.',                                                  who: 'Letter №17', name: 'Lin Park, illustrator' },
    { q: 'Self-hosting was always the dream. Inkwell is the first time it has not also been a chore.',                                      who: 'Letter №03', name: 'Hawthorn & Co.' },
    { q: 'Switching preset is a single click and my entire site changes character. I changed it four times this week.',                     who: 'Letter №19', name: 'Studio Foglio' }
  ];
  const track = document.getElementById('marqueeTrack');
  if (track) {
    const html = QUOTES.map(c => `
      <article class="quote-card">
        <p class="q">${c.q}</p>
        <p class="who">${c.who} · <em>${c.name}</em></p>
      </article>
    `).join('');
    track.innerHTML = html + html;  // duplicated for seamless loop
  }

  // ──────────────────────────────────────────────────────────
  // 10) Animated terminal (typewriter)
  // ──────────────────────────────────────────────────────────
  const termBody = document.getElementById('terminalBody');
  if (termBody) {
    const SCRIPT = [
      { t: 'c',  s: '# Clone, configure, run — three lines.' },
      { t: 'k',  s: '$ ', plain: 'git clone https://github.com/marutisoftwaresolutions/inkwell.git inkwell' },
      { t: 'ok', s: '  ✓ Cloned in 1.2s · 1,847 objects', delay: 320 },
      { t: 'k',  s: '$ ', plain: 'cd inkwell/Blog.Web && dotnet restore' },
      { t: 'ok', s: '  ✓ Restored 41 packages · .NET 8.0', delay: 280 },
      { t: 'k',  s: '$ ', plain: 'dotnet ef database update' },
      { t: 'ok', s: '  ✓ Applied 12 migrations · seeded admin', delay: 280 },
      { t: 'k',  s: '$ ', plain: 'dotnet watch run' },
      { t: 'ok', s: '  ✓ Now listening on http://localhost:5000', delay: 320 },
      { t: 'c',  s: '# The first registered user becomes the admin.' }
    ];

    const fragments = [];
    let cursor;

    function ensureCursor() {
      if (!cursor) { cursor = document.createElement('span'); cursor.className = 'blink'; termBody.appendChild(cursor); }
      else termBody.appendChild(cursor);
    }
    function appendSpan(cls, text) {
      const sp = document.createElement('span');
      sp.className = cls || '';
      sp.textContent = text;
      termBody.insertBefore(sp, cursor);
      return sp;
    }
    function newline() {
      const br = document.createElement('br');
      termBody.insertBefore(br, cursor);
    }

    let canceled = false;
    async function runTerminal() {
      termBody.innerHTML = '';
      ensureCursor();
      if (prefersReduced) {
        // dump all at once
        SCRIPT.forEach(step => {
          appendSpan(step.t, step.s + (step.plain || ''));
          newline();
        });
        return;
      }
      for (const step of SCRIPT) {
        if (canceled) return;
        // prefix (instant), e.g. "$ " or "# comment"
        appendSpan(step.t, step.s);
        if (step.plain) {
          // type the command character by character
          const target = appendSpan('', '');
          for (const ch of step.plain) {
            if (canceled) return;
            target.textContent += ch;
            await wait(22 + Math.random() * 30);
          }
        }
        newline();
        await wait(step.delay || 220);
      }
    }
    function wait(ms) { return new Promise(r => setTimeout(r, ms)); }
    // Only start when terminal scrolls into view
    if ('IntersectionObserver' in window) {
      const tio = new IntersectionObserver((entries) => {
        entries.forEach(e => {
          if (e.isIntersecting) {
            runTerminal();
            tio.disconnect();
          }
        });
      }, { threshold: 0.3 });
      tio.observe(termBody);
    } else {
      runTerminal();
    }
  }

  // ──────────────────────────────────────────────────────────
  // 11) Magnetic buttons (CTA only — subtle)
  // ──────────────────────────────────────────────────────────
  if (!prefersReduced) {
    document.querySelectorAll('.magnetic').forEach(btn => {
      btn.addEventListener('mousemove', (e) => {
        const r = btn.getBoundingClientRect();
        const x = e.clientX - (r.left + r.width / 2);
        const y = e.clientY - (r.top + r.height / 2);
        btn.style.transform = `translate(${x * 0.18}px, ${y * 0.18}px)`;
      });
      btn.addEventListener('mouseleave', () => { btn.style.transform = ''; });
    });
  }

  // ──────────────────────────────────────────────────────────
  // 12) Smooth-scroll header anchors
  // ──────────────────────────────────────────────────────────
  document.querySelectorAll('a[href^="#"]').forEach(a => {
    a.addEventListener('click', (e) => {
      const id = a.getAttribute('href');
      if (id.length < 2) return;
      const target = document.querySelector(id);
      if (!target) return;
      e.preventDefault();
      const top = target.getBoundingClientRect().top + window.scrollY - 70;
      window.scrollTo({ top, behavior: prefersReduced ? 'auto' : 'smooth' });
    });
  });
})();
