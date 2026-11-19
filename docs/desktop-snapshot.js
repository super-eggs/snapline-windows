'use strict';

// Paint the demo's own surfaces into a bitmap using their current positions.
// The same bitmap is displayed during selection and used for all pixel actions.
function paintDesktopSnapshot(desktop, canvas, { wallpaper, clothespin, icons }) {
  canvas.width = desktop.clientWidth;
  canvas.height = desktop.clientHeight;
  const context = canvas.getContext('2d');
  const frame = desktop.getBoundingClientRect();
  const left = frame.left + desktop.clientLeft;
  const top = frame.top + desktop.clientTop;
  const find = selector => desktop.querySelector(selector);
  const full = { x: 0, y: 0, w: canvas.width, h: canvas.height };
  const glyph = document.createElement('canvas');
  glyph.width = glyph.height = 40;
  const glyphContext = glyph.getContext('2d');

  function box(element) {
    const rect = element.getBoundingClientRect();
    return { x: rect.left - left, y: rect.top - top, w: rect.width, h: rect.height };
  }

  function path(rect, radius = 0) {
    context.beginPath();
    context.roundRect(rect.x, rect.y, rect.w, rect.h, radius);
  }

  function clipped(rect, radius, draw) {
    context.save();
    path(rect, radius);
    context.clip();
    draw();
    context.restore();
  }

  function fill(rect, color, radius = 0) {
    context.fillStyle = color;
    path(rect, radius);
    context.fill();
  }

  function surface(element, background, draw, shadow = false) {
    const rect = box(element);
    if (!rect.w || !rect.h) return;
    const style = getComputedStyle(element);
    const radius = Number.parseFloat(style.borderTopLeftRadius) || 0;
    if (shadow) {
      context.save();
      context.shadowColor = '#05162874';
      context.shadowBlur = 18;
      context.shadowOffsetY = 8;
    }
    fill(rect, background || style.backgroundColor, radius);
    if (shadow) context.restore();
    clipped(rect, radius, draw);
    const border = Number.parseFloat(style.borderTopWidth);
    if (border) {
      context.lineWidth = border;
      context.strokeStyle = style.borderTopColor;
      path({ x: rect.x + border / 2, y: rect.y + border / 2, w: rect.w - border, h: rect.h - border }, radius);
      context.stroke();
    }
  }

  function image(media, rect, fit = 'fill') {
    const width = media.naturalWidth ?? media.width;
    const height = media.naturalHeight ?? media.height;
    if (!width || !height || !rect.w || !rect.h) return;
    if (fit === 'fill') {
      context.drawImage(media, rect.x, rect.y, rect.w, rect.h);
      return;
    }
    const scale = (fit === 'cover' ? Math.max : Math.min)(rect.w / width, rect.h / height);
    const w = width * scale;
    const h = height * scale;
    context.drawImage(media, rect.x + (rect.w - w) / 2, rect.y + (rect.h - h) / 2, w, h);
  }

  function text(element, value = element.textContent.trim(), align = 'left', offset = 0) {
    const rect = box(element);
    if (!rect.w || !rect.h || rect.w <= offset) return;
    const style = getComputedStyle(element);
    context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
    context.textBaseline = 'middle';
    context.textAlign = align;
    context.fillStyle = style.color;
    context.fillText(value, rect.x + (align === 'center' ? rect.w / 2 : offset), rect.y + rect.h / 2, rect.w - offset);
  }

  function fileIcon(element) {
    const rect = box(element);
    const style = getComputedStyle(element);
    const background = context.createLinearGradient(rect.x, rect.y, rect.x + rect.w, rect.y + rect.h);
    background.addColorStop(0, style.getPropertyValue('--file-light'));
    background.addColorStop(1, style.getPropertyValue('--file-dark'));
    fill(rect, background, 3);
    context.fillStyle = style.getPropertyValue('--file-mark');
    context.beginPath();
    context.moveTo(rect.x + 3, rect.y + rect.h - 4);
    context.lineTo(rect.x + 7, rect.y + 8);
    context.lineTo(rect.x + 10, rect.y + 11);
    context.lineTo(rect.x + 12, rect.y + 7);
    context.lineTo(rect.x + rect.w - 3, rect.y + rect.h - 4);
    context.closePath();
    context.fill();
  }

  function paintControls(container) {
    for (const button of container.querySelectorAll('button')) {
      surface(button, null, () => {
        const icon = button.querySelector('.demo-icon');
        const picture = button.querySelector('img');
        const file = button.querySelector('.demo-file-icon');
        if (icon) {
          const style = getComputedStyle(icon);
          const x = -Number.parseFloat(style.getPropertyValue('--icon')) * 2;
          glyphContext.clearRect(0, 0, 40, 40);
          glyphContext.drawImage(icons, x, 0, 40, 40, 0, 0, 40, 40);
          glyphContext.globalCompositeOperation = 'source-in';
          glyphContext.fillStyle = style.color;
          glyphContext.fillRect(0, 0, 40, 40);
          glyphContext.globalCompositeOperation = 'source-over';
          context.globalAlpha = Number(getComputedStyle(button).opacity);
          image(glyph, box(icon));
          context.globalAlpha = 1;
        } else if (picture) image(picture, box(picture));
        else if (file) fileIcon(file);
        else if (button.classList.contains('demo-ink')) {
          const rect = box(button);
          fill({ x: rect.x + rect.w / 2 - 6.5, y: rect.y + rect.h / 2 - 6.5, w: 13, h: 13 }, button.dataset.ink, 7);
        }
        if (button.matches('.demo-taskbar [aria-pressed="true"]')) {
          const rect = box(button);
          fill({ x: rect.x + rect.w / 2 - 5, y: rect.y + rect.h - 3, w: 10, h: 2 }, '#9ed0ff', 1);
        }
      });
    }
  }

  function paintTitle(window) {
    const title = window.querySelector('.demo-window-title');
    surface(title, null, () => {
      const file = title.querySelector('.demo-file-icon');
      const icon = title.querySelector('img');
      if (file) fileIcon(file);
      if (icon) image(icon, box(icon));
      text(title, title.textContent.trim(), 'left', 32);
      paintControls(title);
    });
  }

  function paintPaper(window) {
    surface(window, null, () => {
      paintTitle(window);
      const media = window.querySelector('img, canvas');
      image(media, box(media));
    }, true);
  }

  function slider(input) {
    const rect = box(input);
    if (!rect.w || !rect.h) return;
    const color = getComputedStyle(input).accentColor;
    const fraction = (input.value - input.min) / (input.max - input.min);
    const x = rect.x + 5 + (rect.w - 10) * fraction;
    const y = rect.y + rect.h / 2;
    fill({ x: rect.x, y: y - 3, w: rect.w, h: 6 }, '#696969', 3);
    fill({ x: rect.x, y: y - 3, w: x - rect.x, h: 6 }, color, 3);
    fill({ x: x - 5, y: y - 5, w: 10, h: 10 }, color, 5);
  }

  image(wallpaper, full, 'cover');
  const papers = desktop.querySelectorAll('.demo-paper');
  const frontPaper = find('.demo-paper[data-front]');
  for (const window of papers) {
    if (window !== frontPaper) paintPaper(window);
  }

  const shelf = find('.demo-shelf');
  if (!shelf.hidden) {
    const rope = find('.demo-rope');
    image(rope, box(rope));
    const shots = find('.demo-shots');
    clipped(box(shots), 0, () => {
      for (const item of shots.children) {
        surface(item, null, () => {
          const thumb = item.querySelector('canvas');
          image(thumb, box(thumb));
        }, true);
        const rect = box(item);
        image(clothespin, { x: rect.x + rect.w / 2 - 9, y: rect.y - 24, w: 18, h: 32 });
      }
    });
    const tools = find('.demo-shelf-tools');
    surface(tools, '#f8f6ef', () => paintControls(tools));
  }

  const editor = find('.demo-editor');
  surface(editor, null, () => {
    paintTitle(editor);
    const viewport = find('.demo-editor-image');
    const pixels = viewport.querySelector('canvas');
    const rect = box(viewport);
    const radius = Math.max(rect.w, rect.h) / 2;
    const gradient = context.createRadialGradient(rect.x + rect.w / 2, rect.y + rect.h / 2, 0, rect.x + rect.w / 2, rect.y + rect.h / 2, radius);
    gradient.addColorStop(0, '#313233');
    gradient.addColorStop(1, '#252628');
    fill(rect, gradient);
    clipped(rect, 0, () => image(pixels, box(pixels)));
    const tools = find('.demo-editor-tools');
    surface(tools, null, () => {});
    paintControls(find('.demo-editor-command'));
    paintControls(tools);
    paintControls(find('.demo-editor-footer'));
    const zoom = find('.demo-zoom-level');
    surface(zoom, null, () => text(zoom, zoom.textContent, 'center'));
    text(find('.demo-editor-name'), undefined, 'center');
    text(find('.demo-editor-info'));
    slider(find('.demo-zoom'));
  }, true);

  if (frontPaper) paintPaper(frontPaper);

  const pin = find('.demo-pin');
  if (!pin.hidden) {
    const pixels = pin.querySelector('canvas');
    context.save();
    context.globalAlpha = Number(getComputedStyle(pixels).opacity);
    image(pixels, box(pixels), 'contain');
    context.restore();
    const tools = find('.demo-pin-tools');
    if (Number(getComputedStyle(tools).opacity)) {
      surface(tools, null, () => {
        for (const label of tools.querySelectorAll('label')) {
          text(label, label.textContent.trim());
          slider(label.querySelector('input'));
        }
        paintControls(tools);
      });
    }
  }

  const taskbar = find('.demo-taskbar');
  surface(taskbar, '#132a3d', () => {
    const tile = box(find('.demo-launcher'));
    for (const x of [0, 9]) {
      for (const y of [0, 9]) fill({ x: tile.x + x, y: tile.y + y, w: 7, h: 7 }, '#9acbff', 1);
    }
    paintControls(taskbar);
    const network = box(find('.demo-network'));
    context.beginPath();
    context.ellipse(network.x + network.w / 2, network.y + network.h, network.w / 2, network.h - 2, 0, Math.PI * 1.15, Math.PI * 1.85);
    context.lineWidth = 1.4;
    context.strokeStyle = '#f2f7ff';
    context.stroke();
    const battery = box(find('.demo-battery'));
    path(battery, 2);
    context.lineWidth = 1;
    context.stroke();
    text(find('.demo-tray > span:last-child'));
  });
}
