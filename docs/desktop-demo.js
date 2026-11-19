(() => {
  'use strict';

  const english = document.documentElement.lang === 'en';
  const messages = english ? {
    selected: name => `Selected “${name}.” Double-click the image or choose Preview selected image.`,
    startCapture: 'Drag over the sample note, or select the entire image from the toolbar.',
    newCapture: 'New screenshot',
    collected: 'Added to the shelf. Double-click the thumbnail or choose Preview selected image.',
    openEditor: 'Click the pen icon at the top of the preview to show the annotation tools.',
    undo: 'Undid the last change.',
    rotatedLeft: 'Rotated 90° counterclockwise.',
    rotatedRight: 'Rotated 90° clockwise.',
    pinned: 'Drag to move. Scroll to resize, hold Ctrl while scrolling to change opacity, or double-click to close.',
    cropped: (width, height) => `Selected ${width} × ${height} pixels. Click the check mark to finish.`,
    marked: 'Annotation added. Undo it or pin the image to the desktop.',
    tour: 'Watch demo',
    stopTour: 'Stop demo',
    tourCrop: 'Select part of the sample note.',
    tourMark: 'Use an arrow to point out a detail.',
    reset: 'Select Capture to crop the sample note, or open the annotation tools in the preview.',
    cancelCapture: 'Capture canceled.',
    closePreview: 'Preview closed. Annotations are kept in the demo copy.',
    closePin: 'Pinned image closed.',
    shelfHidden: 'Screenshot shelf hidden. Click the Snapline icon in the system tray to reopen it.',
    shelfShown: 'Screenshot shelf shown.',
    whole: 'Entire sample image selected. Click the check mark to finish.',
    annotate: 'Choose Pen or Arrow, then drag on the image to annotate.',
    saved: 'Saved to the demo copy.',
    captureReady: 'The entire sample is selected. Reselect an area or use the toolbar to annotate, pick a color, or save.',
    savedCapture: 'PNG download started. You can keep editing this capture.',
    design: 'Design review',
    weekend: 'Weekend ideas',
    notes: 'A good idea',
    unavailable: 'The sample images could not load. Refresh the page to try again.',
  } : {
    selected: name => `已选中「${name}」。双击图片或点击「预览选中图片」。`,
    startCapture: '在示例笔记上拖动框选，也可用工具栏选择整张。',
    newCapture: '刚刚截取的图片',
    collected: '截图已挂到晾衣绳。双击缩略图或点击「预览选中图片」。',
    openEditor: '点击预览顶部的画笔图标展开标注工具。',
    undo: '已撤销上一步。',
    rotatedLeft: '已向左旋转 90°。',
    rotatedRight: '已向右旋转 90°。',
    pinned: '拖住图片直接移动。滚轮缩放，Ctrl+滚轮调节透明度，双击关闭。',
    cropped: (width, height) => `已框选 ${width} × ${height} 像素。点击工具栏上的对勾完成截图。`,
    marked: '已添加标注。可以撤销，或把结果贴到桌面。',
    tour: '观看演示',
    stopTour: '停止演示',
    tourCrop: '框选示例笔记中需要的区域。',
    tourMark: '用箭头标出重点。',
    reset: '点击「截图」框选示例笔记，或打开预览里的标注工具。',
    cancelCapture: '已取消截图。',
    closePreview: '预览已关闭，标注已保留在演示图片上。',
    closePin: '贴图已关闭。',
    shelfHidden: '晾衣绳已收起。点击右下角的 Snapline 图标可再次展开。',
    shelfShown: '晾衣绳已展开。',
    whole: '已选择整张示例图片。点击工具栏上的对勾完成截图。',
    annotate: '选择画笔或箭头后，在图片上拖动标注。',
    saved: '已保存到演示副本。',
    captureReady: '已选中整张示例图。可重新框选，也可直接用工具栏标注、取色或保存。',
    savedCapture: '已开始下载 PNG，可以继续编辑这次截图。',
    design: '页面设计评审',
    weekend: '周末灵感',
    notes: '一个好点子',
    unavailable: '示例图片未能加载，刷新页面后可重试。',
  };

  const demo = document.querySelector('#desktop-demo');
  const find = selector => demo.querySelector(selector);
  const button = action => find(`[data-action="${action}"]`);
  const desktop = find('.demo-desktop');
  const note = find('.demo-source');
  const source = find('.demo-source-image img');
  const capture = find('.demo-capture');
  const selection = find('.demo-selection');
  const captureActions = find('.demo-capture-actions');
  const shelf = find('.demo-shelf');
  const shots = find('.demo-shots');
  const rope = find('.demo-rope');
  const editor = find('.demo-editor');
  const editorTools = find('.demo-editor-tools');
  const editorCanvas = editor.querySelector('canvas');
  const editorZoom = find('.demo-zoom');
  const pin = find('.demo-pin');
  const pinCanvas = pin.querySelector('canvas');
  const pinHandle = find('.demo-pin-handle');
  const size = find('[data-adjust="size"]');
  const opacity = find('[data-adjust="opacity"]');
  const status = find('.demo-status');
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const bounds = (value, min, max) => Math.max(min, Math.min(value, max));
  const say = message => { status.textContent = message; };
  const captureTools = createCaptureTools(capture, captureActions, source, say, english);
  const whole = () => ({ x: 0, y: 0, w: source.naturalWidth, h: source.naturalHeight });
  let samples, images, selected, editing, crop, pinX, pinY, gesture;
  let zoom = 1;
  let tool = 'pen';
  let ink = '#ff4500';
  let history = [];
  let tourTimer, tourSteps, highlighted, autoObserver;

  function canvasCopy(image, x = 0, y = 0, w = image.width, h = image.height) {
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    canvas.getContext('2d').drawImage(image, x, y, w, h, 0, 0, w, h);
    return canvas;
  }

  async function loadImage(path) {
    const image = new Image();
    image.src = new URL(path, source.src).href;
    await image.decode();
    return image;
  }

  function showShelf(show) {
    shelf.hidden = !show;
    button('shelf').setAttribute('aria-pressed', String(show));
    if (show) drawRope();
  }

  function showNote(show) {
    note.hidden = !show;
    button('note').setAttribute('aria-pressed', String(show));
  }

  function ropeSag() {
    return Math.min(28, desktop.clientWidth * .055);
  }

  function positionShots() {
    const rail = shots.getBoundingClientRect();
    if (!rail.width) return;
    for (const item of shots.children) {
      const x = item.getBoundingClientRect().left - rail.left + item.offsetWidth / 2;
      const t = bounds(x / rail.width, 0, 1);
      item.style.transform = `translateY(${4 * ropeSag() * t * (1 - t) - 32}px)`;
    }
  }

  function drawRope() {
    if (shelf.hidden || !rope.clientWidth) return;
    const width = rope.clientWidth;
    const ratio = window.devicePixelRatio;
    rope.width = Math.round(width * ratio);
    rope.height = Math.round(36 * ratio);
    const ctx = rope.getContext('2d');
    ctx.scale(ratio, ratio);
    for (const [color, stroke, y] of [['#998363', 3.4, 4], ['#c9b28c', 2.8, 4], ['#ecdcbd', 1, 3.35]]) {
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.quadraticCurveTo(width / 2, y + ropeSag() * 2, width, y);
      ctx.strokeStyle = color;
      ctx.lineWidth = stroke;
      ctx.stroke();
    }
    positionShots();
  }

  function selectImage(image) {
    selected = image;
    for (const item of shots.children) item.setAttribute('aria-pressed', String(item === selected.thumbnail));
  }

  function renderShots() {
    shots.replaceChildren(...images.map(image => {
      const item = document.createElement('button');
      item.type = 'button';
      item.className = 'demo-shot';
      item.title = image.name;
      item.setAttribute('aria-label', image.name);
      const thumb = document.createElement('canvas');
      const scale = Math.min(180 / image.canvas.width, 120 / image.canvas.height, 1);
      thumb.width = Math.max(1, Math.round(image.canvas.width * scale));
      thumb.height = Math.max(1, Math.round(image.canvas.height * scale));
      item.style.setProperty('--thumb-width', `clamp(${Math.max(32, thumb.width * .5)}px, ${thumb.width / 12}vw, ${thumb.width}px)`);
      thumb.getContext('2d').drawImage(image.canvas, 0, 0, thumb.width, thumb.height);
      thumb.setAttribute('aria-hidden', 'true');
      item.append(thumb);
      item.addEventListener('click', () => {
        selectImage(image);
        say(messages.selected(image.name));
      });
      item.addEventListener('dblclick', () => openEditor(image));
      image.thumbnail = item;
      return item;
    }));
    selectImage(selected);
    if (!shelf.hidden && shots.clientWidth) positionShots();
  }

  function drawCrop(rect) {
    crop = rect;
    const valid = rect && rect.w >= 8 && rect.h >= 8;
    selection.hidden = !valid;
    button('collect').disabled = button('pin-crop').disabled = button('save-crop').disabled = !valid;
    button('save-crop').hidden = button('pin-crop').hidden = button('collect').hidden = !valid;
    button('whole').hidden = Boolean(valid);
    button('reselect').hidden = !valid;
    captureTools.setRegion(valid ? rect : null);
    if (capture.hidden) return;
    const frame = capture.getBoundingClientRect();
    const img = source.getBoundingClientRect();
    const area = rect || whole();
    const left = img.left - frame.left + img.width * area.x / source.naturalWidth;
    const top = img.top - frame.top + img.height * area.y / source.naturalHeight;
    const width = img.width * area.w / source.naturalWidth;
    const height = img.height * area.h / source.naturalHeight;
    Object.assign(selection.style, { left: `${left}px`, top: `${top}px`, width: `${width}px`, height: `${height}px` });
    find('.demo-crop-size').textContent = valid ? `${area.w} × ${area.h}` : '';
    const toolsWidth = captureActions.offsetWidth;
    const toolsHeight = captureActions.offsetHeight;
    captureActions.style.left = `${bounds(left + width - toolsWidth, 6, frame.width - toolsWidth - 6)}px`;
    const below = top + height + 7;
    const above = top - toolsHeight - 32;
    const toolbarTop = below + toolsHeight > frame.height - 6 && above >= 6 ? above : below;
    captureActions.style.top = `${bounds(toolbarTop, 6, frame.height - toolsHeight - 6)}px`;
  }

  function endCapture() {
    if (gesture?.kind === 'crop') gesture = null;
    capture.hidden = captureActions.hidden = true;
    captureActions.removeAttribute('data-dragging');
    drawCrop(null);
  }

  function startCapture() {
    closeEditor();
    pin.hidden = true;
    showNote(true);
    capture.hidden = captureActions.hidden = false;
    drawCrop(null);
    say(messages.startCapture);
  }

  function collect() {
    if (button('collect').disabled) return;
    captureTools.commitText();
    selectImage({ name: messages.newCapture, canvas: canvasCopy(captureTools.canvas) });
    images.unshift(selected);
    images.length = Math.min(images.length, 10);
    endCapture();
    showShelf(true);
    renderShots();
    shots.scrollLeft = 0;
    say(messages.collected);
  }

  function setZoom(value) {
    zoom = bounds(value, .55, 2.5);
    const area = editorCanvas.parentElement;
    if (editor.hidden) return;
    const fit = Math.min(area.clientWidth / editorCanvas.width, area.clientHeight / editorCanvas.height);
    editorCanvas.style.width = `${Math.max(1, Math.floor(editorCanvas.width * fit * zoom))}px`;
    button('zoom-out').disabled = zoom <= .55;
    button('zoom-in').disabled = zoom >= 2.5;
    editorZoom.value = Math.round(zoom * 100);
    find('.demo-zoom-level').textContent = `${Math.round(fit * zoom * 100)}%`;
    find('.demo-editor-info').textContent = `PNG · ${editorCanvas.width} × ${editorCanvas.height}`;
  }

  function setTool(value) {
    tool = value;
    for (const item of editor.querySelectorAll('[data-tool]')) item.setAttribute('aria-pressed', String(item.dataset.tool === tool));
  }

  function setInk(color) {
    ink = color;
    for (const item of editorTools.querySelectorAll('[data-ink]')) item.setAttribute('aria-pressed', String(item.dataset.ink === color));
  }

  function showEditorTools(show) {
    editorTools.hidden = !show;
    button('annotate').setAttribute('aria-expanded', String(show));
    setZoom(zoom);
  }

  function openEditor(image) {
    endCapture();
    if (editing) closeEditor();
    // The editor modifies the demo copy so annotations remain when reopened.
    editing = image;
    selectImage(image);
    history = [];
    zoom = 1;
    setTool('pen');
    setInk('#ff4500');
    editorCanvas.width = image.canvas.width;
    editorCanvas.height = image.canvas.height;
    editorCanvas.getContext('2d').drawImage(image.canvas, 0, 0);
    find('.demo-editor-name').textContent = `${image.name}.png`;
    editor.hidden = false;
    showEditorTools(false);
    button('undo').disabled = true;
    say(messages.openEditor);
  }

  function closeEditor() {
    if (!editing) return;
    if (gesture?.kind === 'mark') {
      restore(gesture.before);
      gesture = null;
    }
    editing.canvas = canvasCopy(editorCanvas);
    editing = null;
    editor.hidden = true;
    history = [];
    renderShots();
  }

  function snapshot() {
    return editorCanvas.getContext('2d').getImageData(0, 0, editorCanvas.width, editorCanvas.height);
  }

  function restore(pixels) {
    if (editorCanvas.width !== pixels.width || editorCanvas.height !== pixels.height) {
      editorCanvas.width = pixels.width;
      editorCanvas.height = pixels.height;
      setZoom(zoom);
    }
    editorCanvas.getContext('2d').putImageData(pixels, 0, 0);
  }

  function remember(pixels) {
    if (history.length === 20) history.shift();
    history.push(pixels);
    button('undo').disabled = false;
  }

  function undo() {
    if (!history.length) return;
    restore(history.pop());
    button('undo').disabled = !history.length;
    say(messages.undo);
  }

  function rotate(direction) {
    remember(snapshot());
    const previous = canvasCopy(editorCanvas);
    editorCanvas.width = previous.height;
    editorCanvas.height = previous.width;
    const ctx = editorCanvas.getContext('2d');
    if (direction > 0) ctx.translate(editorCanvas.width, 0);
    else ctx.translate(0, editorCanvas.height);
    ctx.rotate(direction * Math.PI / 2);
    ctx.drawImage(previous, 0, 0);
    ctx.resetTransform();
    setZoom(zoom);
    say(direction > 0 ? messages.rotatedRight : messages.rotatedLeft);
  }

  function mark(from, to, kind) {
    const ctx = editorCanvas.getContext('2d');
    const weight = Math.max(2, Math.min(editorCanvas.width, editorCanvas.height) / 65);
    ctx.strokeStyle = ink;
    ctx.lineWidth = weight;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    if (kind === 'arrow') {
      const angle = Math.atan2(to.y - from.y, to.x - from.x);
      const length = Math.min(weight * 5, Math.hypot(to.x - from.x, to.y - from.y) * .48);
      for (const direction of [-1, 1]) {
        ctx.moveTo(to.x, to.y);
        ctx.lineTo(to.x - Math.cos(angle + direction * .52) * length, to.y - Math.sin(angle + direction * .52) * length);
      }
    }
    ctx.stroke();
  }

  function movePin(x, y) {
    pinX = bounds(x, 0, Math.max(0, desktop.clientWidth - pin.offsetWidth));
    pinY = bounds(y, 0, Math.max(0, desktop.clientHeight - find('.demo-taskbar').offsetHeight - pin.offsetHeight));
    pin.style.left = `${pinX}px`;
    pin.style.top = `${pinY}px`;
  }

  function adjustPin() {
    pin.style.width = `min(${245 * Number(size.value) / 100}px, 72%)`;
    pinCanvas.style.opacity = Number(opacity.value) / 100;
    movePin(pinX, pinY);
  }

  function showPin(image) {
    pinCanvas.width = image.width;
    pinCanvas.height = image.height;
    pinCanvas.getContext('2d').drawImage(image, 0, 0);
    size.value = opacity.value = '100';
    pin.hidden = false;
    pinX = desktop.clientWidth * .7;
    pinY = desktop.clientHeight * .39;
    adjustPin();
    say(messages.pinned);
  }

  function point(event, surface, width, height) {
    const rect = surface.getBoundingClientRect();
    return {
      x: bounds(Math.round((event.clientX - rect.left) / rect.width * width), 0, width),
      y: bounds(Math.round((event.clientY - rect.top) / rect.height * height), 0, height),
    };
  }

  function track(surface, kind, start, move, finish) {
    surface.addEventListener('pointerdown', event => {
      if (event.button !== 0 || gesture) return;
      const drag = start(event);
      if (!drag) return;
      gesture = { ...drag, kind, pointerId: event.pointerId };
      surface.setPointerCapture(event.pointerId);
    });
    const active = event => gesture?.kind === kind && gesture.pointerId === event.pointerId;
    surface.addEventListener('pointermove', event => { if (active(event)) move(event, gesture); });
    surface.addEventListener('pointerup', event => {
      if (!active(event)) return;
      move(event, gesture);
      finish(gesture, false);
      gesture = null;
    });
    surface.addEventListener('lostpointercapture', event => {
      if (!active(event)) return;
      finish(gesture, true);
      gesture = null;
    });
  }

  const capturePoint = event => point(event, source, source.naturalWidth, source.naturalHeight);
  track(capture, 'crop', event => {
    if (event.target.closest('.demo-capture-text, .demo-color-reading')) return null;
    const start = capturePoint(event);
    if (capture.dataset.mode === 'mark') {
      event.preventDefault();
      capture.focus({ preventScroll: true });
      const annotation = captureTools.begin(start);
      return annotation ? { annotation } : null;
    }
    if (capture.hasAttribute('data-locked')) return null;
    const before = crop;
    const edge = event.target.dataset.edge;
    captureActions.setAttribute('data-dragging', '');
    if (edge) return { before, edge };
    if (before && start.x >= before.x && start.x <= before.x + before.w && start.y >= before.y && start.y <= before.y + before.h) {
      return { before, start, moving: true };
    }
    drawCrop({ ...start, w: 0, h: 0 });
    return { start, before };
  }, (event, { start, edge, before, annotation, moving }) => {
    const end = capturePoint(event);
    if (annotation) {
      captureTools.update(annotation, end, event.shiftKey);
      return;
    }
    if (moving) {
      const x = bounds(before.x + end.x - start.x, 0, source.naturalWidth - before.w);
      const y = bounds(before.y + end.y - start.y, 0, source.naturalHeight - before.h);
      drawCrop({ ...before, x, y });
      return;
    }
    if (edge) {
      const right = before.x + before.w;
      const bottom = before.y + before.h;
      const x = edge.includes('w') ? Math.min(end.x, right - 8) : before.x;
      const y = edge.includes('n') ? Math.min(end.y, bottom - 8) : before.y;
      const w = (edge.includes('e') ? Math.max(end.x, x + 8) : right) - x;
      const h = (edge.includes('s') ? Math.max(end.y, y + 8) : bottom) - y;
      drawCrop({ x, y, w, h });
      return;
    }
    drawCrop({ x: Math.min(start.x, end.x), y: Math.min(start.y, end.y), w: Math.abs(end.x - start.x), h: Math.abs(end.y - start.y) });
  }, ({ before, annotation }, canceled) => {
    if (annotation) {
      captureTools.finish(annotation, canceled);
      if (!canceled && annotation.kind !== 'eraser') say(messages.marked);
    } else {
      captureActions.removeAttribute('data-dragging');
      if (canceled) drawCrop(before);
      else if (!button('collect').disabled) say(messages.cropped(crop.w, crop.h));
    }
  });
  capture.addEventListener('pointermove', event => {
    if (!event.target.closest('.demo-color-reading')) captureTools.hover(capturePoint(event));
  });
  capture.addEventListener('pointerleave', () => captureTools.hover({ x: -1, y: -1 }));

  const editorPoint = event => point(event, editorCanvas, editorCanvas.width, editorCanvas.height);
  track(editorCanvas, 'mark', event => {
    if (editorTools.hidden) return null;
    const start = editorPoint(event);
    return { start, previous: start, before: snapshot(), tool, changed: false };
  }, (event, drag) => {
    const end = editorPoint(event);
    if (end.x === drag.previous.x && end.y === drag.previous.y) return;
    if (drag.tool === 'arrow') restore(drag.before);
    mark(drag.tool === 'arrow' ? drag.start : drag.previous, end, drag.tool);
    drag.previous = end;
    drag.changed = true;
  }, (drag, canceled) => {
    if (canceled) restore(drag.before);
    else if (drag.changed) {
      remember(drag.before);
      say(messages.marked);
    }
  });

  track(pinHandle, 'pin', event => ({ x: pinX - event.clientX, y: pinY - event.clientY }),
    (event, drag) => movePin(drag.x + event.clientX, drag.y + event.clientY), () => {});
  pinHandle.addEventListener('dblclick', closePin);
  pinHandle.addEventListener('keydown', event => {
    const directions = { ArrowUp: [0, -1], ArrowDown: [0, 1], ArrowLeft: [-1, 0], ArrowRight: [1, 0] };
    if (!Object.hasOwn(directions, event.key)) return;
    event.preventDefault();
    const [x, y] = directions[event.key];
    movePin(pinX + x * 12, pinY + y * 12);
  });
  size.addEventListener('input', adjustPin);
  opacity.addEventListener('input', adjustPin);
  editorZoom.addEventListener('input', () => setZoom(Number(editorZoom.value) / 100));
  shots.addEventListener('scroll', positionShots);
  pin.addEventListener('wheel', event => {
    if (event.target.matches('input')) return;
    event.preventDefault();
    const control = event.ctrlKey ? opacity : size;
    control.value = Number(control.value) - Math.sign(event.deltaY) * 5;
    adjustPin();
  }, { passive: false });

  function stopTour() {
    autoObserver?.disconnect();
    clearTimeout(tourTimer);
    if (!tourSteps) return;
    tourSteps = null;
    highlighted?.classList.remove('demo-tour-focus');
    highlighted = null;
    button('tour').setAttribute('aria-pressed', 'false');
    button('tour').textContent = messages.tour;
  }

  function playTour() {
    stopTour();
    button('tour').setAttribute('aria-pressed', 'true');
    button('tour').textContent = messages.stopTour;
    tourSteps = [
      [capture, startCapture],
      [button('collect'), () => {
        drawCrop({ x: 32, y: 26, w: 552, h: 330 });
        say(messages.tourCrop);
      }],
      [button('open'), collect],
      [editor, () => openEditor(selected)],
      [editorCanvas, () => {
        showEditorTools(true);
        const { width, height } = editorCanvas;
        remember(snapshot());
        mark({ x: width * .23, y: height * .22 }, { x: width * .6, y: height * .5 }, 'arrow');
        say(messages.tourMark);
      }],
      [pin, () => pinPreview(false)],
    ];
    const next = () => {
      highlighted?.classList.remove('demo-tour-focus');
      const step = tourSteps?.shift();
      if (!step) {
        stopTour();
        return;
      }
      [highlighted] = step;
      step[1]();
      highlighted.classList.add('demo-tour-focus');
      tourTimer = setTimeout(next, 1450);
    };
    next();
  }

  function reset() {
    stopTour();
    gesture = editing = null;
    history = [];
    editor.hidden = pin.hidden = true;
    endCapture();
    images = samples.map(image => ({ ...image }));
    selectImage(images[0]);
    showNote(true);
    showShelf(true);
    renderShots();
    shots.scrollLeft = 0;
    openEditor(selected);
    say(messages.reset);
  }

  function cancelCapture() {
    endCapture();
    say(messages.cancelCapture);
  }

  function closePreview() {
    closeEditor();
    (shelf.hidden ? button('shelf') : button('open')).focus({ preventScroll: true });
    say(messages.closePreview);
  }

  function pinPreview(focus = true) {
    showPin(editorCanvas);
    closeEditor();
    if (focus) pinHandle.focus({ preventScroll: true });
  }

  function pinCrop() {
    captureTools.commitText();
    showPin(captureTools.canvas);
    endCapture();
    pinHandle.focus({ preventScroll: true });
  }

  function saveCrop() {
    captureTools.commitText();
    captureTools.canvas.toBlob(blob => {
      if (!blob) return;
      const url = URL.createObjectURL(blob);
      const download = document.createElement('a');
      download.href = url;
      download.download = 'snapline-capture.png';
      download.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
      say(messages.savedCapture);
    });
  }

  function closePin() {
    if (gesture?.kind === 'pin') gesture = null;
    pin.hidden = true;
    button('capture').focus({ preventScroll: true });
    say(messages.closePin);
  }

  const actions = {
    capture: () => {
      startCapture();
      button('whole').focus({ preventScroll: true });
    },
    shelf: () => {
      showShelf(shelf.hidden);
      say(shelf.hidden ? messages.shelfHidden : messages.shelfShown);
    },
    collapse: () => {
      showShelf(false);
      say(messages.shelfHidden);
      button('shelf').focus({ preventScroll: true });
    },
    note: () => {
      if (!capture.hidden) cancelCapture();
      showNote(note.hidden);
    },
    'close-source': () => {
      showNote(false);
      button('note').focus({ preventScroll: true });
    },
    tour: () => tourSteps ? stopTour() : playTour(),
    reset,
    whole: () => {
      drawCrop(whole());
      say(messages.whole);
      button('collect').focus({ preventScroll: true });
    },
    reselect: () => {
      drawCrop(null);
      say(messages.startCapture);
      capture.focus({ preventScroll: true });
    },
    'save-crop': saveCrop,
    cancel: () => {
      cancelCapture();
      button('capture').focus({ preventScroll: true });
    },
    collect: () => {
      collect();
      button('open').focus({ preventScroll: true });
    },
    open: () => {
      openEditor(selected);
      button('annotate').focus({ preventScroll: true });
    },
    'close-editor': closePreview,
    annotate: () => {
      showEditorTools(editorTools.hidden);
      if (!editorTools.hidden) say(messages.annotate);
    },
    save: () => {
      closePreview();
      say(messages.saved);
    },
    undo,
    rotate: () => rotate(1),
    'rotate-left': () => rotate(-1),
    fit: () => setZoom(1),
    'zoom-out': () => setZoom(zoom / 1.22),
    'zoom-in': () => setZoom(zoom * 1.22),
    pin: pinPreview,
    'pin-crop': pinCrop,
    'close-pin': closePin,
  };
  demo.addEventListener('pointerdown', event => {
    if (!event.target.closest('[data-action="tour"]')) stopTour();
  });
  demo.addEventListener('focusin', () => { if (button('tour').getAttribute('aria-pressed') === 'false') stopTour(); });
  demo.addEventListener('click', event => {
    const item = event.target.closest('button');
    if (!item) return;
    if (item.dataset.action !== 'tour') stopTour();
    if (item.dataset.action) actions[item.dataset.action]();
    if (item.dataset.tool) setTool(item.dataset.tool);
    if (item.dataset.ink) setInk(item.dataset.ink);
  });
  demo.addEventListener('keydown', event => {
    const togglesTour = event.target === button('tour') && (event.key === 'Enter' || event.key === ' ');
    if (!togglesTour) stopTour();
    if (!capture.hidden) {
      if (captureTools.handleKey(event)) {
        event.preventDefault();
        return;
      }
      if (event.target.matches('textarea')) return;
    }
    if (event.key === 'Escape') {
      if (!capture.hidden) actions.cancel();
      else if (!editor.hidden) closePreview();
      else if (!pin.hidden) closePin();
      else return;
    } else if (event.key === 'F1') {
      actions.capture();
    } else if (event.key === 'Enter' && event.target.tagName !== 'BUTTON' && !capture.hidden && !button('collect').disabled) {
      actions.collect();
    } else if (event.key.toLowerCase() === 'a' && event.ctrlKey && !capture.hidden) {
      actions.whole();
    } else if (event.key.toLowerCase() === 's' && event.ctrlKey && !capture.hidden && !button('save-crop').disabled) {
      saveCrop();
    } else if (event.key.toLowerCase() === 't' && event.ctrlKey && !event.altKey && !capture.hidden && !button('pin-crop').disabled) {
      pinCrop();
    } else if (event.key.toLowerCase() === 't' && event.ctrlKey && event.altKey) {
      actions.shelf();
    } else if (event.key.toLowerCase() === 'z' && event.ctrlKey && editing) {
      undo();
    } else return;
    event.preventDefault();
  });
  document.querySelector('[data-try-capture]').addEventListener('click', event => {
    if (demo.hidden || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    stopTour();
    startCapture();
    drawCrop(whole());
    say(messages.captureReady);
    location.hash = 'try';
    document.querySelector('#try').scrollIntoView();
    capture.focus({ preventScroll: true });
  });
  new ResizeObserver(() => {
    drawRope();
    if (!capture.hidden) drawCrop(crop);
    if (!editor.hidden) setZoom(zoom);
    if (!pin.hidden) movePin(pinX, pinY);
  }).observe(desktop);

  async function init() {
    const [, design, weekend] = await Promise.all([
      source.decode(),
      loadImage(english ? 'en-design-review.png' : '02-image-preview.png'),
      loadImage(english ? 'en-capture-annotation.png' : '05-capture-annotation.png'),
    ]);
    samples = [
      { name: messages.design, canvas: english ? canvasCopy(design) : canvasCopy(design, 100, 103, 900, 560) },
      { name: messages.weekend, canvas: canvasCopy(weekend) },
      { name: messages.notes, canvas: canvasCopy(source) },
    ];
    reset();
    const backdrop = find('.demo-document canvas');
    backdrop.width = weekend.naturalWidth;
    backdrop.height = weekend.naturalHeight;
    backdrop.getContext('2d').drawImage(weekend, 0, 0);
    demo.hidden = false;
    document.querySelector('#desktop-fallback').hidden = true;
    if (!reducedMotion.matches && matchMedia('(pointer: fine)').matches) {
      autoObserver = new IntersectionObserver(([entry]) => {
        if (entry.intersectionRatio >= .65) {
          autoObserver.disconnect();
          tourTimer = setTimeout(() => {
            if (!document.hidden && !reducedMotion.matches) playTour();
          }, 2600);
        }
      }, { threshold: .65 });
      autoObserver.observe(demo);
    }
  }

  init().catch(() => {
    document.querySelector('#desktop-fallback p').textContent = messages.unavailable;
  });
})();
