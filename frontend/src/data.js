import { useEffect, useState } from 'react';
import { api } from './api';

export function useResource(path) {
  const [revision, setRevision] = useState(0);
  const [snapshot, setSnapshot] = useState(null);

  useEffect(() => {
    if (!path) return;
    const controller = new AbortController();
    api(path, { signal: controller.signal })
      .then((data) => setSnapshot({ path, revision, data }))
      .catch((error) => {
        if (!controller.signal.aborted) setSnapshot({ path, revision, error });
      });
    return () => controller.abort();
  }, [path, revision]);

  const current = snapshot?.path === path && snapshot.revision === revision ? snapshot : null;
  return {
    data: current?.data,
    error: current?.error,
    loading: Boolean(path && !current),
    reload: () => setRevision((value) => value + 1),
  };
}
