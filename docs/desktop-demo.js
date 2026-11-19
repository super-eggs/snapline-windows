(() => {
  'use strict';

  const demo = document.querySelector('#desktop-demo');
  const find = selector => demo.querySelector(selector);
  const button = action => find(`[data-action="${action}"]`);
  const desktop = find('.demo-desktop');
  const source = find('.demo-source-image img');
  const capture = find('.demo-capture');
  const selection = find('.demo-selection');
  const captureActions = find('.demo-capture-actions');
  const shelf = find('.demo-shelf');
  const shots = find('.demo-shots');
  const editor = find('.demo-editor');
  const editorCanvas = editor.querySelector('canvas');
  const pin = find('.demo-pin');
  const pinCanvas = pin.querySelector('canvas');
  const pinHandle = find('.demo-pin-handle');
  const size = find('[data-adjust="size"]');
  const opacity = find('[data-adjust="opacity"]');
  const status = find('.demo-status');
  const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const bounds = (value, min, max) => Math.max(min, Math.min(value, max));
  const say = message => { status.textContent = message; };
  const whole = () => ({ x: 0, y: 0, w: source.naturalWidth, h: source.naturalHeight });
  let samples, images, selected, editing, crop, pinX, pinY, gesture;
  let zoom = 1;
  let tool = 'pen';
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
    image.src = path;
    await image.decode();
    return image;
  }

  function showShelf(show) {
    shelf.hidden = !show;
    button('shelf').setAttribute('aria-pressed', String(show));
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
      const scale = Math.min(144 / image.canvas.width, 88 / image.canvas.height, 1);
      thumb.width = Math.max(1, Math.round(image.canvas.width * scale));
      thumb.height = Math.max(1, Math.round(image.canvas.height * scale));
      thumb.getContext('2d').drawImage(image.canvas, 0, 0, thumb.width, thumb.height);
      thumb.setAttribute('aria-hidden', 'true');
      item.append(thumb);
      item.addEventListener('click', () => {
        selectImage(image);
        say(`已选中「${image.name}」。双击图片或点击「预览选中图片」。`);
      });
      item.addEventListener('dblclick', () => openEditor(image));
      image.thumbnail = item;
      return item;
    }));
    selectImage(selected);
  }

  function drawCrop(rect) {
    crop = rect;
    selection.hidden = !rect;
    button('collect').disabled = !rect || rect.w < 8 || rect.h < 8;
    if (!rect) return;
    const full = whole();
    selection.style.left = `${100 * rect.x / full.w}%`;
    selection.style.top = `${100 * rect.y / full.h}%`;
    selection.style.width = `${100 * rect.w / full.w}%`;
    selection.style.height = `${100 * rect.h / full.h}%`;
  }

  function endCapture() {
    if (gesture?.kind === 'crop') gesture = null;
    capture.hidden = captureActions.hidden = true;
    drawCrop(null);
  }

  function startCapture() {
    closeEditor();
    pin.hidden = true;
    drawCrop(null);
    capture.hidden = captureActions.hidden = false;
    say('拖动鼠标或手指框选，也可以选择整张示例图片。');
  }

  function collect() {
    if (button('collect').disabled) return;
    const { x, y, w, h } = crop;
    selectImage({ name: '刚刚截取的图片', canvas: canvasCopy(source, x, y, w, h) });
    images.unshift(selected);
    images.length = Math.min(images.length, 10);
    endCapture();
    showShelf(true);
    renderShots();
    shots.scrollLeft = 0;
    say('截图已挂到晾衣绳。双击缩略图或点击「预览选中图片」。');
  }

  function setZoom(value) {
    zoom = bounds(value, .55, 2.5);
    const area = editorCanvas.parentElement;
    if (editor.hidden) return;
    const fit = Math.min(area.clientWidth / editorCanvas.width, area.clientHeight / editorCanvas.height);
    editorCanvas.style.width = `${Math.max(1, Math.floor(editorCanvas.width * fit * zoom))}px`;
    button('zoom-out').disabled = zoom <= .55;
    button('zoom-in').disabled = zoom >= 2.5;
  }

  function setTool(value) {
    tool = value;
    for (const item of editor.querySelectorAll('[data-tool]')) item.setAttribute('aria-pressed', String(item.dataset.tool === tool));
  }

  function openEditor(image) {
    endCapture();
    if (editing) closeEditor();
    // The editor modifies the demo copy so annotations remain when reopened.
    editing = image;
    selectImage(image);
    history = [];
    setTool('pen');
    editorCanvas.width = image.canvas.width;
    editorCanvas.height = image.canvas.height;
    editorCanvas.getContext('2d').drawImage(image.canvas, 0, 0);
    find('.demo-editor-name').textContent = image.name;
    editor.hidden = false;
    button('undo').disabled = true;
    setZoom(1);
    say('在图片上拖动即可标注。也可以旋转、放大或贴到桌面。');
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
    say('已撤销上一步。');
  }

  function rotate() {
    remember(snapshot());
    const previous = canvasCopy(editorCanvas);
    editorCanvas.width = previous.height;
    editorCanvas.height = previous.width;
    const ctx = editorCanvas.getContext('2d');
    ctx.translate(editorCanvas.width, 0);
    ctx.rotate(Math.PI / 2);
    ctx.drawImage(previous, 0, 0);
    ctx.resetTransform();
    setZoom(zoom);
    say('已顺时针旋转 90°。');
  }

  function mark(from, to, kind) {
    const ctx = editorCanvas.getContext('2d');
    const weight = Math.max(2, Math.min(editorCanvas.width, editorCanvas.height) / 65);
    ctx.strokeStyle = '#275ddf';
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
    pinY = bounds(y, 0, Math.max(0, desktop.clientHeight - pin.offsetHeight));
    pin.style.left = `${pinX}px`;
    pin.style.top = `${pinY}px`;
  }

  function adjustPin() {
    pin.style.width = `min(${245 * Number(size.value) / 100}px, 72%)`;
    pinCanvas.style.opacity = Number(opacity.value) / 100;
    movePin(pinX, pinY);
  }

  function showPin() {
    pinCanvas.width = editorCanvas.width;
    pinCanvas.height = editorCanvas.height;
    pinCanvas.getContext('2d').drawImage(editorCanvas, 0, 0);
    closeEditor();
    size.value = opacity.value = '100';
    pin.hidden = false;
    pinX = desktop.clientWidth - 270;
    pinY = Math.min(170, desktop.clientHeight - pin.offsetHeight - 18);
    adjustPin();
    say('已贴到示例桌面。拖动贴图标题，或用滑块调整大小和透明度。');
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
      gesture = { kind, pointerId: event.pointerId, ...start(event) };
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

  const capturePoint = event => point(event, capture, source.naturalWidth, source.naturalHeight);
  track(capture, 'crop', event => {
    const start = capturePoint(event);
    drawCrop({ ...start, w: 0, h: 0 });
    return { start };
  }, (event, { start }) => {
    const end = capturePoint(event);
    drawCrop({ x: Math.min(start.x, end.x), y: Math.min(start.y, end.y), w: Math.abs(end.x - start.x), h: Math.abs(end.y - start.y) });
  }, (_, canceled) => {
    if (canceled) drawCrop(null);
    else if (!button('collect').disabled) say(`已框选 ${crop.w} × ${crop.h} 像素。点击「收进晾衣绳」。`);
  });

  const editorPoint = event => point(event, editorCanvas, editorCanvas.width, editorCanvas.height);
  track(editorCanvas, 'mark', event => {
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
      say('已添加标注。可以撤销，或把结果贴到桌面。');
    }
  });

  track(pinHandle, 'pin', event => ({ x: pinX - event.clientX, y: pinY - event.clientY }),
    (event, drag) => movePin(drag.x + event.clientX, drag.y + event.clientY), () => {});
  pinHandle.addEventListener('keydown', event => {
    const directions = { ArrowUp: [0, -1], ArrowDown: [0, 1], ArrowLeft: [-1, 0], ArrowRight: [1, 0] };
    if (!Object.hasOwn(directions, event.key)) return;
    event.preventDefault();
    const [x, y] = directions[event.key];
    movePin(pinX + x * 12, pinY + y * 12);
  });
  size.addEventListener('input', adjustPin);
  opacity.addEventListener('input', adjustPin);
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
    button('tour').textContent = '观看演示';
  }

  function playTour() {
    stopTour();
    button('tour').setAttribute('aria-pressed', 'true');
    button('tour').textContent = '停止演示';
    tourSteps = [
      [capture, startCapture],
      [button('collect'), () => {
        drawCrop({ x: 32, y: 26, w: 552, h: 330 });
        say('框选示例笔记中需要的区域。');
      }],
      [button('open'), collect],
      [editor, () => openEditor(selected)],
      [editorCanvas, () => {
        const { width, height } = editorCanvas;
        remember(snapshot());
        mark({ x: width * .23, y: height * .22 }, { x: width * .6, y: height * .5 }, 'arrow');
        say('用箭头标出重点。');
      }],
      [pin, showPin],
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
    showShelf(true);
    renderShots();
    shots.scrollLeft = 0;
    say('点击「截图」，框选一块示例图片。');
  }

  function cancelCapture() {
    endCapture();
    say('已取消截图。');
  }

  function closePreview() {
    closeEditor();
    (shelf.hidden ? button('shelf') : button('open')).focus({ preventScroll: true });
    say('预览已关闭，标注已保留在演示图片上。');
  }

  function pinPreview() {
    showPin();
    pinHandle.focus({ preventScroll: true });
  }

  function closePin() {
    if (gesture?.kind === 'pin') gesture = null;
    pin.hidden = true;
    button('capture').focus({ preventScroll: true });
    say('贴图已关闭。');
  }

  const actions = {
    capture: () => {
      startCapture();
      button('whole').focus({ preventScroll: true });
    },
    shelf: () => {
      showShelf(shelf.hidden);
      say(shelf.hidden ? '晾衣绳已收起。' : '晾衣绳已展开。');
    },
    tour: () => tourSteps ? stopTour() : playTour(),
    reset,
    whole: () => {
      drawCrop(whole());
      say('已选择整张示例图片。点击「收进晾衣绳」。');
    },
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
      find('[data-tool="pen"]').focus({ preventScroll: true });
    },
    'close-editor': closePreview,
    undo,
    rotate,
    'zoom-out': () => setZoom(zoom / 1.22),
    'zoom-in': () => setZoom(zoom * 1.22),
    pin: pinPreview,
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
  });
  demo.addEventListener('keydown', event => {
    const togglesTour = event.target === button('tour') && (event.key === 'Enter' || event.key === ' ');
    if (!togglesTour) stopTour();
    if (event.key === 'Escape') {
      if (!capture.hidden) actions.cancel();
      else if (!editor.hidden) closePreview();
      else if (!pin.hidden) closePin();
      else return;
    } else if (event.key === 'F1') {
      actions.capture();
    } else if (event.key.toLowerCase() === 't' && event.ctrlKey && event.altKey) {
      actions.shelf();
    } else if (event.key.toLowerCase() === 'z' && event.ctrlKey && editing) {
      undo();
    } else return;
    event.preventDefault();
  });
  new ResizeObserver(() => {
    if (!editor.hidden) setZoom(zoom);
    if (!pin.hidden) movePin(pinX, pinY);
  }).observe(desktop);

  async function init() {
    const [, design, note] = await Promise.all([
      source.decode(),
      loadImage('images/02-image-preview.png'),
      loadImage('images/05-capture-annotation.png'),
    ]);
    samples = [
      { name: '页面设计评审', canvas: canvasCopy(design, 100, 103, 900, 560) },
      { name: '周末灵感', canvas: canvasCopy(note) },
      { name: '一个好点子', canvas: canvasCopy(source) },
    ];
    reset();
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
    document.querySelector('#desktop-fallback p').textContent = '示例图片未能加载，刷新页面后可重试。';
  });
})();
