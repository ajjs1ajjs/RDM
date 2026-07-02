/// <reference lib="webworker" />

interface RdpFramePayload {
  session_id: string;
  data: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

type InMessage =
  | { type: "init"; canvas: OffscreenCanvas }
  | { type: "frame"; payload: RdpFramePayload };

let canvas: OffscreenCanvas | null = null;
let ctx: OffscreenCanvasRenderingContext2D | null = null;
let frameCount = 0;
const imageDataPool = new Map<string, ImageData>();
let frameQueue: RdpFramePayload[] = [];
let scheduled = false;

function flush() {
  scheduled = false;
  if (!ctx || frameQueue.length === 0) return;
  const batch = frameQueue;
  frameQueue = [];

  for (const payload of batch) {
    const w = payload.width;
    const h = payload.height;
    const pixelCount = w * h;
    if (pixelCount === 0) continue;

    const key = `${w},${h}`;
    let imageData = imageDataPool.get(key);
    if (!imageData) {
      imageData = new ImageData(w, h);
      imageDataPool.set(key, imageData);
    }
    const rgba = imageData.data;

    // Decode base64 + BGR→RGB swap in a single pass
    const binaryStr = atob(payload.data);
    const srcLen = binaryStr.length;
    const outLen = rgba.length;
    const stop = Math.min(srcLen, outLen);
    for (let si = 0, di = 0; si < stop; si += 4, di += 4) {
      rgba[di] = binaryStr.charCodeAt(si + 2);
      rgba[di + 1] = binaryStr.charCodeAt(si + 1);
      rgba[di + 2] = binaryStr.charCodeAt(si);
      rgba[di + 3] = 255;
    }

    ctx.putImageData(imageData, payload.x, payload.y);
    frameCount++;
  }
  (self as DedicatedWorkerGlobalScope).postMessage({
    type: "rendered",
    count: frameCount,
  });
}

self.addEventListener("message", (e: MessageEvent<InMessage>) => {
  const msg = e.data;
  if (msg.type === "init") {
    canvas = msg.canvas;
    ctx = canvas.getContext("2d", { alpha: false });
  } else if (msg.type === "frame") {
    frameQueue.push(msg.payload);
    if (!scheduled) {
      scheduled = true;
      // Yield to message loop so multiple frames can be coalesced
      setTimeout(flush, 0);
    }
  }
});
