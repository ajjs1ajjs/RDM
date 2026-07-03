/// <reference lib="webworker" />

interface RdpFrame {
  data: string;
  x: number;
  y: number;
  width: number;
  height: number;
}

type InMessage =
  | { type: "init"; canvas: OffscreenCanvas }
  | { type: "frames"; frames: RdpFrame[] };

let canvas: OffscreenCanvas | null = null;
let ctx: OffscreenCanvasRenderingContext2D | null = null;
let frameCount = 0;

self.addEventListener("message", (e: MessageEvent<InMessage>) => {
  const msg = e.data;
  if (msg.type === "init") {
    canvas = msg.canvas;
    ctx = canvas.getContext("2d", { alpha: false });
  } else if (msg.type === "frames") {
    if (!ctx) return;
    for (const frame of msg.frames) {
      const w = frame.width;
      const h = frame.height;
      if (w === 0 || h === 0) continue;

      try {
        // High-performance synchronous base64 decode using an optimized JIT-friendly loop
        const binaryStr = atob(frame.data);
        const len = binaryStr.length;
        const bytes = new Uint8ClampedArray(len);
        for (let i = 0; i < len; i++) {
          bytes[i] = binaryStr.charCodeAt(i);
        }
        
        const imageData = new ImageData(bytes, w, h);
        ctx.putImageData(imageData, frame.x, frame.y);
        frameCount++;
      } catch (err) {
        console.error("Failed to render frame in worker:", err);
      }
    }

    (self as DedicatedWorkerGlobalScope).postMessage({
      type: "rendered",
      count: frameCount,
    });
  }
});
