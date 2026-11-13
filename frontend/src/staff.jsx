import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { isAdmin, useSession } from './session';
import { Accounts } from './staff-reports';

const staffTabs = [
  ['/management/orders', 'Заказы'],
  ['/management/catalog', 'Товары'],
  ['/management/inventory', 'Остатки'],
  ['/management/reports', 'Продажи'],
];
const adminTabs = [
  ['/management/accounts', 'Пользователи'],
  ['/management/comparison', 'Продажи по точкам'],
];

export function Management() {
  const { account } = useSession();
  const { pathname } = useLocation();
  const tabs = isAdmin(account) ? [...staffTabs, ...adminTabs] : staffTabs;
  const title = tabs.find(([path]) => pathname === path || pathname.startsWith(path + '/'))?.[1];
  return <div className="stack">
    <nav className="staff-nav" aria-label="Разделы управления">
      {tabs.map(([path, label]) => <NavLink key={path} to={path}>{label}</NavLink>)}
    </nav>
    {title && <h1>{title}</h1>}
    <Outlet />
  </div>;
}

export function ManageAccounts() {
  const { account } = useSession();
  return <Accounts currentId={account.id} />;
}
