import { useEffect } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, Link, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { Cart, Checkout, Profile } from './buyer';
import { Catalog, Product } from './catalog';
import { Login } from './login';
import { Order, Orders } from './orders';
import { Guard, SessionProvider, canShop, useSession } from './session';
import { ErrorMessage, useAction } from './ui';
import './styles.css';

function Header() {
  const { account, loading, logout } = useSession();
  const navigate = useNavigate();
  const action = useAction();
  return <header className="site-header">
    <div className="header-inner">
      <Link className="wordmark" to="/">Магазин одежды</Link>
      <nav aria-label="Основное меню" className="main-nav">
        <NavLink to="/" end>Каталог</NavLink>
        {canShop(account) && <>
          <NavLink to="/cart">Корзина</NavLink>
          <NavLink to="/orders">Мои заказы</NavLink>
        </>}
        {account && <NavLink to="/profile">Профиль</NavLink>}
        {account
          ? <button type="button" className="nav-button" disabled={action.pending}
            onClick={() => action.run(async () => { await logout(); navigate('/'); })}>Выйти</button>
          : !loading && <NavLink to="/login">Войти</NavLink>}
      </nav>
    </div>
    {action.error && <div className="container"><ErrorMessage error={action.error} /></div>}
  </header>;
}

function App() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (hash === '#catalog') document.getElementById('catalog')?.scrollIntoView();
    else window.scrollTo(0, 0);
  }, [pathname, hash]);

  return <>
    <a className="skip-link" href="#main">К содержанию</a>
    <Header />
    <main id="main" className="container" tabIndex={-1}>
      <Routes>
        <Route path="/" element={<Catalog />} />
        <Route path="/products/:id" element={<Product />} />
        <Route path="/login" element={<Login />} />
        <Route path="/register" element={<Login register />} />
        <Route path="/cart" element={<Guard allow={canShop}><Cart /></Guard>} />
        <Route path="/checkout" element={<Guard allow={canShop}><Checkout /></Guard>} />
        <Route path="/orders" element={<Guard allow={canShop}><Orders /></Guard>} />
        <Route path="/orders/:id" element={<Guard allow={canShop}><Order /></Guard>} />
        <Route path="/profile" element={<Guard><Profile /></Guard>} />
        <Route path="*" element={<div className="empty"><h1>Страница не найдена</h1><Link to="/">Открыть каталог</Link></div>} />
      </Routes>
    </main>
    <footer className="site-footer">
      <div className="container footer-inner">
        <Link className="wordmark" to="/">Магазин одежды</Link>
        <a href="https://unsplash.com/license" className="muted">Фото витрины · Unsplash</a>
      </div>
    </footer>
  </>;
}

createRoot(document.getElementById('root')).render(
  <BrowserRouter><SessionProvider><App /></SessionProvider></BrowserRouter>,
);
