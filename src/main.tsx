import ReactDOM from "react-dom/client";
import App from "./App";
import { DialogsProvider } from "./components/AppDialogs";

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <DialogsProvider>
    <App />
  </DialogsProvider>,
);
