"""Recognize text locally and return glyph positions in screenshot pixels."""

import io
import json
import os
import sys
import traceback


def prevent_network(event, _):
    if event in {"socket.connect", "socket.sendto", "socket.getaddrinfo", "socket.gethostbyname", "socket.gethostbyaddr"}:
        raise RuntimeError("Snapline OCR is offline")


def recognize(source):
    import numpy as np
    from PIL import Image
    from rapidocr import RapidOCR

    with Image.open(io.BytesIO(source), formats=["PNG"]) as image:
        if image.width * image.height > 40_000_000:
            raise ValueError("Image exceeds the pixel limit")
        rgba = image.convert("RGBA")
        background = Image.new("RGB", image.size, "white")
        background.paste(rgba, mask=rgba.getchannel("A"))
        pixels = np.asarray(background)[:, :, ::-1].copy()  # RapidOCR takes OpenCV BGR.

    engine = RapidOCR(params={
        "EngineConfig.onnxruntime.intra_op_num_threads": min(4, os.cpu_count() or 1),
        "EngineConfig.onnxruntime.inter_op_num_threads": 1,
    })
    result = engine(pixels, use_cls=False, return_word_box=True, return_single_char_box=True)
    texts, boxes = result.txts, result.boxes
    if texts is None or boxes is None:
        return {"lines": []}
    word_results = result.word_results
    has_glyphs = word_results is not None and len(word_results) == len(texts)
    lines = []
    for index, (text, box) in enumerate(zip(texts, boxes)):
        glyphs = []
        if has_glyphs:
            for word in word_results[index]:
                if len(word) == 3 and isinstance(word[0], str) and np.shape(word[2]) == (4, 2):
                    glyphs.append({"text": word[0], "box": np.asarray(word[2]).tolist()})
        lines.append({"text": text, "box": box.tolist(), "glyphs": glyphs})
    return {"lines": lines}


if __name__ == "__main__":
    sys.addaudithook(prevent_network)
    stdout = os.dup(sys.stdout.fileno())
    os.dup2(sys.stderr.fileno(), sys.stdout.fileno())
    with os.fdopen(stdout, "wb") as output:
        try:
            payload = recognize(sys.stdin.buffer.read())
        except Exception:
            traceback.print_exc()
            payload = {"error": "recognition-failed"}
        output.write(json.dumps(payload, ensure_ascii=False, allow_nan=False).encode("utf-8"))
