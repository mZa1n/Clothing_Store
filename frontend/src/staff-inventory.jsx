import { useState } from 'react';
import { Link } from 'react-router-dom';
import { api, query } from './api';
import { useResource } from './data';
import { ActionForm, Empty, Field, Pagination, Resource, Table, displayDate, locationKinds, namedOptions,
  option, readForm } from './ui';

const inventory = '/management/inventory';
const pageSize = 15;

const pointOptions = (locations) => locations.map((point) => option(point.id, locationKinds[point.kind] + ' · ' + point.name));

export function Stock() {
  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState({});
  const [updated, setUpdated] = useState('');
  const [formVersion, setFormVersion] = useState(0);
  const locations = useResource('/catalog/locations');
  const filters = useResource('/catalog/filters');
  const stocks = useResource(query(inventory, { ...filter, page, pageSize }));
  const movements = useResource(query(inventory + '/movements', { pageSize }));

  async function save(kind, values) {
    const body = kind === 'locations' ? values : Object.fromEntries(
      Object.entries(values).map(([key, value]) => [key, Number(value)]),
    );
    await api(inventory + '/' + kind, { method: 'POST', body });
    setUpdated(kind === 'locations' ? 'Точка добавлена.' : 'Операция проведена.');
    if (kind === 'locations') locations.reload();
    else { stocks.reload(); movements.reload(); }
    setFormVersion((value) => value + 1);
  }

  return <div className="stack">
    {updated && <p className="feedback success" role="status">{updated}</p>}
    <Resource state={locations}>{(points) => <>
      <div className="staff-operations">
        <details className="panel"><summary>Новая точка</summary>
          <ActionForm key={'place' + formVersion} className="nested-form" onSubmit={(values) => save('locations', values)} submit="Добавить точку">
            {(error) => <>
              <Field name="kind" label="Тип точки" options={namedOptions(locationKinds)} error={error} />
              <Field name="name" label="Название точки" maxLength={120} required error={error} />
              <Field name="address" label="Адрес" maxLength={240} required error={error} />
            </>}
          </ActionForm>
        </details>
        <details className="panel"><summary>Поступление</summary>
          <ActionForm key={'receipt' + formVersion} className="nested-form" onSubmit={(values) => save('receipts', values)} submit="Провести поступление">
            {(error) => <>
              <VariantId error={error} />
              <Field name="warehouseId" label="Склад поступления" options={pointOptions(points.filter((point) => point.kind === 'warehouse'))}
                placeholder="Выберите склад" required error={error} />
              <Field name="quantity" label="Количество" type="number" min={1} max={1000000} required error={error} />
            </>}
          </ActionForm>
        </details>
        <details className="panel"><summary>Перемещение</summary>
          <ActionForm key={'transfer' + formVersion} className="nested-form" onSubmit={(values) => save('transfers', values)} submit="Провести перемещение">
            {(error) => <>
              <VariantId error={error} />
              <Field name="sourceId" label="Откуда" options={pointOptions(points)} placeholder="Выберите точку" required error={error} />
              <Field name="destinationId" label="Куда" options={pointOptions(points)} placeholder="Выберите точку" required error={error} />
              <Field name="quantity" label="Количество" type="number" min={1} max={1000000} required error={error} />
            </>}
          </ActionForm>
        </details>
      </div>
      <form className="filter-panel" onSubmit={(event) => {
        const values = readForm(event);
        setFilter(values);
        setPage(1);
      }}>
        <div className="field-grid">
          <Field name="locationId" label="Точка учёта" options={pointOptions(points)} placeholder="Все точки" />
          <Field name="categoryId" label="Категория" options={(filters.data?.categories ?? []).map((row) => option(row.id, row.name))} placeholder="Все категории" />
          <Field name="manufacturerId" label="Производитель" options={(filters.data?.manufacturers ?? []).map((row) => option(row.id, row.name))} placeholder="Все производители" />
          <Field name="size" label="Размер" maxLength={24} />
          <Field name="color" label="Цвет" maxLength={60} />
        </div>
        <div className="actions"><button className="button primary">Показать остатки</button>
          <button className="button secondary" type="reset" onClick={() => { setFilter({}); setPage(1); }}>Сбросить</button></div>
      </form>

      <Resource state={stocks}>{(rows) => <>
        {rows.length ? <Table headers={['Товар', 'Точка', 'Остаток', 'Резерв', 'Доступно']}>
          {rows.map((row) => <tr key={row.variantId + ':' + row.locationId}>
            <td>{row.product}<br /><span className="muted">{row.sku} · {row.size} · {row.color} · ID {row.variantId}</span></td>
            <td>{row.location}</td><td>{row.onHand}</td><td>{row.reserved}</td><td>{row.available}</td>
          </tr>)}
        </Table> : <Empty><p>Остатков по заданным условиям нет.</p></Empty>}
        <Pagination page={page} size={pageSize} count={rows.length} onChange={setPage} />
      </>}</Resource>

      <details className="panel"><summary>Последние движения</summary>
        <div className="nested-form">
          <Resource state={movements}>{(rows) => rows.length
            ? <Table headers={['Дата', 'Операция', 'Вариант', 'Откуда', 'Куда', 'Количество']}>
              {rows.map((row) => <tr key={row.id}>
                <td>{displayDate(row.createdAt)}</td><td>{row.kind === 'receipt' ? 'Поступление' : 'Перемещение'}</td>
                <td>ID {row.variantId}</td><td>{points.find((point) => point.id === row.sourceId)?.name ?? '—'}</td>
                <td>{points.find((point) => point.id === row.destinationId)?.name ?? row.destinationId}</td><td>{row.quantity}</td>
              </tr>)}
            </Table>
            : <p>Движений пока нет.</p>}</Resource>
        </div>
      </details>
    </>}</Resource>
  </div>;
}

function VariantId({ error }) {
  return <div>
    <Field name="variantId" label="ID варианта" type="number" min={1} required error={error} />
    <p className="muted">ID указан в вариантах <Link to="/management/catalog">карточки товара</Link>.</p>
  </div>;
}
