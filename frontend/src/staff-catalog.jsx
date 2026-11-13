import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { api, query } from './api';
import { useResource } from './data';
import { ActionForm, Check, Empty, Field, Pagination, Resource, Table, money, namedOptions, recordOptions } from './ui';

const catalog = '/management/catalog';
const pageSize = 15;
const dictionaryNames = {
  categories: 'Категории', brands: 'Бренды', manufacturers: 'Производители', 'size-grids': 'Размерные сетки',
};

export function StaffCatalog() {
  const [page, setPage] = useState(1);
  const products = useResource(query(catalog + '/products', { page, pageSize }));
  return <div className="stack">
    <div className="actions">
      <Link className="button primary" to={catalog + '/new'}>Добавить товар</Link>
      <Link className="button secondary" to={catalog + '/references'}>Справочники</Link>
    </div>
    <Resource state={products}>{(rows) => <>
      {rows.length ? <Table headers={['Товар', 'Категория', 'Производитель', 'Цена', 'Состояние']}>
        {rows.map((row) => <tr key={row.id}>
          <td><Link to={catalog + '/' + row.id}>{row.name}</Link></td>
          <td>{row.category}</td><td>{row.manufacturer}</td><td>{money.format(row.price)}</td>
          <td>{row.active ? 'Активен' : 'Неактивен'}</td>
        </tr>)}
      </Table> : <Empty><p>Товаров пока нет. Сначала заведите категорию, производителя и размерную сетку в справочниках.</p></Empty>}
      <Pagination page={page} size={pageSize} count={rows.length} onChange={setPage} />
    </>}</Resource>
  </div>;
}

export function References() {
  const [kind, setKind] = useState('categories');
  return <div className="stack">
    <Link className="text-link" to={catalog}>Товары</Link>
    <div className="narrow">
      <Field name="dictionary" label="Справочник" value={kind} options={namedOptions(dictionaryNames)}
        onChange={(event) => setKind(event.target.value)} />
    </div>
    <Dictionary key={kind} kind={kind} />
  </div>;
}

function Dictionary({ kind }) {
  const endpoint = catalog + '/' + kind;
  const records = useResource(endpoint);
  const [id, setId] = useState('');
  const [message, setMessage] = useState('');
  return <div className="stack">
    {message && <p className="feedback success" role="status">{message}</p>}
    <Resource state={records}>{(rows) => {
      const row = rows.find((entry) => String(entry.id) === id);
      return <>
        <div className="narrow">
          <Field name="entry" label="Запись" value={id} placeholder="Новая запись" options={recordOptions(rows)}
            onChange={(event) => { setId(event.target.value); setMessage(''); }} />
        </div>
        <ActionForm className="panel narrow" key={row?.id ?? 'new'} submit={row ? 'Сохранить запись' : 'Добавить запись'}
          onSubmit={async (values) => {
            const body = { name: values.name.trim() };
            if (kind === 'manufacturers') body.country = values.country.trim();
            if (kind === 'manufacturers' || kind === 'size-grids') body.active = Boolean(values.active);
            const saved = await api(endpoint + (row ? '/' + row.id : ''), { method: row ? 'PUT' : 'POST', body });
            setId(String(saved.id));
            setMessage('Запись сохранена.');
            records.reload();
          }}>{(error) => <>
            <Field name="name" label="Название" defaultValue={row?.name} maxLength={120} required error={error} />
            {kind === 'manufacturers' && <Field name="country" label="Страна" defaultValue={row?.country}
              maxLength={80} required error={error} />}
            {(kind === 'manufacturers' || kind === 'size-grids') &&
              <Check name="active" label="Запись активна" defaultChecked={row?.active ?? true} />}
          </>}</ActionForm>
        {kind === 'size-grids' && row && <div className="stack">
          <h2>Размеры · {row.name}</h2>
          {row.sizes.map((size) => <details className="panel" key={size.id}>
            <summary>{size.label} · {size.measurements}</summary>
            <SizeForm row={row} size={size} refresh={records.reload} />
          </details>)}
          <details className="panel"><summary>Добавить размер</summary>
            <SizeForm row={row} refresh={records.reload} />
          </details>
        </div>}
      </>;
    }}</Resource>
  </div>;
}

function SizeForm({ row, size, refresh }) {
  const endpoint = catalog + '/size-grids/' + row.id + '/sizes';
  return <ActionForm className="nested-form" onSubmit={async (values) => {
    await api(endpoint + (size ? '/' + size.id : ''), { method: size ? 'PUT' : 'POST', body: values });
    refresh();
  }} submit={size ? 'Сохранить размер' : 'Добавить размер'}>
    {(error) => <>
      <Field name="label" label="Обозначение размера" defaultValue={size?.label} maxLength={24} required error={error} />
      <Field name="measurements" label="Мерки" defaultValue={size?.measurements} maxLength={240} required error={error} />
    </>}
  </ActionForm>;
}

export function ProductEditor() {
  const { id } = useParams();
  const product = useResource(id ? catalog + '/products/' + encodeURIComponent(id) : null);
  const category = useResource(catalog + '/categories');
  const manufacturers = useResource(catalog + '/manufacturers');
  const brands = useResource(catalog + '/brands');
  const grids = useResource(catalog + '/size-grids');
  const dependencies = [product, category, manufacturers, brands, grids];
  const waiting = dependencies.find((state) => state.error) ?? dependencies.find((state) => state.loading);
  return <div className="stack">
    <div className="actions"><Link className="text-link" to={catalog}>Товары</Link>
      <Link className="text-link" to={catalog + '/references'}>Справочники</Link></div>
    {waiting ? <Resource state={waiting}>{() => null}</Resource> :
      <ProductEditorContents key={id ?? 'new'} product={product.data} refresh={product.reload}
        references={{ categories: category.data, manufacturers: manufacturers.data, brands: brands.data, grids: grids.data }} />}
  </div>;
}

function ProductEditorContents({ product: initial, refresh, references }) {
  const [product, setProduct] = useState(initial);
  const [saved, setSaved] = useState(false);
  const navigate = useNavigate();
  const ready = references.categories.length && references.manufacturers.length && references.grids.length;
  if (!ready) return <Empty>
    <p>Для карточки товара нужна категория, производитель и размерная сетка.</p>
    <Link className="text-link" to={catalog + '/references'}>Открыть справочники</Link>
  </Empty>;

  return <div className="stack">
    <ActionForm className="panel" submit={product ? 'Сохранить товар' : 'Создать товар'}
      onSubmit={async (values) => {
        setSaved(false);
        const body = {
          name: values.name.trim(), description: values.description.trim(), imageUrl: values.imageUrl.trim() || null,
          price: Number(values.price), categoryId: Number(values.categoryId), manufacturerId: Number(values.manufacturerId),
          sizeGridId: product?.sizeGrid.id ?? Number(values.sizeGridId), brandId: Number(values.brandId) || null, active: Boolean(values.active),
        };
        const result = await api(catalog + '/products' + (product ? '/' + product.id : ''),
          { method: product ? 'PUT' : 'POST', body });
        if (!product) navigate(catalog + '/' + result.id, { replace: true });
        else { setProduct(result); setSaved(true); }
      }}>{(error) => <>
        <h2>{product ? product.name : 'Новый товар'}</h2>
        <div className="field-grid">
          <Field name="name" label="Название товара" defaultValue={product?.name} maxLength={120} required error={error} />
          <Field name="price" label="Цена, ₽" defaultValue={product?.price} type="number" min=".01" max="9999999999.99" step=".01" required error={error} />
          <Field name="categoryId" label="Категория" defaultValue={product?.category.id ?? ''} options={recordOptions(references.categories)}
            placeholder="Выберите категорию" required error={error} />
          <Field name="manufacturerId" label="Производитель" defaultValue={product?.manufacturer.id ?? ''} options={recordOptions(references.manufacturers)}
            placeholder="Выберите производителя" required error={error} />
          <Field name="brandId" label="Бренд" defaultValue={product?.brand?.id ?? ''} options={recordOptions(references.brands)} placeholder="Без бренда" error={error} />
          <Field name="sizeGridId" label="Размерная сетка" defaultValue={product?.sizeGrid.id ?? ''} options={recordOptions(references.grids)}
            placeholder="Выберите сетку" disabled={Boolean(product)} required error={error} />
        </div>
        <Field name="imageUrl" label="URL фотографии" type="url" defaultValue={product?.imageUrl ?? ''} maxLength={2048} error={error} />
        <Field name="description" label="Описание" defaultValue={product?.description} maxLength={4000} multiline required error={error} />
        <Check name="active" label="Товар активен" defaultChecked={product?.active ?? true} />
      </>}</ActionForm>

    {saved && <p className="feedback success" role="status">Карточка сохранена.</p>}
    {product && <div className="stack">
      <h2>Варианты</h2>
      {!product.sizeGrid.sizes.length && <p>Добавьте размер в сетку в разделе «Справочники».</p>}
      {product.variants.map((variant) => <details key={variant.id} className="panel">
        <summary>{variant.sku} · {variant.size} · {variant.color} · ID {variant.id}{!variant.active && ' · неактивен'}</summary>
        <VariantForm product={product} variant={variant} refresh={refresh} />
      </details>)}
      {product.sizeGrid.sizes.length > 0 && <details className="panel"><summary>Добавить вариант</summary>
        <VariantForm product={product} refresh={refresh} />
      </details>}
    </div>}
  </div>;
}

function VariantForm({ product, variant, refresh }) {
  const endpoint = catalog + '/products/' + product.id + '/variants';
  return <ActionForm className="nested-form" onSubmit={async (values) => {
    const body = { sku: values.sku, color: values.color, gridSizeId: Number(values.gridSizeId), active: Boolean(values.active) };
    await api(endpoint + (variant ? '/' + variant.id : ''), { method: variant ? 'PUT' : 'POST', body });
    refresh();
  }} submit={variant ? 'Сохранить вариант' : 'Добавить вариант'}>
    {(error) => <>
      <div className="field-grid">
        <Field name="sku" label="Артикул" defaultValue={variant?.sku} maxLength={64} required error={error} />
        <Field name="color" label="Цвет" defaultValue={variant?.color} maxLength={60} required error={error} />
        <Field name="gridSizeId" label="Размер" defaultValue={variant?.gridSizeId ?? ''}
          options={recordOptions(product.sizeGrid.sizes, 'label')} placeholder="Выберите размер" required error={error} />
      </div>
      <Check name="active" label="Вариант активен" defaultChecked={variant?.active ?? true} />
    </>}
  </ActionForm>;
}
