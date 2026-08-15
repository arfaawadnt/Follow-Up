export const environment = {
  production: false,
  // Proxied to the API on :5088 by proxy.conf.json in dev; same-origin when served from the API.
  apiBase: '/api/v1',
  hubBase: '/hubs',
};
