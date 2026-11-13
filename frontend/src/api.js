class ApiError extends Error {
  constructor(status, problem) {
    const messages = {
      400: 'Проверьте введённые данные и повторите запрос.',
      401: 'Войдите, чтобы продолжить.',
      403: 'У вас нет доступа к этому разделу.',
      404: 'Запись не найдена.',
      409: 'Данные изменились. Проверьте их и повторите действие.',
      500: 'Сервис временно недоступен. Попробуйте ещё раз.',
      502: 'Бэкенд недоступен. Проверьте, что API запущен.',
    };
    const title = problem?.title;
    super(title && /[А-Яа-яЁё]/.test(title) ? title : messages[status] ?? 'Не удалось выполнить запрос.');
    this.status = status;
    this.fields = Object.fromEntries(
      Object.entries(problem?.errors ?? {}).map(([field, messages]) => [
        field.replace(/^\$\./, '').toLowerCase(), messages.join(' '),
      ]),
    );
  }
}

let csrf;

export function clearCsrf() {
  csrf = undefined;
}

async function send(path, { method = 'GET', body, signal, headers = {} } = {}) {
  let response;
  try {
    response = await fetch('/api' + path, {
      method,
      credentials: 'same-origin',
      headers: { Accept: 'application/json', ...headers, ...(body == null ? {} : { 'Content-Type': 'application/json' }) },
      body: body == null ? undefined : JSON.stringify(body),
      signal,
    });
  } catch (error) {
    if (error.name === 'AbortError') throw error;
    throw new Error('Не удалось связаться с сервером. Проверьте подключение и повторите запрос.');
  }

  const json = response.headers.get('Content-Type')?.includes('json');
  const data = json ? await response.json() : null;
  if (!response.ok) {
    if (response.status === 401) clearCsrf();
    throw new ApiError(response.status, data);
  }
  if (response.status !== 204 && !json) throw new ApiError(502);
  return data;
}

export async function api(path, options = {}) {
  if (!options.method || options.method === 'GET') return send(path, options);

  csrf ??= send('/auth/csrf').then(({ token }) => token).catch((error) => {
    clearCsrf();
    throw error;
  });

  try {
    return await send(path, {
      ...options, headers: { ...options.headers, 'X-CSRF-TOKEN': await csrf },
    });
  } catch (error) {
    if (error.status === 400 && Object.keys(error.fields).length === 0) clearCsrf();
    throw error;
  }
}

export function query(path, values) {
  const params = new URLSearchParams();
  Object.entries(values).forEach(([key, value]) => {
    if (value !== '' && value != null) params.set(key, value);
  });
  return params.size ? path + '?' + params : path;
}
