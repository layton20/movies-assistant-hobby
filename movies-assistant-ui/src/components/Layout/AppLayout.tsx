import { Outlet } from "react-router-dom";
import styles from "./AppLayout.module.css";
import { ChatPanel } from "../chatPanel/ChatPanel";

export function AppLayout() {
  return (
    <div className={styles.layout}>
      <main className={styles.main}>
        <Outlet />
      </main>
      <ChatPanel />
    </div>
  );
}
