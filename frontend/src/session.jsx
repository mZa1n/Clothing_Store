import { createContext, useContext, useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { api, clearCsrf } from './api';
import { ErrorMessage, Loading } from './ui';

const Session = createContext(null);
export const useSession = () => useContext(Session);
export const canShop = (account) => account?.roles.includes('Customer');
export const canManage = (account) => account?.roles.some((role) => role === 'Manager' || role === 'Admin');
export const isAdmin = (account) => account?.roles.includes('Admin');

export function SessionProvider({ children }) {
  const [state, setState] = useState({ loading: true });
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    api('/auth/me', { signal: controller.signal })
      .then((account) => setState({ account }))
      .catch((error) => {
        if (!controller.signal.aborted)
          setState(error.status === 401 ? { account: null } : { error });
      });
    return () => controller.abort();
  }, [attempt]);

  useEffect(() => {
    const expire = () => setState({ account: null });
    window.addEventListener('clothing-session-expired', expire);
    return () => window.removeEventListener('clothing-session-expired', expire);
  }, []);

  async function login(credentials) {
    clearCsrf();
    const account = await api('/auth/login', { method: 'POST', body: credentials });
    clearCsrf();
    setState({ account });
    return account;
  }

  async function logout() {
    try {
      await api('/auth/logout', { method: 'POST' });
    } catch (error) {
      if (error.status !== 401) throw error;
    }
    clearCsrf();
    setState({ account: null });
  }

  return <Session.Provider value={{
    ...state, login, logout, updateAccount: (account) => setState({ account }),
    retry: () => { setState({ loading: true }); setAttempt((value) => value + 1); },
  }}>{children}</Session.Provider>;
}

export function Guard({ allow, children }) {
  const { account, loading, error, retry } = useSession();
  const location = useLocation();
  if (loading) return <Loading />;
  if (error) return <div className="stack"><ErrorMessage error={error} />
    <button className="button secondary" type="button" onClick={retry}>Повторить</button>
  </div>;
  if (!account) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (allow && !allow(account)) return <div className="empty"><h1>Доступ ограничен</h1><p>Этот раздел недоступен вашей роли.</p></div>;
  return children;
}
