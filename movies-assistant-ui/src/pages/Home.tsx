import styles from "./Home.module.css";

export function Home() {
  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Movie Dashboard</h1>
      <p className={styles.subtitle}>Your personal cinema guide</p>

      <div className={styles.placeholderGrid}>
        {Array.from({ length: 6 }).map((_, i) => (
          <div key={i} className={styles.card}>
            <div className={styles.cardImage} />
            <div className={styles.cardLines}>
              <div className={styles.line} style={{ width: "70%" }} />
              <div className={styles.line} style={{ width: "45%" }} />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
