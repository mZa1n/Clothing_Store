import { useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from './api';
import { useResource } from './data';
import { canShop, useSession } from './session';
import { ActionForm, Empty, ErrorMessage, Field, Resource, fulfillmentNames, money, namedOptions,
  option, readForm, roleNames, useAction } from './ui';

export function Cart() {
  const state = useResource('/cart');
  return <div className="stack">
    <h1>Корзина</h1>
    <Resource state={state}>{(cart) => <CartItems initialCart={cart} />}</Resource>
  </div>;
}

function CartItems({ initialCart }) {
  const [cart, setCart] = useState(initialCart);
  const action = useAction();
  if (cart.items.length === 0) return <Empty>
    <h2>Корзина пуста</h2><Link className="text-link" to="/#catalog">Перейти в каталог</Link>
  </Empty>;

  return <div className="split-layout">
    <div className="stack compact">
      {cart.items.map((item) => <article className="panel cart-line" key={item.variantId}>
        <div>
          <h2 className="small-heading">{item.productName}</h2>
          <p className="muted">{item.size} · {item.color} · {item.sku}</p>
          {!item.active && <p className="field-error">Снят с продажи</p>}
        </div>
        <strong>{money.format(item.unitPrice * item.quantity)}</strong>
        <form className="cart-controls" key={item.quantity} onSubmit={(event) => {
          const { quantity } = readForm(event);
          action.run(async () => setCart(await api('/cart/items/' + item.variantId, {
            method: 'PUT', body: { quantity: Number(quantity) },
          })));
        }}>
          <Field name="quantity" label="Количество" type="number" min={1} max={1000000}
            defaultValue={item.quantity} disabled={action.pending || !item.active} required />
          <button className="button secondary" disabled={action.pending || !item.active}>Обновить</button>
          <button className="button secondary" type="button" disabled={action.pending}
            aria-label={'Удалить ' + item.productName}
            onClick={() => action.run(async () => {
              await api('/cart/items/' + item.variantId, { method: 'DELETE' });
              setCart(await api('/cart'));
            })}>Удалить</button>
        </form>
      </article>)}
      <ErrorMessage error={action.error} />
    </div>
    <aside className="panel stack compact">
      <h2 className="small-heading">Итого</h2>
      <p className="large-price">{money.format(cart.estimatedTotal)}</p>
      {cart.items.every((item) => item.active)
        ? <div><Link className="button primary" to="/checkout">К оформлению</Link></div>
        : <p>Удалите снятые с продажи товары, чтобы продолжить.</p>}
    </aside>
  </div>;
}

export function Checkout() {
  const { account } = useSession();
  const cart = useResource('/cart');
  const locations = useResource('/catalog/locations');
  return <div className="stack">
    <Link className="text-link" to="/cart">Корзина</Link>
    <h1>Оформление заказа</h1>
    <Resource state={cart}>{(current) => current.items.length === 0
      ? <Empty><p>Корзина пуста.</p><Link className="text-link" to="/orders">Посмотреть мои заказы</Link></Empty>
      : <Resource state={locations}>{(points) => <CheckoutForm cart={current} points={points} account={account} />}</Resource>}
    </Resource>
  </div>;
}

function CheckoutForm({ cart, points, account }) {
  const navigate = useNavigate();
  const stores = points.filter((point) => point.kind === 'store');
  const hasWarehouse = points.some((point) => point.kind === 'warehouse');
  const [method, setMethod] = useState(hasWarehouse ? 'delivery' : 'pickup');
  const operation = useRef(null);

  async function submit(values) {
    const body = {
      fulfillment: method, fullName: values.fullName.trim(), phone: values.phone.trim(),
      ...(method === 'pickup' ? { pickupLocationId: Number(values.pickupLocationId) } : { address: values.address.trim() }),
    };
    const signature = JSON.stringify(body);
    if (operation.current?.signature !== signature) {
      operation.current = { signature, key: crypto.randomUUID() };
    }
    const order = await api('/orders/checkout', {
      method: 'POST', body, headers: { 'Idempotency-Key': operation.current.key },
    });
    navigate('/orders/' + order.id, { replace: true, state: { created: true } });
  }

  return <div className="split-layout">
    {(!hasWarehouse && stores.length === 0) || cart.items.some((item) => !item.active)
      ? <Empty><p>Оформление недоступно. Проверьте корзину и наличие точек получения.</p></Empty>
      : <ActionForm className="panel" onSubmit={submit} submit="Оформить заказ">
        {(error) => <>
          <Field name="fulfillment" label="Способ получения" value={method} onChange={(event) => setMethod(event.target.value)}
            options={namedOptions(fulfillmentNames).filter(({ value }) => value === 'delivery' ? hasWarehouse : stores.length > 0)} error={error} />
          <Field name="fullName" label="Имя и фамилия" defaultValue={account.fullName ?? ''}
            autoComplete="name" maxLength={120} required error={error} />
          <Field name="phone" label="Телефон" defaultValue={account.phone ?? ''} type="tel"
            autoComplete="tel" maxLength={30} required error={error} />
          {method === 'delivery'
            ? <Field name="address" label="Адрес доставки" autoComplete="street-address" maxLength={240} required error={error} />
            : <Field name="pickupLocationId" label="Магазин самовывоза" placeholder="Выберите магазин"
              options={stores.map((point) => option(point.id, point.name + ' · ' + point.address))} required error={error} />}
        </>}
      </ActionForm>}
    <aside className="panel stack compact">
      <h2 className="small-heading">Ваш заказ</h2>
      {cart.items.map((item) => <div className="line-total" key={item.variantId}>
        <span>{item.productName} × {item.quantity}</span><strong>{money.format(item.unitPrice * item.quantity)}</strong>
      </div>)}
      <div className="line-total grand-total"><span>Итого</span><strong>{money.format(cart.estimatedTotal)}</strong></div>
    </aside>
  </div>;
}

export function Profile() {
  const { account, updateAccount } = useSession();
  const [saved, setSaved] = useState(false);
  const contactKey = account.fullName + '|' + account.phone;
  return <div className="stack">
    <h1>Профиль</h1>
    <div className="panel narrow stack">
      <div>
        <p><strong>{account.email}</strong></p>
        <p className="muted">{account.roles.map((role) => roleNames[role] ?? role).join(', ')}</p>
      </div>
      {canShop(account) && <ActionForm key={contactKey} onSubmit={async (values) => {
        setSaved(false);
        const result = await api('/auth/me/contact', { method: 'PUT', body: values });
        updateAccount(result);
        setSaved(true);
      }}>{(error) => <>
        <Field name="fullName" label="Имя и фамилия" defaultValue={account.fullName ?? ''}
          autoComplete="name" maxLength={120} required error={error} />
        <Field name="phone" label="Телефон" type="tel" defaultValue={account.phone ?? ''}
          autoComplete="tel" maxLength={30} required error={error} />
      </>}</ActionForm>}
      {saved && <p className="feedback success" role="status">Контакты сохранены.</p>}
    </div>
  </div>;
}
