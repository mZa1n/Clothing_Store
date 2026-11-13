import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { api, query } from './api';
import { useResource } from './data';
import { canShop, useSession } from './session';
import { Empty, ErrorMessage, Field, Pagination, ProductImage, Resource, money, option, readForm,
  recordOptions, textOptions, useAction } from './ui';

const pageSize = 12;

export function Catalog() {
  const [search, setSearch] = useSearchParams();
  const page = Math.max(1, Number(search.get('page')) || 1);
  const filters = useResource('/catalog/filters');
  const products = useResource(query('/catalog/products', {
    search: search.get('search'), categoryId: search.get('categoryId'),
    manufacturerId: search.get('manufacturerId'), size: search.get('size'),
    color: search.get('color'), page, pageSize,
  }));

  function apply(event) {
    const values = readForm(event);
    setSearch(Object.fromEntries(Object.entries(values).filter(([, value]) => value.trim())));
  }

  function changePage(value) {
    const next = new URLSearchParams(search);
    if (value > 1) next.set('page', value);
    else next.delete('page');
    setSearch(next);
  }

  return <>
    <section className="catalog-cover">
      <div className="cover-content">
        <h1>Одежда на<br />каждый день</h1>
        <p>Выберите размер и цвет, а затем оформите доставку или самовывоз.</p>
        <a className="button primary" href="#catalog">Смотреть каталог</a>
      </div>
      <img src="/catalog-cover.jpg" alt="Цветные футболки на вешалках" fetchPriority="high" />
    </section>

    <section className="catalog-body" id="catalog" aria-labelledby="catalog-title">
      <div className="section-heading"><h2 id="catalog-title">Каталог</h2></div>
      <form className="filter-panel" key={search.toString()} onSubmit={apply}>
        <div className="filter-grid">
          <Field name="search" label="Поиск" type="search" maxLength={120} placeholder="Название или артикул" defaultValue={search.get('search') ?? ''} />
          <Field name="categoryId" label="Категория" defaultValue={search.get('categoryId') ?? ''}
            options={recordOptions(filters.data?.categories)} placeholder="Все" />
          <Field name="manufacturerId" label="Производитель" defaultValue={search.get('manufacturerId') ?? ''}
            options={recordOptions(filters.data?.manufacturers)} placeholder="Все" />
          <Field name="size" label="Размер" defaultValue={search.get('size') ?? ''}
            options={textOptions(filters.data?.sizes)} placeholder="Все" />
          <Field name="color" label="Цвет" defaultValue={search.get('color') ?? ''}
            options={textOptions(filters.data?.colors)} placeholder="Все" />
        </div>
        <div className="actions"><button className="button primary">Найти</button>
          {search.size > 0 && <button className="button secondary" type="button" onClick={() => setSearch({})}>Сбросить</button>}
        </div>
        <ErrorMessage error={filters.error} />
      </form>

      <Resource state={products}>{(rows) => <>
        {rows.length === 0 ? <Empty>
          <h3>{search.size ? 'Товары не найдены' : 'Каталог пока пуст'}</h3>
          <p>{search.size ? 'Попробуйте изменить параметры поиска.' : 'Товары появятся здесь после добавления ассортимента.'}</p>
        </Empty> : <div className="product-grid">
          {rows.map((product) => <Link className="product-card" key={product.id} to={'/products/' + product.id}>
            <ProductImage url={product.imageUrl} />
            <div className="product-card-info">
              <p className="muted">{product.category} · {product.brand || product.manufacturer}</p>
              <h3>{product.name}</h3>
              <strong>{money.format(product.price)}</strong>
            </div>
          </Link>)}
        </div>}
        <Pagination page={page} size={pageSize} count={rows.length} onChange={changePage} />
      </>}</Resource>
    </section>
  </>;
}

export function Product() {
  const { id } = useParams();
  const product = useResource('/catalog/products/' + encodeURIComponent(id));
  const locations = useResource('/catalog/locations');
  return <div className="stack">
    <Link className="text-link" to="/#catalog">Каталог</Link>
    <Resource state={product}>{(item) => <ProductDetails key={item.id} product={item} locations={locations} />}</Resource>
  </div>;
}

function ProductDetails({ product, locations }) {
  const { account } = useSession();
  const [variantId, setVariantId] = useState(String(product.variants.find((v) => v.availableAt.length)?.id ?? product.variants[0]?.id ?? ''));
  const [added, setAdded] = useState(false);
  const selection = product.variants.find((variant) => String(variant.id) === variantId);
  const action = useAction();
  return <div className="product-detail">
    <ProductImage url={product.imageUrl} alt={product.name} />
    <div className="stack">
      <div>
        <p className="muted">{product.category.name} · {product.brand?.name || product.manufacturer.name}</p>
        <h1>{product.name}</h1>
        <p className="large-price">{money.format(product.price)}</p>
        <p className="description">{product.description}</p>
      </div>
      <div className="stack compact">
        <Field name="variant" label="Размер и цвет" value={variantId} disabled={!product.variants.length || action.pending}
          onChange={(event) => { setVariantId(event.target.value); setAdded(false); }}
          options={product.variants.map((variant) => option(variant.id,
            variant.size + ' · ' + variant.color + (variant.availableAt.length ? '' : ' · нет в наличии')))} />
        {selection && <p className="muted">Артикул {selection.sku}</p>}
        <Resource state={locations}>{(rows) => selection && <div>
          <h2 className="small-heading">Наличие</h2>
          {selection.availableAt.length ? <ul className="plain-list">{rows.filter((point) => selection.availableAt.includes(point.id)).map((point) =>
            <li key={point.id}>{point.kind === 'warehouse' ? 'Доставка со склада' : point.name + ' · ' + point.address}</li>)}</ul>
            : <p>Нет в наличии</p>}
        </div>}</Resource>
        {canShop(account) && <div>
          <button className="button primary" disabled={!selection?.availableAt.length || action.pending}
            onClick={() => action.run(async () => {
              setAdded(false);
              const cart = await api('/cart');
              const quantity = (cart.items.find((item) => item.variantId === selection.id)?.quantity ?? 0) + 1;
              await api('/cart/items/' + selection.id, { method: 'PUT', body: { quantity } });
              setAdded(true);
            })}>{action.pending ? 'Добавляем…' : 'В корзину'}</button>
          {added && <p className="feedback success" role="status">Товар в корзине. <Link to="/cart">Открыть корзину</Link></p>}
        </div>}
        {!account && <Link className="button primary" to="/login" state={{ from: '/products/' + product.id }}>Войти для покупки</Link>}
        <ErrorMessage error={action.error} />
      </div>
      <details className="details"><summary>Размерная сетка</summary>
        <dl>{product.sizeGrid.sizes.map((size) => <div key={size.id}><dt>{size.label}</dt><dd>{size.measurements}</dd></div>)}</dl>
      </details>
    </div>
  </div>;
}
