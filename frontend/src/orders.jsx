import { useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { api, query } from './api';
import { useResource } from './data';
import { Empty, ErrorMessage, Field, Pagination, Resource, Table, displayDate, fulfillmentNames,
  money, namedOptions, statuses, useAction } from './ui';

const pageSize = 15;
const actionForStatus = {
  placed: ['confirm', 'Подтвердить'], confirmed: ['prepare', 'Подготовить'],
  prepared: ['handover', 'Передать'], handedOver: ['complete', 'Завершить'],
};

export function Status({ value }) {
  return <span className={'status status-' + value}>{statuses[value] ?? value}</span>;
}

export function Orders({ staff = false }) {
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState('');
  const path = staff ? '/management/orders' : '/orders';
  const state = useResource(query(path, { page, pageSize, status: staff ? status : '' }));
  return <div className="stack">
    {!staff && <h1>Мои заказы</h1>}
    {staff && <div className="narrow">
      <Field label="Статус заказа" name="status" value={status} placeholder="Все статусы" options={namedOptions(statuses)}
        onChange={(event) => { setStatus(event.target.value); setPage(1); }} />
    </div>}
    <Resource state={state}>{(orders) => <>
      {orders.length
        ? <Table headers={['Заказ', 'Дата', 'Получение', 'Статус', 'Сумма']}>
          {orders.map((order) => <tr key={order.id}>
            <td><Link to={path + '/' + order.id}>Заказ №{order.id}</Link></td>
            <td>{displayDate(order.createdAt)}</td><td>{fulfillmentNames[order.fulfillment]} · {order.location}</td>
            <td><Status value={order.status} /></td><td>{money.format(order.total)}</td>
          </tr>)}
        </Table>
        : <Empty><p>Заказов нет.</p>{!staff && <Link className="text-link" to="/#catalog">Перейти в каталог</Link>}</Empty>}
      <Pagination page={page} size={pageSize} count={orders.length} onChange={setPage} />
    </>}</Resource>
  </div>;
}

export function Order({ staff = false }) {
  const { id } = useParams();
  const location = useLocation();
  const path = staff ? '/management/orders' : '/orders';
  const endpoint = path + '/' + encodeURIComponent(id);
  const state = useResource(endpoint);
  return <div className="stack">
    <Link className="text-link" to={path}>{staff ? 'Все заказы' : 'Мои заказы'}</Link>
    {location.state?.created && !staff && <p className="feedback success" role="status">Заказ оформлен.</p>}
    <Resource state={state}>{(order) => <OrderContents key={order.id} initialOrder={order} staff={staff} endpoint={endpoint} />}</Resource>
  </div>;
}

function OrderContents({ initialOrder, staff, endpoint }) {
  const [order, setOrder] = useState(initialOrder);
  const [confirming, setConfirming] = useState(false);
  const action = useAction();
  const command = staff
    ? actionForStatus[order.status]
    : ['placed', 'confirmed', 'prepared'].includes(order.status) ? ['cancel', 'Отменить заказ'] : null;
  const requiresConfirmation = command?.[0] === 'cancel' || command?.[0] === 'complete';

  function change() {
    if (requiresConfirmation && !confirming) return setConfirming(true);
    return action.run(async () => {
      setOrder(await api(endpoint + '/' + command[0], { method: 'POST' }));
      setConfirming(false);
    });
  }

  return <div className="stack">
    <div className="section-heading">
      <h1>Заказ №{order.id}</h1><Status value={order.status} />
    </div>
    <div className="split-layout">
      <Table headers={['Товар', 'Количество', 'Цена']}>
        {order.items.map((item) => <tr key={item.variantId}>
          <td>{item.productName}<br /><span className="muted">{item.size} · {item.color} · {item.sku}</span></td>
          <td>{item.quantity}</td><td>{money.format(item.unitPrice)}</td>
        </tr>)}
      </Table>
      <aside className="panel stack compact">
        <h2 className="small-heading">Получение</h2>
        <dl className="info-list">
          <div><dt>Способ</dt><dd>{fulfillmentNames[order.fulfillment]}</dd></div>
          <div><dt>Адрес</dt><dd>{order.address || order.location.address}</dd></div>
          {staff && <div><dt>Точка учёта</dt><dd>{order.location.name}</dd></div>}
          <div><dt>Получатель</dt><dd>{order.fullName}</dd></div>
          <div><dt>Телефон</dt><dd>{order.phone}</dd></div>
          <div><dt>Оформлен</dt><dd>{displayDate(order.createdAt)}</dd></div>
        </dl>
        <div className="line-total grand-total"><span>Итого</span><strong>{money.format(order.total)}</strong></div>
        {command && <div className="stack compact">
          {confirming && <p>{staff ? 'Завершить заказ и списать товар?' : 'Отменить заказ и снять резерв?'}</p>}
          <div className="actions">
            <button className={'button ' + (staff ? 'primary' : 'secondary')} type="button" disabled={action.pending} onClick={change}>
              {action.pending ? 'Обновляем…' : confirming ? 'Да, подтвердить' : command[1]}
            </button>
            {confirming && <button type="button" className="button secondary" disabled={action.pending} onClick={() => setConfirming(false)}>Назад</button>}
          </div>
        </div>}
        <ErrorMessage error={action.error} />
      </aside>
    </div>
  </div>;
}
