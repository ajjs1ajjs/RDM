import React, { createContext, useContext, useState, useCallback, useRef } from "react";
import { X } from "lucide-react";

type DialogType = "alert" | "confirm" | "prompt";

interface DialogState {
  type: DialogType;
  title: string;
  message: string;
  defaultValue?: string;
  resolve: (value: any) => void;
}

interface DialogsContextType {
  alert: (message: string, title?: string) => Promise<void>;
  confirm: (message: string, title?: string) => Promise<boolean>;
  prompt: (message: string, defaultValue?: string, title?: string) => Promise<string | null>;
}

const DialogsContext = createContext<DialogsContextType>({} as DialogsContextType);

export const useDialogs = () => {
  const ctx = useContext(DialogsContext);
  if (!ctx.alert) throw new Error("useDialogs must be used within DialogsProvider");
  return ctx;
};

export const DialogsProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [state, setState] = useState<DialogState | null>(null);
  const [promptValue, setPromptValue] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);

  const alert = useCallback((message: string, title?: string): Promise<void> => {
    return new Promise((resolve) => {
      setState({ type: "alert", title: title || "", message, resolve });
    });
  }, []);

  const confirm = useCallback((message: string, title?: string): Promise<boolean> => {
    return new Promise((resolve) => {
      setState({ type: "confirm", title: title || "", message, resolve });
    });
  }, []);

  const prompt = useCallback((message: string, defaultValue?: string, title?: string): Promise<string | null> => {
    setPromptValue(defaultValue || "");
    return new Promise((resolve) => {
      setState({ type: "prompt", title: title || "", message, defaultValue, resolve });
    });
  }, []);

  const handleClose = useCallback((result: any) => {
    if (state) {
      state.resolve(result);
      setState(null);
    }
  }, [state]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (e.key === "Escape") {
      if (state?.type === "confirm" || state?.type === "prompt") {
        handleClose(state.type === "confirm" ? false : null);
      } else {
        handleClose(undefined);
      }
    }
    if (e.key === "Enter" && state?.type === "prompt") {
      handleClose(promptValue);
    }
  }, [state, handleClose, promptValue]);

  return (
    <DialogsContext.Provider value={{ alert, confirm, prompt }}>
      {children}
      {state && (
      <div className="modal-overlay" onClick={() => state.type !== "alert" && handleClose(state.type === "confirm" ? false : null)}>
        <div className="modal-box glass-card" onClick={(e) => e.stopPropagation()} style={{ maxWidth: "450px" }} onKeyDown={handleKeyDown}>
          <div className="header-row" style={{ marginBottom: "15px" }}>
            <h3>{state.title || (state.type === "alert" ? "Alert" : state.type === "confirm" ? "Confirm" : "Input Required")}</h3>
            <button className="btn btn-secondary" style={{ padding: "4px" }} onClick={() => handleClose(state.type === "alert" ? undefined : state.type === "confirm" ? false : null)}>
              <X size={16} />
            </button>
          </div>

          <div style={{ display: "flex", flexDirection: "column", gap: "16px" }}>
            <p style={{ color: "var(--text-secondary)", fontSize: "0.9rem", lineHeight: "1.4", whiteSpace: "pre-wrap" }}>
              {state.message}
            </p>

            {state.type === "prompt" && (
              <input
                ref={inputRef}
                type="text"
                className="text-input"
                value={promptValue}
                onChange={(e) => setPromptValue(e.target.value)}
                autoFocus
                placeholder="Enter value..."
                style={{ width: "100%", boxSizing: "border-box" }}
              />
            )}

            <div className="form-actions">
              {state.type === "alert" && (
                <button type="button" className="btn btn-primary" onClick={() => handleClose(undefined)} autoFocus>
                  OK
                </button>
              )}
              {state.type === "confirm" && (
                <>
                  <button type="button" className="btn btn-secondary" onClick={() => handleClose(false)}>Cancel</button>
                  <button type="button" className="btn btn-primary" onClick={() => handleClose(true)} autoFocus>OK</button>
                </>
              )}
              {state.type === "prompt" && (
                <>
                  <button type="button" className="btn btn-secondary" onClick={() => handleClose(null)}>Cancel</button>
                  <button type="button" className="btn btn-primary" onClick={() => handleClose(promptValue)} autoFocus>OK</button>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
      )}
    </DialogsContext.Provider>
  );
};
