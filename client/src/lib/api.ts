import axios from "axios";

const api = axios.create();

/// Set once, by AuthTokenBridge, from inside AuthKitProvider.
///
/// The token lives in a React context and axios is a module, so one of them has
/// to reach across. This direction is the cheap one: a function reference set on
/// mount, rather than threading a client instance through every hook that makes
/// a request.
let getAccessToken: (() => Promise<string>) | null = null;

export function setAccessTokenGetter(getter: (() => Promise<string>) | null) {
  getAccessToken = getter;
}

// AuthKit refreshes the token when it is close to expiring, so asking for it per
// request costs a promise and no network call in the common case. Asking once
// and caching it here would hand back a stale token after every refresh.
api.interceptors.request.use(async (config) => {
  if (!getAccessToken) return config;

  try {
    const token = await getAccessToken();
    config.headers.Authorization = `Bearer ${token}`;
  } catch {
    // LoginRequiredError: nobody is signed in. Send the request anyway and let
    // the API answer 401, so there is one place that handles being signed out
    // rather than two.
  }

  return config;
});

export default api;
