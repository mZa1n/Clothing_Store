import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { api } from './api';
import { useSession } from './session';
import { ActionForm, Field, Loading } from './ui';

export function Login({ register = false }) {
  const { account, loading, login } = useSession();
  const location = useLocation();
  const navigate = useNavigate();
  const from = location.state?.from ?? '/';
  if (loading) return <Loading />;
  if (account) return <Navigate to={from} replace />;

  async function submit(values) {
    if (register) await api('/auth/register', { method: 'POST', body: values });
    await login(values);
    navigate(from, { replace: true });
  }

  return <section className="auth-panel">
    <h1>{register ? 'Регистрация' : 'Вход'}</h1>
    <ActionForm key={String(register)} onSubmit={submit} submit={register ? 'Создать аккаунт' : 'Войти'}>
      {(error) => <>
        <Field name="email" label="Электронная почта" type="email" autoComplete="username" maxLength={254} required error={error} />
        <Field name="password" label="Пароль" type="password" autoComplete={register ? 'new-password' : 'current-password'}
          minLength={register ? 8 : undefined} maxLength={128} required error={error} />
        {register && <p className="muted">От 8 символов, включая цифру, строчную и заглавную буквы.</p>}
      </>}
    </ActionForm>
    <Link className="text-link" to={register ? '/login' : '/register'} state={location.state}>
      {register ? 'Уже есть аккаунт? Войти' : 'Создать аккаунт покупателя'}
    </Link>
  </section>;
}
