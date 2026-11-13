import { useEffect, useId, useRef, useState } from 'react';

export const statuses = {
  placed: 'Оформлен', confirmed: 'Подтверждён', prepared: 'Подготовлен',
  handedOver: 'Передан', completed: 'Завершён', cancelled: 'Отменён',
};
export const roleNames = { Customer: 'Покупатель', Manager: 'Менеджер', Admin: 'Администратор' };
export const fulfillmentNames = { delivery: 'Доставка', pickup: 'Самовывоз' };
export const locationKinds = { warehouse: 'Склад', store: 'Магазин' };
export const money = new Intl.NumberFormat('ru-RU', { style: 'currency', currency: 'RUB', maximumFractionDigits: 2 });
const dateTime = new Intl.DateTimeFormat('ru-RU', { dateStyle: 'medium', timeStyle: 'short' });
export const displayDate = (value) => dateTime.format(new Date(value));
export const option = (value, label) => ({ value, label });
export const recordOptions = (records = [], label = 'name') => records.map((row) => option(row.id, row[label]));
export const textOptions = (values = []) => values.map((value) => option(value, value));
export const namedOptions = (values) => Object.entries(values).map(([key, value]) => option(key, value));

export function ErrorMessage({ error }) {
  if (!error) return null;
  return <div className="feedback error" role="alert">
    <p>{error.message}</p>
    {error.fields?.[''] && <p>{error.fields['']}</p>}
  </div>;
}

export function Loading() {
  return <p className="feedback" role="status">Загрузка…</p>;
}

export function Empty({ children }) {
  return <div className="empty">{children}</div>;
}

export function Resource({ state, children }) {
  if (state.loading) return <Loading />;
  if (state.error) return <div className="stack">
    <ErrorMessage error={state.error} />
    <button type="button" className="button secondary" onClick={state.reload}>Повторить</button>
  </div>;
  return children(state.data);
}

export function Field({ name, label, error, options, placeholder, multiline, ...props }) {
  const id = useId();
  const message = error?.fields?.[name.toLowerCase()];
  const attributes = { id, name, 'aria-invalid': message ? true : undefined, 'aria-describedby': message ? id + '-error' : undefined, ...props };
  let control;
  if (options) {
    control = <select {...attributes}>
      {placeholder != null && <option value="">{placeholder}</option>}
      {options.map(({ value, label }) => <option key={value} value={value}>{label}</option>)}
    </select>;
  } else if (multiline) {
    control = <textarea {...attributes} placeholder={placeholder} />;
  } else {
    control = <input {...attributes} placeholder={placeholder} />;
  }
  return <div className="field">
    <label htmlFor={id}>{label}</label>
    {control}
    {message && <small className="field-error" id={id + '-error'}>{message}</small>}
  </div>;
}

export function Check({ name, label, defaultChecked }) {
  return <label className="check"><input type="checkbox" name={name} defaultChecked={defaultChecked} /> {label}</label>;
}

export function useAction() {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState(null);
  const busy = useRef(false);
  async function run(action) {
    if (busy.current) return;
    busy.current = true;
    setPending(true);
    setError(null);
    try {
      await action();
    } catch (error) {
      setError(error);
    } finally {
      busy.current = false;
      setPending(false);
    }
  }
  return { pending, error, run };
}

export const readForm = (event) => {
  event.preventDefault();
  return Object.fromEntries(new FormData(event.currentTarget));
};

export function ActionForm({ onSubmit, children, submit = 'Сохранить', className = '' }) {
  const action = useAction();
  return <form className={'form ' + className} onSubmit={(event) => {
    const values = readForm(event);
    return action.run(() => onSubmit(values));
  }}>
    <fieldset disabled={action.pending}>
      {children(action.error)}
      <ErrorMessage error={action.error} />
      <div><button className="button primary" type="submit">{action.pending ? 'Сохраняем…' : submit}</button></div>
    </fieldset>
  </form>;
}

export function Pagination({ page, size, count, onChange }) {
  if (page <= 1 && count < size) return null;
  return <nav className="pagination" aria-label="Страницы">
    <button className="button secondary" disabled={page <= 1} onClick={() => onChange(page - 1)}>Назад</button>
    <span>Страница {page}</span>
    <button className="button secondary" disabled={count < size} onClick={() => onChange(page + 1)}>Дальше</button>
  </nav>;
}

export function Table({ headers, children }) {
  return <div className="table-scroll" tabIndex={0} role="region" aria-label="Таблица, горизонтальная прокрутка">
    <table>
      <thead><tr>{headers.map((header) => <th scope="col" key={header}>{header}</th>)}</tr></thead>
      <tbody>{children}</tbody>
    </table>
  </div>;
}

export function ProductImage({ url, alt = '' }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [url]);
  return <div className="product-image">
    {url && !failed
      ? <img src={url} alt={alt} loading="lazy" onError={() => setFailed(true)} />
      : <span>Фото отсутствует</span>}
  </div>;
}
