import { useParams } from "react-router-dom";
import styles from "./MovieDetail.module.css";

export function MovieDetail() {
  const { id } = useParams<{ id: string }>();

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>Movie #{id}</h1>
      <p className={styles.subtitle}>
        Movie detail page — wire up to your data source here.
      </p>
    </div>
  );
}
