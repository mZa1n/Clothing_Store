import { useState } from 'react';
import { api, query } from './api';
import { useResource } from './data';
import { ActionForm, Empty, Field, Pagination, Resource, Table, locationKinds, money, namedOptions,
  option, roleNames } from './ui';

function dateInput(date) {
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
}

function periodFromForm({ from, to, locationId }) {
  const start = new Date(from + 'T00:00:00');
  const end = new Date(to + 'T00:00:00');
  end.setDate(end.getDate() + 1);
  if (start >= end) throw new Error('Дата окончания должна быть не раньше даты начала.');
  return { from: start.toISOString(), to: end.toISOString(), locationId };
}

export function SalesReports({ compare = false }) {
  const locations = useResource(compare ? null : '/catalog/locations');
  const today = new Date();
  const [dateDefaults] = useState(() => {
    const first = new Date(today.getFullYear(), today.getMonth(), 1);
    return { from: dateInput(first), to: dateInput(today) };
  });
  const [result, setResult] = useState(null);

  return <div className="stack">
    <ActionForm className="panel" onSubmit={async (values) => {
      const endpoint = compare ? '/admin/reports/sales' : '/management/reports/sales';
      setResult(await api(query(endpoint, periodFromForm(values))));
    }} submit="Показать отчёт">{(error) => <div className="field-grid">
      <Field name="from" label="Дата начала" type="date" defaultValue={dateDefaults.from} required error={error} />
      <Field name="to" label="Дата окончания" type="date" defaultValue={dateDefaults.to} required error={error} />
      {!compare && <Field name="locationId" label="Точка учёта" placeholder="Все точки"
        options={(locations.data ?? []).map((point) => option(point.id, locationKinds[point.kind] + ' · ' + point.name))}
        disabled={locations.loading || Boolean(locations.error)} error={error} />}
    </div>}</ActionForm>
    {result && <>
      <Metrics summary={compare ? result.total : result} />
      {compare && (result.locations.length
        ? <Table headers={['Точка', 'Тип', 'Заказы', 'Выручка', 'Средний чек']}>
          {result.locations.map(({ location, sales }) => <tr key={location.id}>
            <td>{location.name}</td><td>{location.kind === 'warehouse' ? 'Доставка со склада' : 'Самовывоз'}</td>
            <td>{sales.orders}</td><td>{money.format(sales.revenue)}</td><td>{money.format(sales.averageOrder)}</td>
          </tr>)}
        </Table> : <Empty><p>Точек учёта пока нет.</p></Empty>)}
    </>}
  </div>;
}

function Metrics({ summary }) {
  return <dl className="metrics">
    <div className="panel"><dt>Завершённые заказы</dt><dd>{summary.orders}</dd></div>
    <div className="panel"><dt>Выручка</dt><dd>{money.format(summary.revenue)}</dd></div>
    <div className="panel"><dt>Средний чек</dt><dd>{money.format(summary.averageOrder)}</dd></div>
  </dl>;
}

const pageSize = 15;

export function Accounts({ currentId }) {
  const [page, setPage] = useState(1);
  const [saved, setSaved] = useState('');
  const accounts = useResource(query('/admin/accounts', { page, pageSize }));

  return <div className="stack">
    {saved && <p className="feedback success" role="status">{saved}</p>}
    <Resource state={accounts}>{(rows) => <>
      {rows.length ? <div className="stack compact">
        {rows.map((account) => <details key={account.id} className="panel">
          <summary className="account-summary">{account.email} · {account.roles.map((role) => roleNames[role] ?? role).join(', ')}{account.disabled && ' · заблокирован'}</summary>
          {account.id === currentId ? <p className="nested-form">Это ваша учётная запись.</p> :
            <ActionForm className="nested-form" submit="Сохранить доступ" onSubmit={async (values) => {
              await api('/admin/accounts/' + account.id + '/access', {
                method: 'PUT', body: { role: values.role.toLowerCase(), disabled: values.disabled === 'true' },
              });
              setSaved('Доступ для ' + account.email + ' обновлён.');
              accounts.reload();
            }}>{(error) => <>
              {account.fullName && <p>{account.fullName}</p>}
              <div className="field-grid">
                <Field name="role" label="Роль" options={namedOptions(roleNames)}
                  defaultValue={account.roles[0] ?? 'Customer'} error={error} />
                <Field name="disabled" label="Доступ" defaultValue={String(account.disabled)}
                  options={[option('false', 'Разрешён'), option('true', 'Заблокирован')]} error={error} />
              </div>
            </>}</ActionForm>}
        </details>)}
      </div> : <Empty><p>Пользователей нет.</p></Empty>}
      <Pagination page={page} size={pageSize} count={rows.length}
        onChange={(value) => { setPage(value); setSaved(''); }} />
    </>}</Resource>
  </div>;
}
