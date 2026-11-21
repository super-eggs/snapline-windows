'use strict';

function createCaptureTools(overlay, toolbar, source, say, english) {
  const copy = english ? {
    tools: ['Move or resize selection', 'Pen', 'Rectangle', 'Ellipse', 'Line', 'Arrow', 'Text', 'Mosaic', 'Eraser'],
    colors: ['Orange red', 'Gold', 'Green', 'Blue', 'White', 'Black'],
    undo: 'Undo (Ctrl+Z)', redo: 'Redo (Ctrl+Y)', color: 'Annotation color', width: 'Stroke width',
    picker: 'Color picker', text: 'Annotation text', apply: 'Apply', textAdded: 'Drag the text to adjust its position.',
    findColor: 'Point at the image to sample its original color. Click or press C to copy. Shift switches HEX / RGB.',
    copied: value => `Copied ${value}.`, copyFailed: value => `Picked ${value}. Clipboard access is unavailable.`,
  } : {
    tools: ['移动或调整选区', '画笔', '矩形', '椭圆', '直线', '箭头', '文字', '马赛克', '橡皮擦'],
    colors: ['橙红色', '金黄色', '绿色', '蓝色', '白色', '黑色'],
    undo: '撤销（Ctrl+Z）', redo: '重做（Ctrl+Y）', color: '标注颜色', width: '标注线宽',
    picker: '取色器', text: '标注文字', apply: '确定', textAdded: '可以直接拖动文字调整位置。',
    findColor: '移动到选区内取原图颜色，点击或按 C 复制，Shift 切换 HEX / RGB。',
    copied: value => `已复制 ${value}。`, copyFailed: value => `已取色 ${value}。当前浏览器无法写入剪贴板。`,
  };
  const selection = overlay.querySelector('.demo-selection');
  const canvas = selection.querySelector('canvas');
  const context = canvas.getContext('2d');
  const original = document.createElement('canvas');
  const originalContext = original.getContext('2d', { willReadFrequently: true });
  const mosaic = document.createElement('canvas');
  const options = document.createElement('div');
  options.className = 'demo-capture-options';
  options.hidden = true;
  toolbar.prepend(options);
  const kinds = ['hand', 'pen', 'rectangle', 'ellipse', 'line', 'arrow', 'text', 'mosaic', 'eraser'];
  const colors = ['#ff4500', '#ffd700', '#32cd32', '#1e90ff', '#ffffff', '#000000'];
  let region, picked, textStart;
  let mode = 'hand';
  let ink = colors[0];
  let width = 3;
  let hex = true;
  let history = [[]];
  let position = 0;

  function makeButton(icon, label, handler) {
    const button = document.createElement('button');
    button.type = 'button';
    button.title = label;
    button.setAttribute('aria-label', label);
    const image = document.createElement('span');
    image.className = icon ? `demo-icon icon-${icon}` : 'demo-ink-dot';
    button.append(image);
    button.addEventListener('click', handler);
    return button;
  }

  function divider() {
    const line = document.createElement('span');
    line.className = 'demo-capture-divider';
    line.setAttribute('aria-hidden', 'true');
    options.append(line);
  }

  const toolButtons = kinds.map((kind, index) => {
    const button = makeButton(kind, copy.tools[index], () => selectTool(kind));
    button.dataset.captureTool = kind;
    options.append(button);
    return button;
  });
  divider();
  const undoButton = makeButton('undo', copy.undo, () => moveHistory(-1));
  const redoButton = makeButton('redo', copy.redo, () => moveHistory(1));
  options.append(undoButton, redoButton);
  divider();

  function choice(icon, label) {
    const control = document.createElement('div');
    control.className = 'demo-capture-choice';
    const panel = document.createElement('div');
    panel.className = 'demo-capture-popover';
    panel.hidden = true;
    const button = makeButton(icon, label, () => {
      commitText();
      const open = panel.hidden;
      closeChoices();
      panel.hidden = !open;
      button.setAttribute('aria-expanded', String(open));
    });
    button.setAttribute('aria-expanded', 'false');
    control.append(button, panel);
    options.append(control);
    return { button, panel };
  }

  const palette = choice('', copy.color);
  palette.button.firstChild.style.background = ink;
  colors.forEach((color, index) => {
    const button = makeButton('', copy.colors[index], () => {
      ink = color;
      palette.button.firstChild.style.background = color;
      closeChoices();
      palette.button.focus({ preventScroll: true });
    });
    button.firstChild.style.background = color;
    palette.panel.append(button);
  });
  const widths = choice('width', copy.width);
  for (const size of [2, 3, 5, 8]) {
    const button = document.createElement('button');
    button.type = 'button';
    button.dataset.width = size;
    button.textContent = `${size} px`;
    button.setAttribute('aria-pressed', String(size === width));
    button.addEventListener('click', () => {
      width = size;
      for (const item of widths.panel.children) item.setAttribute('aria-pressed', String(item === button));
      closeChoices();
      widths.button.focus({ preventScroll: true });
    });
    widths.panel.append(button);
  }

  divider();
  const picker = makeButton('picker', copy.picker, () => selectTool(mode === 'picker' ? 'hand' : 'picker'));
  picker.dataset.captureTool = 'picker';
  options.append(picker);

  const form = document.createElement('form');
  form.className = 'demo-capture-text';
  form.hidden = true;
  const input = document.createElement('textarea');
  input.maxLength = 500;
  input.setAttribute('aria-label', copy.text);
  const apply = document.createElement('button');
  apply.type = 'submit';
  apply.textContent = copy.apply;
  form.append(input, apply);
  selection.append(form);
  form.addEventListener('submit', event => {
    event.preventDefault();
    commitText();
    overlay.focus({ preventScroll: true });
  });

  const reading = document.createElement('div');
  reading.className = 'demo-color-reading';
  reading.hidden = true;
  const lens = document.createElement('span');
  lens.className = 'demo-color-lens';
  lens.setAttribute('aria-hidden', 'true');
  const lensPixels = document.createElement('canvas');
  lensPixels.width = lensPixels.height = 9;
  const lensContext = lensPixels.getContext('2d');
  lens.append(lensPixels);
  const swatch = document.createElement('span');
  swatch.className = 'demo-ink-dot';
  const value = document.createElement('output');
  const format = document.createElement('button');
  format.type = 'button';
  format.addEventListener('click', switchFormat);
  reading.append(lens, swatch, value, format);
  selection.append(reading);

  function closeChoices() {
    for (const { button, panel } of [palette, widths]) {
      panel.hidden = true;
      button.setAttribute('aria-expanded', 'false');
    }
  }

  function refresh() {
    for (const button of [...toolButtons, picker]) {
      button.setAttribute('aria-pressed', String(button.dataset.captureTool === mode));
    }
    undoButton.disabled = position === 0;
    redoButton.disabled = position === history.length - 1;
    overlay.dataset.mode = mode === 'hand' ? 'select' : 'mark';
    overlay.toggleAttribute('data-locked', history[position].length > 0);
  }

  function selectTool(kind) {
    commitText();
    closeChoices();
    reading.hidden = true;
    mode = kind;
    overlay.classList.remove('demo-text-movable');
    refresh();
    if (kind === 'picker') say(copy.findColor);
  }

  function setRegion(rect) {
    if (!rect && !region) return;
    if (rect && region && rect.x === region.x && rect.y === region.y && rect.w === region.w && rect.h === region.h) return;
    options.hidden = !rect;
    region = rect;
    history = [[]];
    position = 0;
    mode = 'hand';
    picked = null;
    reading.hidden = form.hidden = true;
    overlay.classList.remove('demo-text-movable');
    closeChoices();
    refresh();
    if (!rect) return;
    canvas.width = original.width = rect.w;
    canvas.height = original.height = rect.h;
    originalContext.drawImage(source, rect.x, rect.y, rect.w, rect.h, 0, 0, rect.w, rect.h);
    render();
  }

  function draw(mark) {
    const { kind, start, end } = mark;
    const w = Math.abs(end.x - start.x);
    const h = Math.abs(end.y - start.y);
    const x = Math.min(start.x, end.x);
    const y = Math.min(start.y, end.y);
    context.strokeStyle = context.fillStyle = mark.ink;
    context.lineWidth = mark.width;
    context.lineCap = context.lineJoin = 'round';
    if (kind === 'mosaic') {
      if (!w || !h) return;
      const patchWidth = Math.min(w, canvas.width - x);
      const patchHeight = Math.min(h, canvas.height - y);
      mosaic.width = Math.max(1, Math.round(patchWidth / 11));
      mosaic.height = Math.max(1, Math.round(patchHeight / 11));
      mosaic.getContext('2d').drawImage(canvas, x, y, patchWidth, patchHeight, 0, 0, mosaic.width, mosaic.height);
      context.imageSmoothingEnabled = false;
      context.drawImage(mosaic, x, y, patchWidth, patchHeight);
      context.imageSmoothingEnabled = true;
      return;
    }
    if (kind === 'text') {
      const size = 12 + mark.width * 2;
      context.font = `${size}px Segoe UI, Microsoft YaHei, sans-serif`;
      context.textBaseline = 'top';
      mark.text.split('\n').forEach((line, index) => context.fillText(line, start.x, start.y + index * size * 1.3, mark.maxWidth));
      return;
    }
    context.beginPath();
    if (kind === 'rectangle') context.rect(x, y, w, h);
    else if (kind === 'ellipse') context.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
    else if (kind === 'pen') {
      context.moveTo(start.x, start.y);
      if (mark.points.length === 1) context.lineTo(start.x + .1, start.y);
      else for (const point of mark.points) context.lineTo(point.x, point.y);
    } else {
      context.moveTo(start.x, start.y);
      context.lineTo(end.x, end.y);
      if (kind === 'arrow') {
        const angle = Math.atan2(end.y - start.y, end.x - start.x);
        const length = Math.min(mark.width * 5, Math.hypot(w, h) * .45);
        for (const side of [-1, 1]) {
          context.moveTo(end.x, end.y);
          context.lineTo(end.x - Math.cos(angle + side * .52) * length, end.y - Math.sin(angle + side * .52) * length);
        }
      }
    }
    context.stroke();
  }

  function render(marks = history[position], draft = null) {
    context.clearRect(0, 0, canvas.width, canvas.height);
    context.drawImage(original, 0, 0);
    marks.forEach(draw);
    if (draft) draw(draft);
  }

  function saveHistory(marks) {
    history.splice(position + 1, history.length, marks);
    if (history.length > 25) history.shift();
    position = history.length - 1;
    render();
    refresh();
  }

  function moveHistory(step) {
    commitText();
    const next = position + step;
    if (next < 0 || next >= history.length) return;
    position = next;
    render();
    refresh();
  }

  function place(element, point, gap = 7) {
    const frame = overlay.getBoundingClientRect();
    const area = selection.getBoundingClientRect();
    element.hidden = false;
    const left = Math.min(point.x / canvas.width * area.width + gap, frame.right - area.left - element.offsetWidth - 6);
    const top = Math.min(point.y / canvas.height * area.height + gap, frame.bottom - area.top - element.offsetHeight - 6);
    element.style.left = `${Math.max(frame.left - area.left + 6, left)}px`;
    element.style.top = `${Math.max(frame.top - area.top + 6, top)}px`;
  }

  function commitText() {
    if (form.hidden) return;
    form.hidden = true;
    const text = input.value.trim();
    if (!text) return;
    const size = 12 + width * 2;
    const lines = text.split('\n');
    context.font = `${size}px Segoe UI, Microsoft YaHei, sans-serif`;
    const maxWidth = Math.max(1, canvas.width - textStart.x);
    const textWidth = Math.min(maxWidth, Math.ceil(Math.max(...lines.map(line => context.measureText(line).width))));
    const textHeight = Math.ceil(lines.length * size * 1.3);
    const start = { x: textStart.x, y: Math.min(textStart.y, Math.max(0, canvas.height - textHeight)) };
    saveHistory([...history[position], { kind: 'text', start, end: start, text, ink, width, maxWidth, textWidth, textHeight }]);
    say(copy.textAdded);
  }

  function within(point) {
    return region && point.x >= region.x && point.y >= region.y && point.x <= region.x + region.w && point.y <= region.y + region.h;
  }

  function local(point) {
    return {
      x: Math.max(0, Math.min(region.w - 1, point.x - region.x)),
      y: Math.max(0, Math.min(region.h - 1, point.y - region.y)),
    };
  }

  function textIndex(point) {
    return history[position].findLastIndex(mark => mark.kind === 'text'
      && point.x >= mark.start.x && point.x <= mark.start.x + mark.textWidth
      && point.y >= mark.start.y && point.y <= mark.start.y + mark.textHeight);
  }

  function canMoveText(point) {
    return (mode === 'hand' || mode === 'text') && within(point) && textIndex(local(point)) !== -1;
  }

  function updateReading() {
    const rgb = `rgb(${picked.join(', ')})`;
    value.textContent = hex ? '#' + picked.map(part => part.toString(16).padStart(2, '0')).join('').toUpperCase() : rgb;
    swatch.style.background = rgb;
    format.textContent = hex ? 'RGB' : 'HEX';
  }

  function switchFormat() {
    hex = !hex;
    if (picked) updateReading();
  }

  function hover(point) {
    overlay.classList.toggle('demo-text-movable', Boolean(canMoveText(point)));
    if (mode !== 'picker') return;
    if (!within(point)) {
      reading.hidden = true;
      return;
    }
    const sample = local(point);
    picked = Array.from(originalContext.getImageData(sample.x, sample.y, 1, 1).data.subarray(0, 3));
    lensContext.clearRect(0, 0, 9, 9);
    lensContext.drawImage(original, 4 - sample.x, 4 - sample.y);
    updateReading();
    place(reading, sample);
  }

  async function copyColor() {
    if (!picked) return;
    const color = value.textContent;
    const activeRegion = region;
    try {
      await navigator.clipboard.writeText(color);
      if (region === activeRegion) say(copy.copied(color));
    } catch {
      if (region === activeRegion) say(copy.copyFailed(color));
    }
  }

  function begin(point) {
    if (!within(point)) return null;
    closeChoices();
    if (mode === 'picker') {
      hover(point);
      void copyColor();
      return null;
    }
    const start = local(point);
    if (mode === 'text' || mode === 'hand') {
      commitText();
      const index = textIndex(start);
      if (index !== -1) return { kind: 'move-text', start, index, base: history[position] };
      if (mode === 'hand') return null;
    }
    if (mode === 'text') {
      const size = 12 + width * 2;
      textStart = {
        x: Math.min(start.x, Math.max(0, canvas.width - 140)),
        y: Math.min(start.y, Math.max(0, canvas.height - Math.ceil(size * 1.3))),
      };
      input.value = '';
      input.style.color = ink;
      input.style.fontSize = `${Math.max(14, size * canvas.clientWidth / canvas.width)}px`;
      place(form, textStart, 0);
      input.focus({ preventScroll: true });
      return null;
    }
    return { kind: mode, start, end: start, points: [start], ink, width, remaining: history[position] };
  }

  function penNear(mark, target) {
    const radius = Math.max(8, mark.width * 2);
    return mark.points.some((point, i) => {
      const next = mark.points[i + 1] || point;
      const dx = next.x - point.x;
      const dy = next.y - point.y;
      const lengthSquared = dx * dx + dy * dy;
      const fraction = lengthSquared ? Math.max(0, Math.min(1, ((target.x - point.x) * dx + (target.y - point.y) * dy) / lengthSquared)) : 0;
      return Math.hypot(target.x - point.x - dx * fraction, target.y - point.y - dy * fraction) <= radius;
    });
  }

  function update(draft, point, constrain) {
    let end = local(point);
    if (draft.kind === 'move-text') {
      if (draft.base !== history[position]) return;
      const mark = draft.base[draft.index];
      const start = {
        x: Math.max(0, Math.min(canvas.width - mark.textWidth, mark.start.x + end.x - draft.start.x)),
        y: Math.max(0, Math.min(canvas.height - mark.textHeight, mark.start.y + end.y - draft.start.y)),
      };
      draft.marks = draft.base.slice();
      draft.marks[draft.index] = { ...mark, start, end: start };
      render(draft.marks);
      return;
    }
    if (draft.kind === 'eraser') {
      draft.remaining = draft.remaining.filter(mark => mark.kind !== 'pen' || !penNear(mark, end));
      render(draft.remaining);
      return;
    }
    const dx = end.x - draft.start.x;
    const dy = end.y - draft.start.y;
    if (constrain && ['rectangle', 'ellipse'].includes(draft.kind)) {
      const side = Math.min(Math.abs(dx), Math.abs(dy));
      end = { x: draft.start.x + Math.sign(dx) * side, y: draft.start.y + Math.sign(dy) * side };
    } else if (constrain && ['arrow', 'line'].includes(draft.kind)) {
      const angle = Math.round(Math.atan2(dy, dx) / (Math.PI / 4)) * Math.PI / 4;
      const length = Math.hypot(dx, dy);
      end = { x: draft.start.x + Math.cos(angle) * length, y: draft.start.y + Math.sin(angle) * length };
    }
    draft.end = end;
    if (draft.kind === 'pen') {
      const last = draft.points.at(-1);
      if (end.x !== last.x || end.y !== last.y) draft.points.push(end);
    }
    render(history[position], draft);
  }

  function finish(draft, canceled) {
    if (canceled) render();
    else if (draft.kind === 'move-text') {
      if (draft.base !== history[position]) return;
      const from = draft.base[draft.index].start;
      const to = draft.marks[draft.index].start;
      if (from.x !== to.x || from.y !== to.y) saveHistory(draft.marks);
    }
    else if (draft.kind === 'eraser') {
      if (draft.remaining.length !== history[position].length) saveHistory(draft.remaining);
    } else if (draft.kind === 'pen' || draft.end.x !== draft.start.x || draft.end.y !== draft.start.y) {
      const { remaining, ...mark } = draft;
      saveHistory([...history[position], mark]);
    }
  }

  function handleKey(event) {
    if (event.key === 'Escape' && !form.hidden) {
      form.hidden = true;
      overlay.focus({ preventScroll: true });
      return true;
    }
    if (event.target === input) {
      if (event.key === 'Enter' && event.ctrlKey) {
        form.requestSubmit();
        return true;
      }
      return false;
    }
    if (event.key === 'Escape' && (!palette.panel.hidden || !widths.panel.hidden)) {
      closeChoices();
      overlay.focus({ preventScroll: true });
      return true;
    }
    if (mode === 'picker') {
      if (event.key === 'Shift' && !event.repeat) switchFormat();
      else if (event.key.toLowerCase() === 'c' && !event.ctrlKey) void copyColor();
      else if (event.key === 'Escape') selectTool('hand');
      else return false;
      return true;
    }
    if (event.ctrlKey && event.key.toLowerCase() === 'z') moveHistory(event.shiftKey ? 1 : -1);
    else if (event.ctrlKey && event.key.toLowerCase() === 'y') moveHistory(1);
    else return false;
    return true;
  }

  return { canvas, setRegion, begin, update, finish, hover, handleKey, commitText, canMoveText };
}
