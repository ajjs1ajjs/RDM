// Tauri-to-WPF WebView2 Compatibility Bridge Layer

interface PendingPromise {
  resolve: (value: any) => void;
  reject: (reason: any) => void;
}

const pendingPromises = new Map<string, PendingPromise>();
const eventListeners = new Map<string, Set<(event: any) => void>>();

// Listen for messages from C# WPF
if (typeof window !== 'undefined') {
  // Add listener to window.chrome.webview if it exists
  const checkWebview = () => {
    const win = window as any;
    if (win.chrome && win.chrome.webview) {
      win.chrome.webview.addEventListener('message', (event: any) => {
        const msg = event.data;
        if (msg.type === 'response') {
          const promise = pendingPromises.get(msg.id);
          if (promise) {
            pendingPromises.delete(msg.id);
            if (msg.success) {
              promise.resolve(msg.result);
            } else {
              promise.reject(msg.error || 'Unknown error');
            }
          }
        } else if (msg.type === 'event') {
          const listeners = eventListeners.get(msg.name);
          if (listeners) {
            listeners.forEach((handler) => handler({ payload: msg.payload }));
          }
        }
      });
      console.log('WebView2 bridge connected successfully.');
    } else {
      setTimeout(checkWebview, 50);
    }
  };
  checkWebview();
}

// 1. Mock/bridge for `@tauri-apps/api/core::invoke`
export async function invoke(cmd: string, args: any = {}): Promise<any> {
  // Check if we are running inside C# WebView2
  const win = window as any;
  if (win.chrome && win.chrome.webview) {
    return new Promise((resolve, reject) => {
      const callId = cmd + '_' + Math.random().toString(36).substring(2, 11);
      pendingPromises.set(callId, { resolve, reject });
      win.chrome.webview.postMessage({
        id: callId,
        cmd: cmd,
        args: args
      });
    });
  } else {
    // Fallback to console log in dev
    console.warn(`Tauri invoke "${cmd}" fallback. C# host not found. Args:`, args);
    return Promise.reject(`C# Host not found for command "${cmd}"`);
  }
}

// 2. Mock/bridge for `@tauri-apps/api/event::listen`
export async function listen(
  eventName: string,
  handler: (event: any) => void
): Promise<() => void> {
  let listeners = eventListeners.get(eventName);
  if (!listeners) {
    listeners = new Set();
    eventListeners.set(eventName, listeners);
  }
  listeners.add(handler);

  // Return unsubscribe function
  return () => {
    const active = eventListeners.get(eventName);
    if (active) {
      active.delete(handler);
      if (active.size === 0) {
        eventListeners.delete(eventName);
      }
    }
  };
}

// 3. Mock/bridge for `@tauri-apps/plugin-dialog`
export async function open(options: any = {}): Promise<string | string[] | null> {
  return invoke('show_open_dialog', options);
}

export async function save(options: any = {}): Promise<string | null> {
  return invoke('show_save_dialog', options);
}
